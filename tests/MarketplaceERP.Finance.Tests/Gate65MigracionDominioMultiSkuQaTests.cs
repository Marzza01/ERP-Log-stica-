using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Fase2: prueba obligatoria en PostgreSQL real LOCAL con un nombre de base
/// parecido al de staging. No conecta a Neon ni altera bases no efímeras.
/// </summary>
public sealed class Gate65MigracionDominioMultiSkuQaTests : IAsyncLifetime
{
    private const string Fin07 = "20261107022000_FIN07DetalleFacturaProveedor";
    private const string MultiSku = "20261108060000_IntentoReservasMultiSku";
    private readonly string _dbName = "gate65_staging_" + Guid.NewGuid().ToString("N");
    private string? _admin, _connection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("TEST_PG_CONNECTION no configurada");
        var cfg = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(cfg.Host,
                StringComparer.OrdinalIgnoreCase)
            || cfg.Port != 5432 || cfg.Database != "postgres"
            || cfg.Username != "postgres" || string.IsNullOrWhiteSpace(cfg.Password))
            throw new InvalidOperationException("SOLO_POSTGRES_LOCAL_QA");

        _admin = cfg.ConnectionString;
        cfg.Database = _dbName;
        _connection = cfg.ConnectionString;
        await using var conn = new NpgsqlConnection(_admin);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
        await cmd.ExecuteNonQueryAsync();
        _created = true;
    }

    private FinanceDbContext Context() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_connection!, o =>
                o.MigrationsHistoryTable("__EFMigrationsHistory", "finance")).Options);

    [Fact]
    public async Task NombreTipoStaging_SubeDesdeFin07_ConservaReservaHistoricaYNoQA()
    {
        await using (var context = Context())
        {
            var list = context.Database.GetMigrations().ToArray();
            Assert.Contains(MultiSku, list);
            Assert.DoesNotContain("20261108060000_QaMultiSkuIntentoReservas", list);
            await context.GetService<IMigrator>().MigrateAsync(Fin07);
        }

        var idOrden = Guid.NewGuid();
        var idIntento = Guid.NewGuid();
        var idReserva = Guid.NewGuid();
        var idProducto = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_connection))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO finance.ordenes
                  ("Id","NumeroOrden","CompradorId","VendedorId","SubTotal",
                   "MontoTotal","Moneda","Estado","TipoCliente",
                   "NumeroComprobanteFiscal","GuiaEnvio","DireccionEntrega")
                VALUES (@orden,@numero,@comprador,@vendedor,50,50,'USD','Entregada','B2C',
                        'GATE65-FISCAL','GATE65-GUIA','Solo ensayo local');
                INSERT INTO finance.items_orden
                  ("Id","OrdenId","ProductoId","SkuReferencia","NombreProducto",
                   "Cantidad","PrecioUnitario")
                VALUES (@item,@orden,@producto,'GATE65-TEST','Producto sintético',1,50);
                INSERT INTO finance.intentos_pago
                  ("Id","OrdenId","ReferenciaOperacion","Monto","Moneda","Estado",
                   "CreadoUtc","ActualizadoUtc","ReservaId")
                VALUES (@intento,@orden,@referencia,50,'USD','Preparado',
                        now(),now(),@reserva);
                """;
            cmd.Parameters.AddWithValue("orden", idOrden);
            cmd.Parameters.AddWithValue("numero", "GATE65-" + idOrden.ToString("N"));
            cmd.Parameters.AddWithValue("comprador", Guid.NewGuid());
            cmd.Parameters.AddWithValue("vendedor", Guid.NewGuid());
            cmd.Parameters.AddWithValue("item", Guid.NewGuid());
            cmd.Parameters.AddWithValue("producto", idProducto);
            cmd.Parameters.AddWithValue("intento", idIntento);
            cmd.Parameters.AddWithValue("referencia", "PAY-" + idOrden.ToString("N"));
            cmd.Parameters.AddWithValue("reserva", idReserva);
            Assert.Equal(3, await cmd.ExecuteNonQueryAsync());
        }

        await using (var upgrade = Context())
            await upgrade.GetService<IMigrator>().MigrateAsync(MultiSku);

        await using (var conn = new NpgsqlConnection(_connection))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM finance.intento_reservas_pago
                   WHERE "IntentoPagoId"=@intento AND "ReservaId"=@reserva
                     AND "ProductoId"=@producto AND "Cantidad"=1
                     AND "Procedencia"='LegacySinVerificar'),
                  (SELECT COUNT(*) FROM finance.intentos_pago
                   WHERE "Id"=@intento AND "ReservaId"=@reserva),
                  (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                   WHERE "MigrationId"=@migration),
                  (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                   WHERE "MigrationId"='20261108060000_QaMultiSkuIntentoReservas');
                """;
            cmd.Parameters.AddWithValue("intento", idIntento);
            cmd.Parameters.AddWithValue("reserva", idReserva);
            cmd.Parameters.AddWithValue("producto", idProducto);
            cmd.Parameters.AddWithValue("migration", MultiSku);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(0L, reader.GetInt64(3));
        }

        // Este rollback no debe borrar los vínculos financieros durablemente.
        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var down = Context();
            await down.GetService<IMigrator>().MigrateAsync(Fin07);
        });
        Assert.NotNull(ex);
        Assert.Contains("CHECKOUT_MULTI_SKU_DOWN_BLOQUEADO", ex.ToString());
    }

    [Fact]
    public async Task DobleHistorial_ConQAEnPublic_BloqueaMigracionComercialSinPerderDatos()
    {
        // La aplicacion usa finance; un historial QA antiguo en public
        // debe bloquear igualmente la migracion comercial.
        await using (var initial = Context())
            await initial.GetService<IMigrator>().MigrateAsync(Fin07);

        await using (var conn = new NpgsqlConnection(_connection))
        {
            await conn.OpenAsync();
            await using var create = conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE public."__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL PRIMARY KEY,
                    "ProductVersion" character varying(32) NOT NULL
                );
                INSERT INTO public."__EFMigrationsHistory"
                    ("MigrationId", "ProductVersion")
                VALUES ('20261108060000_QaMultiSkuIntentoReservas','8.0.8');
                """;
            await create.ExecuteNonQueryAsync();
        }

        var failure = await Record.ExceptionAsync(async () =>
        {
            await using var upgrade = Context();
            await upgrade.GetService<IMigrator>().MigrateAsync(MultiSku);
        });
        Assert.NotNull(failure);
        Assert.Contains("CHECKOUT_MIGRATION_CONFLICT_QA_HISTORY", failure!.ToString());

        await using var verify = new NpgsqlConnection(_connection);
        await verify.OpenAsync();
        await using var check = verify.CreateCommand();
        check.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM public."__EFMigrationsHistory"
                 WHERE "MigrationId"='20261108060000_QaMultiSkuIntentoReservas'),
                to_regclass('finance.intento_reservas_pago') IS NULL,
                (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                 WHERE "MigrationId"='20261108060000_IntentoReservasMultiSku');
            """;
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal(0L, reader.GetInt64(2));
    }

    public async Task DisposeAsync()
    {
        if (!_created || _admin is null || _connection is null
            || !_dbName.StartsWith("gate65_staging_", StringComparison.Ordinal))
            return;
        using (var c = new NpgsqlConnection(_connection)) NpgsqlConnection.ClearPool(c);
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
        _created = false;
    }
}
