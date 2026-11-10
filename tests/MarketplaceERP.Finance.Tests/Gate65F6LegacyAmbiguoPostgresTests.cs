using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Valida historia de reservas duplicadas y orden multi-SKU ambigua.
/// PostgreSQL LOCAL y DBs efimeras. Sin Neon ni pagos.
/// </summary>
public sealed class Gate65F6LegacyAmbiguoPostgresTests : IAsyncLifetime
{
    private const string Fin07 = "20261107022000_FIN07DetalleFacturaProveedor";
    private const string Checkout = "20261108060000_IntentoReservasMultiSku";
    private readonly string _db = "gate65_f6_" + Guid.NewGuid().ToString("N");
    private string? _admin, _conn;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("PG QA local requerida.");
        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(cs.Host, StringComparer.OrdinalIgnoreCase)
            || cs.Port != 5432 || cs.Database != "postgres"
            || cs.Username != "postgres" || string.IsNullOrWhiteSpace(cs.Password))
            throw new InvalidOperationException("GATE65_F6_SOLO_POSTGRES_LOCAL");

        _admin = cs.ConnectionString;
        cs.Database = _db;
        _conn = cs.ConnectionString;

        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_db}\"";
        await command.ExecuteNonQueryAsync();
        _created = true;
    }

    private FinanceDbContext Context() =>
        new(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_conn!, o =>
                o.MigrationsHistoryTable("__EFMigrationsHistory", "finance")).Options);

    private async Task SeedOrder(Guid order, Guid product, Guid attempt,
        Guid reservation, int items)
    {
        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO finance.ordenes
              ("Id","NumeroOrden","CompradorId","VendedorId","SubTotal",
               "MontoTotal","Moneda","Estado","TipoCliente",
               "NumeroComprobanteFiscal","GuiaEnvio","DireccionEntrega")
            VALUES (@order,@number,@buyer,@seller,45,45,'USD','Entregada','B2C',
                    'F6-FISCAL','F6-GUIA','Datos sinteticos');
            INSERT INTO finance.items_orden
              ("Id","OrdenId","ProductoId","SkuReferencia","NombreProducto",
               "Cantidad","PrecioUnitario")
            VALUES (@item,@order,@product,'F6-SKU-A','Producto QA A',1,45);
            INSERT INTO finance.intentos_pago
              ("Id","OrdenId","ReferenciaOperacion","Monto","Moneda","Estado",
               "CreadoUtc","ActualizadoUtc","ReservaId")
            VALUES (@attempt,@order,@ref,45,'USD','Preparado',now(),now(),@reserve);
            """;
        cmd.Parameters.AddWithValue("order", order);
        cmd.Parameters.AddWithValue("number", "GATE65-F6-" + order.ToString("N"));
        cmd.Parameters.AddWithValue("buyer", Guid.NewGuid());
        cmd.Parameters.AddWithValue("seller", Guid.NewGuid());
        cmd.Parameters.AddWithValue("item", Guid.NewGuid());
        cmd.Parameters.AddWithValue("product", product);
        cmd.Parameters.AddWithValue("attempt", attempt);
        cmd.Parameters.AddWithValue("ref", "PAY-F6-" + attempt.ToString("N"));
        cmd.Parameters.AddWithValue("reserve", reservation);
        Assert.Equal(3, await cmd.ExecuteNonQueryAsync());

        if (items != 2) return;
        await using var second = conn.CreateCommand();
        second.CommandText = """
            INSERT INTO finance.items_orden
              ("Id","OrdenId","ProductoId","SkuReferencia","NombreProducto",
               "Cantidad","PrecioUnitario")
            VALUES (@item,@order,@product,'F6-SKU-B','Producto QA B',1,45);
            """;
        second.Parameters.AddWithValue("item", Guid.NewGuid());
        second.Parameters.AddWithValue("order", order);
        second.Parameters.AddWithValue("product", Guid.NewGuid());
        Assert.Equal(1, await second.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ReservaHistoricaDuplicada_AbortarSinAlterarDatosNiHistorial()
    {
        await using (var initial = Context())
            await initial.GetService<IMigrator>().MigrateAsync(Fin07);

        var shared = Guid.NewGuid();
        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();
        var attempt1 = Guid.NewGuid();
        var attempt2 = Guid.NewGuid();
        await SeedOrder(order1, Guid.NewGuid(), attempt1, shared, items: 1);
        await SeedOrder(order2, Guid.NewGuid(), attempt2, shared, items: 1);

        var failure = await Record.ExceptionAsync(async () =>
        {
            await using var upgrade = Context();
            await upgrade.GetService<IMigrator>().MigrateAsync(Checkout);
        });
        Assert.NotNull(failure);
        Assert.Contains("CHECKOUT_LEGACY_RESERVA_DUPLICADA", failure!.ToString());

        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM finance.intentos_pago
               WHERE "ReservaId"=@shared AND "Id" IN (@a,@b)),
              (SELECT COUNT(*) FROM finance.items_orden
               WHERE "OrdenId" IN (@o1,@o2)),
              to_regclass('finance.intento_reservas_pago') IS NULL,
              (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
               WHERE "MigrationId"=@migration);
            """;
        check.Parameters.AddWithValue("shared", shared);
        check.Parameters.AddWithValue("a", attempt1);
        check.Parameters.AddWithValue("b", attempt2);
        check.Parameters.AddWithValue("o1", order1);
        check.Parameters.AddWithValue("o2", order2);
        check.Parameters.AddWithValue("migration", Checkout);
        await using var result = await check.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(2L, result.GetInt64(0));
        Assert.Equal(2L, result.GetInt64(1));
        Assert.True(result.GetBoolean(2));
        Assert.Equal(0L, result.GetInt64(3));
    }

    [Fact]
    public async Task OrdenMultiSkuAmbigua_NoInventaBackfill_ConservaReservaLegacy()
    {
        await using (var initial = Context())
            await initial.GetService<IMigrator>().MigrateAsync(Fin07);

        var order = Guid.NewGuid();
        var attempt = Guid.NewGuid();
        var reserve = Guid.NewGuid();
        await SeedOrder(order, Guid.NewGuid(), attempt, reserve, items: 2);

        await using (var upgrade = Context())
            await upgrade.GetService<IMigrator>().MigrateAsync(Checkout);

        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM finance.intentos_pago
               WHERE "Id"=@attempt AND "ReservaId"=@reserve),
              (SELECT COUNT(*) FROM finance.items_orden
               WHERE "OrdenId"=@order),
              (SELECT COUNT(*) FROM finance.intento_reservas_pago
               WHERE "IntentoPagoId"=@attempt),
              (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
               WHERE "MigrationId"=@migration);
            """;
        check.Parameters.AddWithValue("attempt", attempt);
        check.Parameters.AddWithValue("reserve", reserve);
        check.Parameters.AddWithValue("order", order);
        check.Parameters.AddWithValue("migration", Checkout);
        await using var result = await check.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(1L, result.GetInt64(0));
        Assert.Equal(2L, result.GetInt64(1));
        Assert.Equal(0L, result.GetInt64(2));
        Assert.Equal(1L, result.GetInt64(3));
    }

    [Fact]
    public async Task ReservaDuplicadaEntreDosOrdenesMultiSku_BloqueaUpgradeYConservaTodo()
    {
        await using (var initial = Context())
            await initial.GetService<IMigrator>().MigrateAsync(Fin07);

        // Este caso NO debe escaparse por la exclusión del backfill multiproducto.
        var shared = Guid.NewGuid();
        var firstOrder = Guid.NewGuid();
        var secondOrder = Guid.NewGuid();
        await SeedOrder(firstOrder, Guid.NewGuid(), Guid.NewGuid(), shared, items: 2);
        await SeedOrder(secondOrder, Guid.NewGuid(), Guid.NewGuid(), shared, items: 2);

        var failure = await Record.ExceptionAsync(async () =>
        {
            await using var upgrade = Context();
            await upgrade.GetService<IMigrator>().MigrateAsync(Checkout);
        });
        Assert.NotNull(failure);
        Assert.Contains("CHECKOUT_LEGACY_RESERVA_DUPLICADA", failure!.ToString());

        await using var conn = new NpgsqlConnection(_conn);
        await conn.OpenAsync();
        await using var check = conn.CreateCommand();
        check.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM finance.intentos_pago WHERE "ReservaId"=@reserve),
              (SELECT COUNT(*) FROM finance.items_orden
               WHERE "OrdenId" IN (@first,@second)),
              to_regclass('finance.intento_reservas_pago') IS NULL,
              (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
               WHERE "MigrationId"=@migration);
            """;
        check.Parameters.AddWithValue("reserve", shared);
        check.Parameters.AddWithValue("first", firstOrder);
        check.Parameters.AddWithValue("second", secondOrder);
        check.Parameters.AddWithValue("migration", Checkout);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(4L, reader.GetInt64(1));
        Assert.True(reader.GetBoolean(2));
        Assert.Equal(0L, reader.GetInt64(3));
    }

    public async Task DisposeAsync()
    {
        if (!_created || _admin is null || _conn is null
            || !_db.StartsWith("gate65_f6_", StringComparison.Ordinal))
            return;
        using (var pool = new NpgsqlConnection(_conn)) NpgsqlConnection.ClearPool(pool);
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
    }
}
