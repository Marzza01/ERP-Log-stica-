using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Demuestra en PostgreSQL real la unicidad 1:1 y la exclusión de desembolsos
/// concurrentes. Crea y elimina SU propia base local; nunca toca Neon.
/// </summary>
public sealed class LiquidacionPostgresFin02Tests : IAsyncLifetime
{
    private readonly string _database = "ci_fin02_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FIN-02 requiere PostgreSQL local en CI.");
            return;
        }

        var connection = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                connection.Host, StringComparer.OrdinalIgnoreCase) ||
            connection.Port != 5432 ||
            !string.Equals(connection.Database, "postgres", StringComparison.Ordinal) ||
            !string.Equals(connection.Username, "postgres", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(connection.Password))
            throw new InvalidOperationException("FIN-02 admite solo postgres@localhost:5432/postgres.");

        _adminConnection = connection.ConnectionString;
        connection.Database = _database;
        _testConnection = connection.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{_database}\"";
            await command.ExecuteNonQueryAsync();
            _created = true;
        }

        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    private FinanceDbContext NewContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!).Options);

    private static LiquidacionService Service(FinanceDbContext db) =>
        new(db, new ContabilidadService(db, NullLogger<ContabilidadService>.Instance),
            NullLogger<LiquidacionService>.Instance, new PaymentOperationsMode(false));

    private async Task<(Guid SellerId, Guid SplitId)> SeedAsync()
    {
        var seller = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var db = NewContext();
        var order = new Orden
        {
            Id = orderId,
            NumeroOrden = "FIN02-" + Guid.NewGuid().ToString("N"),
            VendedorId = seller,
            CompradorId = Guid.NewGuid(),
            MontoTotal = 100m,
            SubTotal = 100m,
            Moneda = "USD",
            Estado = EstadoOrden.Entregada,
            TipoCliente = TipoCliente.B2C
        };
        var split = new SplitPago
        {
            Id = Guid.NewGuid(),
            Orden = order,
            OrdenId = orderId,
            MontoTotal = 100m,
            MontoVendedor = 90m,
            MontoMarketplace = 10m,
            TransaccionId = "SIM-" + Guid.NewGuid().ToString("N")
        };
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();
        return (seller, split.Id);
    }

    [Fact]
    public async Task PostgreSQL_DosGeneracionesDelMismoSplit_SoloUnaPersiste()
    {
        if (!_created) return;
        var (seller, splitId) = await SeedAsync();

        async Task<bool> Intentar()
        {
            await using var db = NewContext();
            try
            {
                var l = await Service(db).GenerarLiquidacionAsync(seller);
                Assert.Equal(splitId, l.SplitPagoId);
                Assert.Equal(90m, l.MontoLiquidar);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        var resultados = await Task.WhenAll(Intentar(), Intentar());
        Assert.Single(resultados, x => x);
        await using var verify = NewContext();
        var liquidacion = await verify.Liquidaciones.SingleAsync();
        Assert.Equal(splitId, liquidacion.SplitPagoId);
        Assert.Equal(90m, liquidacion.MontoLiquidar);
    }

    [Fact]
    public async Task PostgreSQL_DosProcesamientosDelMismoId_SoloUnAsientoSeConfirma()
    {
        if (!_created) return;
        var (seller, splitId) = await SeedAsync();
        Guid liqId;
        await using (var db = NewContext())
        {
            var liquidacion = await Service(db).GenerarLiquidacionAsync(seller);
            liqId = liquidacion.Id;
            Assert.Equal(splitId, liquidacion.SplitPagoId);
        }

        async Task<bool> Procesar()
        {
            await using var db = NewContext();
            try
            {
                var result = await Service(db).ProcesarPagoAsync(liqId);
                Assert.Equal(EstadoLiquidacion.Completada, result.Estado);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        var resultados = await Task.WhenAll(Procesar(), Procesar());
        Assert.Single(resultados, x => x);

        await using var verify = NewContext();
        var completed = await verify.Liquidaciones.SingleAsync(l => l.Id == liqId);
        Assert.Equal(EstadoLiquidacion.Completada, completed.Estado);
        var journal = await verify.PartidasContables.Include(p => p.Lineas)
            .Where(p => p.TipoOperacion == "Liquidacion")
            .ToListAsync();
        var part = Assert.Single(journal);
        Assert.Equal(liqId, part.ReferenciaOrigenId);
        Assert.Equal(90m, part.TotalDebe);
        Assert.Equal(part.TotalDebe, part.TotalHaber);
        Assert.Equal(2, part.Lineas.Count);
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null ||
            !_database.StartsWith("ci_fin02_", StringComparison.Ordinal))
            return;
        if (_testConnection is not null)
        {
            using var ephemeral = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(ephemeral);
        }
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        _created = false;
    }
}
