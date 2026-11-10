using System.Diagnostics;
using System.Security.Cryptography;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Gate 65 / F5. Respaldo físico pg_dump + pg_restore de FIN07 y
/// actualización de COPIA restaurada a la cadena comercial completa.
/// SOLO bases efímeras localhost:5432/postgres, sin Neon ni pagos.
/// </summary>
public sealed class Gate65F5BackupRestoreCadenaCompletaTests : IAsyncLifetime
{
    private const string Fin07 = "20261107022000_FIN07DetalleFacturaProveedor";
    private const string Checkout = "20261108060000_IntentoReservasMultiSku";
    private const string Auditoria = "20261109203804_CancelacionPreEnvioAuditada";
    private const string Fin08 = "20261110024500_FIN08UnicidadAsientosVentaLiquidacion";
    private const string Fin09 = "20261110050932_FIN09LiquidacionUnoAMuchosExpandir";
    private readonly string _src = "gate65_f5_src_" + Guid.NewGuid().ToString("N");
    private readonly string _dst = "gate65_f5_dst_" + Guid.NewGuid().ToString("N");
    private readonly string _dump = Path.Combine(AppContext.BaseDirectory,
        "gate65_f5_" + Guid.NewGuid().ToString("N") + ".backup");
    private string? _adminCs, _srcCs, _dstCs, _password;
    private bool _createdSrc, _createdDst;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("Gate65: requiere PostgreSQL local QA.");
        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(cs.Host, StringComparer.OrdinalIgnoreCase)
            || cs.Port != 5432 || cs.Database != "postgres"
            || cs.Username != "postgres" || string.IsNullOrWhiteSpace(cs.Password))
            throw new InvalidOperationException("GATE65_F5_SOLO_POSTGRES_LOCAL");
        _password = cs.Password;
        _adminCs = cs.ConnectionString;
        cs.Database = _src;
        _srcCs = cs.ConnectionString;
        cs.Database = _dst;
        _dstCs = cs.ConnectionString;
        await CreateDatabase(_src);
        _createdSrc = true;
        await CreateDatabase(_dst);
        _createdDst = true;
    }

    private async Task CreateDatabase(string db)
    {
        Assert.StartsWith("gate65_f5_", db);
        await using var conn = new NpgsqlConnection(_adminCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{db}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    private static FinanceDbContext Context(string cs) =>
        new(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(cs, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
            .Options);

    private async Task PgTool(string name, params string[] args)
    {
        var info = new ProcessStartInfo(name)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["PGPASSWORD"] = _password!;
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("PostgreSQL tool missing");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"GATE65_F5_{name}_EXIT_{process.ExitCode} (sin publicar parámetros ni secretos)");
    }

    [Fact]
    public async Task Fin07_RespaldoFisico_Restauracion_UpgradeFin09_ConservaDatosYBloqueaQa()
    {
        Assert.True(_createdSrc && _createdDst);
        await using (var fin07 = Context(_srcCs!))
            await fin07.GetService<IMigrator>().MigrateAsync(Fin07);

        var order = Guid.NewGuid();
        var attempt = Guid.NewGuid();
        var reserve = Guid.NewGuid();
        var product = Guid.NewGuid();
        var split = Guid.NewGuid();
        var settlement = Guid.NewGuid();

        await using (var conn = new NpgsqlConnection(_srcCs))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO finance.ordenes
                  ("Id","NumeroOrden","CompradorId","VendedorId","SubTotal",
                   "MontoTotal","Moneda","Estado","TipoCliente",
                   "NumeroComprobanteFiscal","GuiaEnvio","DireccionEntrega")
                VALUES (@order,@number,@buyer,@seller,90,90,'USD','Entregada','B2C',
                        'GATE65-F5-FISCAL','GATE65-F5-GUIA','Solo datos sinteticos');
                INSERT INTO finance.items_orden
                  ("Id","OrdenId","ProductoId","SkuReferencia","NombreProducto",
                   "Cantidad","PrecioUnitario")
                VALUES (@item,@order,@product,'GATE65-F5-SKU','Producto ficticio',1,90);
                INSERT INTO finance.intentos_pago
                  ("Id","OrdenId","ReferenciaOperacion","Monto","Moneda","Estado",
                   "CreadoUtc","ActualizadoUtc","ReservaId")
                VALUES (@attempt,@order,@reference,90,'USD','Preparado',now(),now(),@reserve);
                INSERT INTO finance.splits_pago
                  ("Id","OrdenId","MontoTotal","MontoVendedor","MontoMarketplace","TransaccionId")
                VALUES (@split,@order,90,81,9,@txn);
                INSERT INTO finance.liquidaciones
                  ("Id","VendedorId","SplitPagoId","MontoLiquidar","Moneda","Estado")
                VALUES (@settlement,@seller,@split,81,'USD','Pendiente');
                """;
            cmd.Parameters.AddWithValue("order", order);
            cmd.Parameters.AddWithValue("number", "GATE65F5-" + order.ToString("N"));
            cmd.Parameters.AddWithValue("buyer", Guid.NewGuid());
            var seller = Guid.NewGuid();
            cmd.Parameters.AddWithValue("seller", seller);
            cmd.Parameters.AddWithValue("item", Guid.NewGuid());
            cmd.Parameters.AddWithValue("product", product);
            cmd.Parameters.AddWithValue("attempt", attempt);
            cmd.Parameters.AddWithValue("reference", "PAY-" + order.ToString("N"));
            cmd.Parameters.AddWithValue("reserve", reserve);
            cmd.Parameters.AddWithValue("split", split);
            cmd.Parameters.AddWithValue("txn", "QA-F5-TXN-" + Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("settlement", settlement);
            Assert.Equal(5, await cmd.ExecuteNonQueryAsync());
        }

        await PgTool("pg_dump", "--host=localhost", "--port=5432",
            "--username=postgres", "--no-password", "--format=custom",
            "--file=" + _dump, _src);
        Assert.True(File.Exists(_dump));
        Assert.True(new FileInfo(_dump).Length > 1000);
        var shaBefore = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(_dump)));

        await PgTool("pg_restore", "--host=localhost", "--port=5432",
            "--username=postgres", "--no-password", "--no-owner", "--no-privileges",
            "--exit-on-error", "--dbname=" + _dst, _dump);
        Assert.Equal(shaBefore,
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(_dump))));

        // La copia restaurada mantiene todos los objetos y el historial FIN07.
        await using (var restored = new NpgsqlConnection(_dstCs))
        {
            await restored.OpenAsync();
            await using var check = restored.CreateCommand();
            check.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM finance.intentos_pago
                   WHERE "Id"=@attempt AND "ReservaId"=@reserve),
                  (SELECT COUNT(*) FROM finance.liquidaciones
                   WHERE "Id"=@settlement AND "SplitPagoId"=@split AND "MontoLiquidar"=81),
                  (SELECT "MigrationId" FROM finance."__EFMigrationsHistory"
                   ORDER BY "MigrationId" DESC LIMIT 1);
                """;
            check.Parameters.AddWithValue("attempt", attempt);
            check.Parameters.AddWithValue("reserve", reserve);
            check.Parameters.AddWithValue("settlement", settlement);
            check.Parameters.AddWithValue("split", split);
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(1L, reader.GetInt64(1));
            Assert.Equal(Fin07, reader.GetString(2));
        }

        await using (var upgraded = Context(_dstCs!))
            await upgraded.Database.MigrateAsync();

        await using (var target = new NpgsqlConnection(_dstCs))
        {
            await target.OpenAsync();
            await using var verify = target.CreateCommand();
            verify.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                   WHERE "MigrationId" IN (@checkout,@audit,@fin08,@fin09)),
                  (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                   WHERE "MigrationId" IN
                    ('20261108060000_QaMultiSkuIntentoReservas',
                     '20261109203804_Qa09CancelacionPreEnvioAuditable')),
                  (SELECT COUNT(*) FROM finance.intento_reservas_pago
                   WHERE "IntentoPagoId"=@attempt AND "ReservaId"=@reserve
                     AND "ProductoId"=@product AND "Cantidad"=1
                     AND "Procedencia"='LegacySinVerificar'),
                  (SELECT COUNT(*) FROM finance.splits_pago
                   WHERE "Id"=@split AND "LiquidacionId"=@settlement),
                  (SELECT COUNT(*) FROM finance.liquidaciones
                   WHERE "Id"=@settlement AND "MontoLiquidar"=81),
                  to_regclass('finance.cancelaciones_pre_envio') IS NOT NULL,
                  to_regclass('finance.cancelaciones_pre_envio_qa') IS NULL,
                  (SELECT "MigrationId" FROM finance."__EFMigrationsHistory"
                   ORDER BY "MigrationId" DESC LIMIT 1);
                """;
            verify.Parameters.AddWithValue("checkout", Checkout);
            verify.Parameters.AddWithValue("audit", Auditoria);
            verify.Parameters.AddWithValue("fin08", Fin08);
            verify.Parameters.AddWithValue("fin09", Fin09);
            verify.Parameters.AddWithValue("attempt", attempt);
            verify.Parameters.AddWithValue("reserve", reserve);
            verify.Parameters.AddWithValue("product", product);
            verify.Parameters.AddWithValue("split", split);
            verify.Parameters.AddWithValue("settlement", settlement);
            await using var read = await verify.ExecuteReaderAsync();
            Assert.True(await read.ReadAsync());
            Assert.Equal(4L, read.GetInt64(0));
            Assert.Equal(0L, read.GetInt64(1));
            Assert.Equal(1L, read.GetInt64(2));
            Assert.Equal(1L, read.GetInt64(3));
            Assert.Equal(1L, read.GetInt64(4));
            Assert.True(read.GetBoolean(5));
            Assert.True(read.GetBoolean(6));
            Assert.Equal(Fin09, read.GetString(7));
        }

        // El origen NO se migra: sigue como punto de recuperación físico FIN07.
        await using (var src = new NpgsqlConnection(_srcCs))
        {
            await src.OpenAsync();
            await using var check = src.CreateCommand();
            check.CommandText = """
                SELECT
                  (SELECT "MigrationId" FROM finance."__EFMigrationsHistory"
                   ORDER BY "MigrationId" DESC LIMIT 1),
                  (SELECT COUNT(*) FROM finance.liquidaciones
                   WHERE "Id"=@settlement AND "SplitPagoId"=@split AND "MontoLiquidar"=81),
                  to_regclass('finance.intento_reservas_pago') IS NULL;
                """;
            check.Parameters.AddWithValue("settlement", settlement);
            check.Parameters.AddWithValue("split", split);
            await using var read = await check.ExecuteReaderAsync();
            Assert.True(await read.ReadAsync());
            Assert.Equal(Fin07, read.GetString(0));
            Assert.Equal(1L, read.GetInt64(1));
            Assert.True(read.GetBoolean(2));
        }
    }

    public async Task DisposeAsync()
    {
        if (_srcCs != null)
            using (var c = new NpgsqlConnection(_srcCs)) NpgsqlConnection.ClearPool(c);
        if (_dstCs != null)
            using (var c = new NpgsqlConnection(_dstCs)) NpgsqlConnection.ClearPool(c);
        foreach (var db in new[] { _createdDst ? _dst : null, _createdSrc ? _src : null })
        {
            if (db == null) continue;
            if (!db.StartsWith("gate65_f5_", StringComparison.Ordinal))
                throw new InvalidOperationException("GATE65_F5_DB_NAME_GUARD");
            await using var admin = new NpgsqlConnection(_adminCs);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
        if (File.Exists(_dump) &&
            Path.GetFileName(_dump).StartsWith("gate65_f5_", StringComparison.Ordinal))
            File.Delete(_dump);
    }
}
