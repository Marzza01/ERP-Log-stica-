using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// FIN09 expand/backfill: crea una base LOCAL temporal distinta por prueba.
/// Nunca accede a staging, Neon o cuentas de pago reales.
/// </summary>
public sealed class Fin09MigracionBackfillPostgresQaTests : IAsyncLifetime
{
    private const string PreFin09 = "20261110024500_FIN08UnicidadAsientosVentaLiquidacion";
    private readonly string _name = "ci_fin09_" + Guid.NewGuid().ToString("N");
    private string? _admin;
    private string? _dbConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("FIN09 exige PostgreSQL local en TEST_PG_CONNECTION");
        var connection = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                connection.Host, StringComparer.OrdinalIgnoreCase) ||
            connection.Port != 5432 || connection.Database != "postgres" ||
            connection.Username != "postgres" || string.IsNullOrWhiteSpace(connection.Password))
            throw new InvalidOperationException("FIN09 solo permite postgres@localhost:5432/postgres");
        _admin = connection.ConnectionString;
        connection.Database = _name;
        _dbConnection = connection.ConnectionString;
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_name}\"";
        await cmd.ExecuteNonQueryAsync();
        _created = true;
    }

    private FinanceDbContext Context() => new(
        new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(_dbConnection!).Options);

    private async Task<(Guid Orden, Guid Split, Guid Liquidacion)> SeedLegacyAsync(bool incompatible)
    {
        // SQL directo porque las clases actuales YA contienen LiquidacionId y la
        // base se ha dejado deliberadamente en el esquema antiguo FIN08.
        var orden = Guid.NewGuid();
        var split = Guid.NewGuid();
        var liq = Guid.NewGuid();
        await using var c = new NpgsqlConnection(_dbConnection);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO finance.ordenes
                ("Id","NumeroOrden","CompradorId","VendedorId","SubTotal",
                 "MontoTotal","Moneda","Estado","TipoCliente",
                 "NumeroComprobanteFiscal","GuiaEnvio","DireccionEntrega")
            VALUES (@orden,@numero,@comprador,@vendedor,100,100,'USD','Entregada','B2C',
                    'FIN09-FISCAL-QA','FIN09-GUIA-QA','Direccion de prueba QA');
            INSERT INTO finance.splits_pago
                ("Id","OrdenId","MontoTotal","MontoVendedor","MontoMarketplace","TransaccionId")
            VALUES (@split,@orden,100,90,10,@transaccion);
            INSERT INTO finance.liquidaciones
                ("Id","VendedorId","SplitPagoId","MontoLiquidar","Moneda","Estado")
            VALUES (@liquidacion,@vendedor,@split,@monto,'USD','Pendiente');
            """;
        cmd.Parameters.AddWithValue("orden", orden);
        cmd.Parameters.AddWithValue("numero", "FIN09-HIST-" + Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("comprador", Guid.NewGuid());
        cmd.Parameters.AddWithValue("vendedor", Guid.NewGuid());
        cmd.Parameters.AddWithValue("split", split);
        cmd.Parameters.AddWithValue("liquidacion", liq);
        cmd.Parameters.AddWithValue("transaccion", "SIM-FIN09-" + Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("monto", incompatible ? 91m : 90m);
        await cmd.ExecuteNonQueryAsync();
        return (orden, split, liq);
    }

    [Fact]
    public async Task BaseLegadaValida_SeVinculaSinCambiarIdImporteYRevierte1a1()
    {
        await using (var old = Context())
            await old.GetService<IMigrator>().MigrateAsync(PreFin09);
        var ids = await SeedLegacyAsync(false);

        await using (var upgraded = Context())
            await upgraded.Database.MigrateAsync();

        await using (var verify = Context())
        {
            var split = await verify.SplitsPago.AsNoTracking().SingleAsync(s => s.Id == ids.Split);
            var liquidacion = await verify.Liquidaciones.AsNoTracking()
                .SingleAsync(l => l.Id == ids.Liquidacion);
            Assert.Equal(ids.Liquidacion, split.LiquidacionId);
            Assert.Equal(ids.Split, liquidacion.SplitPagoId);
            Assert.Equal(90m, liquidacion.MontoLiquidar);
            Assert.Equal(EstadoLiquidacion.Pendiente, liquidacion.Estado);
            Assert.Equal(1, await verify.Liquidaciones.CountAsync());
        }

        // Reversión permitida sólo si todos los lotes siguen siendo 1:1.
        await using (var rollback = Context())
            await rollback.GetService<IMigrator>().MigrateAsync(PreFin09);

        await using var conn = new NpgsqlConnection(_dbConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM finance.liquidaciones
            WHERE "Id"=@id AND "SplitPagoId"=@split AND "MontoLiquidar"=90;
            """;
        cmd.Parameters.AddWithValue("id", ids.Liquidacion);
        cmd.Parameters.AddWithValue("split", ids.Split);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task BaseLegadaConImporteInconsistente_FallaSinBackfillNiBorrado()
    {
        await using (var old = Context())
            await old.GetService<IMigrator>().MigrateAsync(PreFin09);
        var ids = await SeedLegacyAsync(true);
        var problem = await Record.ExceptionAsync(async () =>
        {
            await using var upgrade = Context();
            await upgrade.Database.MigrateAsync();
        });
        Assert.NotNull(problem);
        var pg = FindPostgres(problem!);
        Assert.NotNull(pg);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pg!.SqlState);
        Assert.Contains("FIN09", pg.Message);

        await using var conn = new NpgsqlConnection(_dbConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM finance.liquidaciones
             WHERE "Id"=@id AND "MontoLiquidar"=91;
            """;
        cmd.Parameters.AddWithValue("id", ids.Liquidacion);
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    private static PostgresException? FindPostgres(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException p) return p;
        return null;
    }

    public async Task DisposeAsync()
    {
        if (!_created || _admin is null || _dbConnection is null ||
            !_name.StartsWith("ci_fin09_", StringComparison.Ordinal)) return;
        using (var p = new NpgsqlConnection(_dbConnection))
            NpgsqlConnection.ClearPool(p);
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
        _created = false;
    }
}