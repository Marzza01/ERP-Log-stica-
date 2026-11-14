using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Gate65. Ensayo INTEGRADO de FIN07 -> checkout producto -> auditoría producto
/// -> FIN08 -> FIN09, sobre DB temporal creada exclusivamente en PostgreSQL local.
/// Los nombres QA antiguos nunca deben entrar en el historial de migraciones.
/// </summary>
public sealed class Gate65CadenaMigracionesFinanceProductoTests : IAsyncLifetime
{
    private const string Fin07 = "20261107022000_FIN07DetalleFacturaProveedor";
    private const string Checkout = "20261108060000_IntentoReservasMultiSku";
    private const string Audit = "20261109203804_CancelacionPreEnvioAuditada";
    private const string Fin08 = "20261110024500_FIN08UnicidadAsientosVentaLiquidacion";
    private const string Fin09 = "20261110050932_FIN09LiquidacionUnoAMuchosExpandir";

    private readonly string _dbName = "gate65_chain_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("Gate65 requiere TEST_PG_CONNECTION local");
        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(cs.Host, StringComparer.OrdinalIgnoreCase)
            || cs.Port != 5432 || cs.Database != "postgres" ||
            cs.Username != "postgres" || string.IsNullOrWhiteSpace(cs.Password))
            throw new InvalidOperationException("GATE65_SÓLO_POSTGRES_LOCAL");

        _adminConnection = cs.ConnectionString;
        cs.Database = _dbName;
        _testConnection = cs.ConnectionString;

        await using var conn = new NpgsqlConnection(_adminConnection);
        await conn.OpenAsync();
        await using var command = conn.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_dbName}\"";
        await command.ExecuteNonQueryAsync();
        _created = true;
    }

    private FinanceDbContext Context() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!, config =>
                config.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
            .Options);

    [Fact]
    public async Task DesdeFin07_MigraATodasLasMejoras_ReconciliaUnaReserva_NoCompilaQA()
    {
        await using (var prep = Context())
        {
            var migrations = prep.Database.GetMigrations().ToArray();
            Assert.Contains(Checkout, migrations);
            Assert.Contains(Audit, migrations);
            Assert.Contains(Fin08, migrations);
            Assert.Contains(Fin09, migrations);
            Assert.DoesNotContain("20261108060000_QaMultiSkuIntentoReservas", migrations);
            Assert.DoesNotContain("20261109203804_Qa09CancelacionPreEnvioAuditable", migrations);
            await prep.GetService<IMigrator>().MigrateAsync(Fin07);
        }

        var orderId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var reserveId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_testConnection))
        {
            await conn.OpenAsync();
            await using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO finance.ordenes
                  ("Id","NumeroOrden","CompradorId","VendedorId","SubTotal",
                   "MontoTotal","Moneda","Estado","TipoCliente",
                   "NumeroComprobanteFiscal","GuiaEnvio","DireccionEntrega")
                VALUES (@order,@number,@buyer,@seller,90,90,'USD','Entregada','B2C',
                        'GATE65-CHAIN-FISCAL','GATE65-CHAIN-GUIA','QA local');
                INSERT INTO finance.items_orden
                  ("Id","OrdenId","ProductoId","SkuReferencia","NombreProducto",
                   "Cantidad","PrecioUnitario")
                VALUES (@item,@order,@product,'GATE65-CHAIN','Producto sintético',1,90);
                INSERT INTO finance.intentos_pago
                  ("Id","OrdenId","ReferenciaOperacion","Monto","Moneda","Estado",
                   "CreadoUtc","ActualizadoUtc","ReservaId")
                VALUES (@attempt,@order,@reference,90,'USD','Preparado',now(),now(),@reserve);
                """;
            insert.Parameters.AddWithValue("order", orderId);
            insert.Parameters.AddWithValue("number", "GATE65-ORDER-" + Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("buyer", Guid.NewGuid());
            insert.Parameters.AddWithValue("seller", Guid.NewGuid());
            insert.Parameters.AddWithValue("item", Guid.NewGuid());
            insert.Parameters.AddWithValue("product", productId);
            insert.Parameters.AddWithValue("attempt", attemptId);
            insert.Parameters.AddWithValue("reference", "GATE65-PAY-" + Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("reserve", reserveId);
            Assert.Equal(3, await insert.ExecuteNonQueryAsync());
        }

        await using (var migrate = Context())
            await migrate.Database.MigrateAsync();

        await using var check = new NpgsqlConnection(_testConnection);
        await check.OpenAsync();
        await using var query = check.CreateCommand();
        query.CommandText = """
            SELECT
                (SELECT "MigrationId" FROM finance."__EFMigrationsHistory"
                  ORDER BY "MigrationId" DESC LIMIT 1) AS latest,
                (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                  WHERE "MigrationId" IN (@checkout,@audit,@fin08,@fin09)) AS migrations_count,
                (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                  WHERE "MigrationId" IN
                  ('20261108060000_QaMultiSkuIntentoReservas',
                   '20261109203804_Qa09CancelacionPreEnvioAuditable')) AS qa_count,
                (SELECT COUNT(*) FROM finance.intento_reservas_pago
                  WHERE "IntentoPagoId"=@attempt AND "ReservaId"=@reserve
                    AND "ProductoId"=@product AND "Cantidad"=1
                    AND "Procedencia"='LegacySinVerificar') AS backfill_count,
                (SELECT COUNT(*) FROM finance.intentos_pago
                  WHERE "Id"=@attempt AND "ReservaId"=@reserve) AS legacy_count,
                to_regclass('finance.cancelaciones_pre_envio') IS NOT NULL AS audit_present,
                to_regclass('finance.cancelaciones_pre_envio_qa') IS NULL AS qa_audit_absent,
                (SELECT COUNT(*) FROM information_schema.columns
                  WHERE table_schema='finance' AND table_name='splits_pago'
                   AND column_name='LiquidacionId') AS fin09_column;
            """;
        query.Parameters.AddWithValue("checkout", Checkout);
        query.Parameters.AddWithValue("audit", Audit);
        query.Parameters.AddWithValue("fin08", Fin08);
        query.Parameters.AddWithValue("fin09", Fin09);
        query.Parameters.AddWithValue("attempt", attemptId);
        query.Parameters.AddWithValue("reserve", reserveId);
        query.Parameters.AddWithValue("product", productId);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(Fin09, reader.GetString(0));
        Assert.Equal(4L, reader.GetInt64(1));
        Assert.Equal(0L, reader.GetInt64(2));
        Assert.Equal(1L, reader.GetInt64(3));
        Assert.Equal(1L, reader.GetInt64(4));
        Assert.True(reader.GetBoolean(5));
        Assert.True(reader.GetBoolean(6));
        Assert.Equal(1L, reader.GetInt64(7));
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null || _testConnection is null
            || !_dbName.StartsWith("gate65_chain_", StringComparison.Ordinal))
            return;
        using (var pool = new NpgsqlConnection(_testConnection))
            NpgsqlConnection.ClearPool(pool);
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        _created = false;
    }
}
