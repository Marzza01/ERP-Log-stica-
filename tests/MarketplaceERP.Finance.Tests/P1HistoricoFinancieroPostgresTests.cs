using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// P1 historical migration acceptance. Every test creates its own disposable,
/// localhost-only PostgreSQL database. Never connects to Neon or an ERP service.
/// </summary>
public sealed class P1HistoricoFinancieroPostgresTests : IAsyncLifetime
{
    private const string PreFin06 = "20261029150000_I2B2CConciliacionYReembolsos";
    private readonly string _databaseName = "p1_history_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _databaseConnection;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CI debe proveer PostgreSQL local para P1 histórico.");
            return;
        }

        var config = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(config.Host, StringComparer.OrdinalIgnoreCase)
            || config.Port != 5432
            || config.Database != "postgres"
            || config.Username != "postgres"
            || string.IsNullOrWhiteSpace(config.Password))
            throw new InvalidOperationException(
                "P1 histórico solo admite postgres@localhost:5432/postgres.");
        _adminConnection = config.ConnectionString;
        config.Database = _databaseName;
        _databaseConnection = config.ConnectionString;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
        await create.ExecuteNonQueryAsync();
    }

    private FinanceDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_databaseConnection!).Options);

    [Fact]
    public async Task MigracionFin06Fin07_PreservaHistoricosNoVaciosEIdempotencia()
    {
        if (_databaseConnection is null) return;
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreFin06);

        var ids = await SeedHistoricalDataAsync(db, "LEGACY-P1-A");
        Assert.Equal(1, await CountAsync(db, "finance.ordenes"));
        Assert.Equal(1, await CountAsync(db, "finance.facturas_proveedor"));
        Assert.Equal(1, await CountAsync(db, "finance.intentos_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.conciliaciones_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.reembolsos_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.splits_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.liquidaciones"));

        await db.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_databaseConnection);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT f."Id", f."NumeroFactura", f."MontoSubTotal", f."MontoIva",
                       f."MontoTotal", o."Id", i."Id", c."Id", r."Id",
                       s."Id", l."Id", p."Id"
                FROM finance.facturas_proveedor f
                CROSS JOIN finance.ordenes o
                JOIN finance.intentos_pago i ON i."OrdenId" = o."Id"
                JOIN finance.conciliaciones_pago c ON c."IntentoPagoId" = i."Id"
                JOIN finance.reembolsos_pago r ON r."OrdenId" = o."Id"
                JOIN finance.splits_pago s ON s."OrdenId" = o."Id"
                JOIN finance.liquidaciones l ON l."SplitPagoId" = s."Id"
                JOIN finance.ordenes_compra p ON p."Id" = f."OrdenCompraId"
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(ids.InvoiceId, reader.GetGuid(0));
            Assert.Equal(ids.InvoiceNumber, reader.GetString(1));
            Assert.Equal(100m, reader.GetDecimal(2));
            Assert.Equal(13m, reader.GetDecimal(3));
            Assert.Equal(113m, reader.GetDecimal(4));
            Assert.Equal(ids.OrderId, reader.GetGuid(5));
            Assert.Equal(ids.AttemptId, reader.GetGuid(6));
            Assert.Equal(ids.ReconciliationId, reader.GetGuid(7));
            Assert.Equal(ids.RefundId, reader.GetGuid(8));
            Assert.Equal(ids.SplitId, reader.GetGuid(9));
            Assert.Equal(ids.LiquidationId, reader.GetGuid(10));
            Assert.Equal(ids.PoId, reader.GetGuid(11));
            Assert.False(await reader.ReadAsync());
        }

        // FIN07 deliberately does not invent line items for legacy invoices.
        Assert.Equal(0, await CountAsync(db, "finance.items_factura_proveedor"));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // An identical migrated schema must be a strict no-op on a second run.
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await CountAsync(db, "finance.facturas_proveedor"));
        Assert.Equal(1, await CountAsync(db, "finance.intentos_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.reembolsos_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.liquidaciones"));

        // FIN06 normalized uniqueness rejects a second supplier invoice whose
        // number differs only in whitespace and case, without rewriting original.
        var secondPo = Guid.NewGuid();
        await InsertPoAsync(db, secondPo, ids.SupplierId, "PO-LEGACY-SECOND");
        var duplicate = await Assert.ThrowsAsync<PostgresException>(async () =>
            await InsertInvoiceAsync(db, Guid.NewGuid(), secondPo, ids.SupplierId,
                " legacy-p1-a  "));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal(1, await CountAsync(db, "finance.facturas_proveedor"));
    }

    [Fact]
    public async Task DuplicadosHistoricos_Fin06FallaCerradoSinEliminarFilas()
    {
        if (_databaseConnection is null) return;
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreFin06);
        var ids = await SeedHistoricalDataAsync(db, "LEGACY-P1-DUP");
        var secondPo = Guid.NewGuid();
        await InsertPoAsync(db, secondPo, ids.SupplierId, "PO-LEGACY-DUP");
        await InsertInvoiceAsync(db, Guid.NewGuid(), secondPo, ids.SupplierId,
            "  legacy-p1-dup ");

        Assert.Equal(2, await CountAsync(db, "finance.facturas_proveedor"));
        var failure = await Assert.ThrowsAsync<PostgresException>(async () =>
            await db.Database.MigrateAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);

        // No attempt at silent deduplication, deletion, or orphan relinking.
        Assert.Equal(2, await CountAsync(db, "finance.facturas_proveedor"));
        Assert.Equal(1, await CountAsync(db, "finance.intentos_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.conciliaciones_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.reembolsos_pago"));
        Assert.Equal(1, await CountAsync(db, "finance.liquidaciones"));
        Assert.Contains("20261107010000_FIN06IntegridadCompras",
            await db.Database.GetPendingMigrationsAsync());
    }

    private record HistoricalIds(
        Guid OrderId, Guid PoId, Guid SupplierId, Guid InvoiceId,
        Guid AttemptId, Guid ReconciliationId, Guid RefundId,
        Guid SplitId, Guid LiquidationId, string InvoiceNumber);

    private static async Task<HistoricalIds> SeedHistoricalDataAsync(
        FinanceDbContext db, string invoiceNumber)
    {
        var order = Guid.NewGuid();
        var po = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        var vendor = Guid.NewGuid();
        var supplier = Guid.NewGuid();
        var invoice = Guid.NewGuid();
        var attempt = Guid.NewGuid();
        var reconciliation = Guid.NewGuid();
        var refund = Guid.NewGuid();
        var split = Guid.NewGuid();
        var liquidation = Guid.NewGuid();
        var reservation = Guid.NewGuid();
        var stamp = DateTime.UtcNow;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.ordenes
              ("Id","NumeroOrden","CompradorId","VendedorId","SubTotal","ImpuestoIva",
               "CostoEnvio","MontoTotal","Estado","TipoCliente","NumeroComprobanteFiscal",
               "GuiaEnvio","DireccionEntrega")
            VALUES ({order},{"ORD-"+order.ToString("N")},{buyer},{vendor},{200m},{26m},
               {0m},{226m},{"Aprobada"},{"B2C"},{""},{""},{"Dirección ficticia de CI"})
            """);
        await InsertPoAsync(db, po, supplier, "PO-" + po.ToString("N"));
        await InsertInvoiceAsync(db, invoice, po, supplier, invoiceNumber);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.intentos_pago
              ("Id","OrdenId","ReferenciaOperacion","Monto","Moneda","Estado",
               "CreadoUtc","ActualizadoUtc","ReservaId")
            VALUES ({attempt},{order},{"PAY-"+order.ToString("N")},{226m},{"USD"},
               {"Incierto"},{stamp},{stamp},{reservation})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.conciliaciones_pago
              ("Id","IntentoPagoId","OrdenId","Decision","ConsultadoUtc")
            VALUES ({reconciliation},{attempt},{order},{"RevisionManual"},{stamp})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.reembolsos_pago
              ("Id","OrdenId","ReservaId","ClaveIdempotencia","TransaccionId","Monto",
               "Moneda","Estado","Version","CreadoUtc","ActualizadoUtc")
            VALUES ({refund},{order},{reservation},{"REF-"+order.ToString("N")},
               {"TEST-LEGACY-TRANSACTION"},{226m},{"USD"},{"Pendiente"},
               {Guid.NewGuid()},{stamp},{stamp})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.splits_pago
              ("Id","OrdenId","MontoTotal","MontoVendedor","MontoMarketplace","TransaccionId")
            VALUES ({split},{order},{200m},{180m},{20m},{"TEST-LEGACY-TRANSACTION"})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.liquidaciones ("Id","VendedorId","SplitPagoId",
                "MontoLiquidar","Estado")
            VALUES ({liquidation},{vendor},{split},{180m},{"Pendiente"})
            """);
        return new(order, po, supplier, invoice, attempt, reconciliation,
            refund, split, liquidation, invoiceNumber);
    }

    private static Task InsertPoAsync(FinanceDbContext db, Guid id, Guid supplier, string number) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.ordenes_compra
              ("Id","NumeroPO","ProveedorId","ProveedorNombre","Estado",
               "SubTotal","ImpuestoIva","MontoTotal","Observaciones")
            VALUES ({id},{number},{supplier},{"Proveedor ficticio CI"},{"RecibidaCompleta"},
               {100m},{13m},{113m},{"Prueba migración P1"})
            """);

    private static Task InsertInvoiceAsync(
        FinanceDbContext db, Guid id, Guid po, Guid supplier, string number) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO finance.facturas_proveedor
              ("Id","NumeroFactura","OrdenCompraId","ProveedorId","MontoSubTotal",
               "MontoIva","MontoTotal","FechaVencimiento","AprobadaParaPago","Pagada")
            VALUES ({id},{number},{po},{supplier},{100m},{13m},{113m},
               {DateTime.UtcNow.AddDays(30)},{false},{false})
            """);

    private static async Task<long> CountAsync(FinanceDbContext db, string table)
    {
        // All table values are constant, defined by code. Never accept user input.
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT COUNT(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task DisposeAsync()
    {
        if (_adminConnection is null || _databaseConnection is null ||
            !_databaseName.StartsWith("p1_history_", StringComparison.Ordinal)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
