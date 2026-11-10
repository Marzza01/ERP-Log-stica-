using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Gate65 Fase3: auditoria de producto sin DROP sobre DB PostgreSQL LOCAL efimera.
/// No se usa Neon, Render ni DBs ajenas a esta prueba.
/// </summary>
public sealed class Gate65AuditoriaCancelacionDominioPostgresTests : IAsyncLifetime
{
    private const string Fin07 = "20261107022000_FIN07DetalleFacturaProveedor";
    private const string Checkout = "20261108060000_IntentoReservasMultiSku";
    private const string Audit = "20261109203804_CancelacionPreEnvioAuditada";
    private readonly string _dbName = "gate65_audit_" + Guid.NewGuid().ToString("N");
    private string? _admin;
    private string? _database;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("Se requiere PostgreSQL local para Fase3");
        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(cs.Host, StringComparer.OrdinalIgnoreCase)
            || cs.Database != "postgres" || cs.Port != 5432 || cs.Username != "postgres"
            || string.IsNullOrWhiteSpace(cs.Password))
            throw new InvalidOperationException("GATE65_FASE3_SOLO_POSTGRES_LOCAL");
        _admin = cs.ConnectionString;
        cs.Database = _dbName;
        _database = cs.ConnectionString;

        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_dbName}\"";
        await command.ExecuteNonQueryAsync();
        _created = true;
    }

    private FinanceDbContext Context()
    {
        var opts = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_database!, o => o.MigrationsHistoryTable(
                "__EFMigrationsHistory", "finance")).Options;
        return new FinanceDbContext(opts);
    }

    [Fact]
    public async Task MigracionDominio_NoQa_PreservaAuditoriaYRechazaDown()
    {
        await using (var initial = Context())
        {
            var migrations = initial.Database.GetMigrations().ToArray();
            Assert.Contains(Audit, migrations);
            Assert.Contains(Checkout, migrations);
            Assert.DoesNotContain("20261109203804_Qa09CancelacionPreEnvioAuditable", migrations);
            Assert.DoesNotContain("20261108060000_QaMultiSkuIntentoReservas", migrations);
            await initial.GetService<IMigrator>().MigrateAsync(Fin07);
        }

        var ordenId = Guid.NewGuid();
        var intentoId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_database))
        {
            await conn.OpenAsync();
            await using var seed = conn.CreateCommand();
            seed.CommandText = """
                INSERT INTO finance.ordenes
                 ("Id","NumeroOrden","CompradorId","VendedorId","SubTotal",
                  "MontoTotal","Moneda","Estado","TipoCliente",
                  "NumeroComprobanteFiscal","GuiaEnvio","DireccionEntrega")
                VALUES (@orden,@numero,@comprador,@vendedor,35,35,'USD','Pendiente','B2C',
                        'GATE65-AUD-FISCAL','GATE65-AUD-GUIA','QA local');
                INSERT INTO finance.intentos_pago
                 ("Id","OrdenId","ReferenciaOperacion","Monto","Moneda","Estado",
                  "CreadoUtc","ActualizadoUtc","ReservaId")
                VALUES (@intento,@orden,@ref,35,'USD','Preparado',now(),now(),NULL);
                """;
            seed.Parameters.AddWithValue("orden", ordenId);
            seed.Parameters.AddWithValue("numero", "GATE65-AUD-" + Guid.NewGuid().ToString("N"));
            seed.Parameters.AddWithValue("comprador", Guid.NewGuid());
            seed.Parameters.AddWithValue("vendedor", Guid.NewGuid());
            seed.Parameters.AddWithValue("intento", intentoId);
            seed.Parameters.AddWithValue("ref", "PAY-" + ordenId.ToString("N"));
            Assert.Equal(2, await seed.ExecuteNonQueryAsync());
        }

        await using (var migrate = Context())
            await migrate.GetService<IMigrator>().MigrateAsync(Audit);

        var evento = Guid.NewGuid();
        await using (var create = Context())
        {
            create.CancelacionesPreEnvioQa.Add(new CancelacionPreEnvioQa
            {
                Id = evento,
                OrdenId = ordenId,
                IntentoPagoId = intentoId,
                OperadorQaId = Guid.NewGuid(),
                Motivo = MotivoCancelacionPreEnvioQa.VerificacionManualQa,
                ReservasLiberadas = 0,
                RegistradoUtc = DateTime.UtcNow
            });
            Assert.Equal(1, await create.SaveChangesAsync());
        }

        await using (var conn = new NpgsqlConnection(_database))
        {
            await conn.OpenAsync();
            await using var query = conn.CreateCommand();
            query.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM finance.cancelaciones_pre_envio
                     WHERE "Id"=@evento AND "IntentoPagoId"=@intento
                       AND "OrdenId"=@orden AND "OperadorId" <> '00000000-0000-0000-0000-000000000000'::uuid),
                  to_regclass('finance.cancelaciones_pre_envio_qa') IS NULL,
                  (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                     WHERE "MigrationId"=@audit),
                  (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
                     WHERE "MigrationId"='20261109203804_Qa09CancelacionPreEnvioAuditable');
                """;
            query.Parameters.AddWithValue("evento", evento);
            query.Parameters.AddWithValue("intento", intentoId);
            query.Parameters.AddWithValue("orden", ordenId);
            query.Parameters.AddWithValue("audit", Audit);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.True(reader.GetBoolean(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(0L, reader.GetInt64(3));
        }

        var failure = await Record.ExceptionAsync(async () =>
        {
            await using var rollback = Context();
            await rollback.GetService<IMigrator>().MigrateAsync(Checkout);
        });
        Assert.NotNull(failure);
        Assert.Contains("CANCELACION_AUDITORIA_DOWN_BLOQUEADO", failure!.ToString());

        await using (var check = Context())
        {
            var found = await check.CancelacionesPreEnvioQa.AsNoTracking().SingleAsync();
            Assert.Equal(evento, found.Id);
            Assert.Equal(intentoId, found.IntentoPagoId);
            Assert.Equal("VerificacionManualQa", found.Motivo.ToString());
        }
    }

    [Fact]
    public async Task AuditoriaQaPreexistente_RechazaSinBorrarDatosNiCrearTablaProducto()
    {
        await using (var initial = Context())
            await initial.GetService<IMigrator>().MigrateAsync(Checkout);
        await using (var conn = new NpgsqlConnection(_database))
        {
            await conn.OpenAsync();
            await using var create = conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE finance.cancelaciones_pre_envio_qa (
                    "Id" uuid PRIMARY KEY
                );
                INSERT INTO finance.cancelaciones_pre_envio_qa ("Id") VALUES (@evento);
                """;
            create.Parameters.AddWithValue("evento", Guid.NewGuid());
            Assert.Equal(1, await create.ExecuteNonQueryAsync());
        }

        var failed = await Record.ExceptionAsync(async () =>
        {
            await using var attempt = Context();
            await attempt.GetService<IMigrator>().MigrateAsync(Audit);
        });
        Assert.NotNull(failed);
        Assert.Contains("CANCELACION_AUDITORIA_QA_EXISTENTE", failed!.ToString());

        await using var verify = new NpgsqlConnection(_database);
        await verify.OpenAsync();
        await using var query = verify.CreateCommand();
        query.CommandText = """
            SELECT
             (SELECT COUNT(*) FROM finance.cancelaciones_pre_envio_qa),
             to_regclass('finance.cancelaciones_pre_envio') IS NULL,
             (SELECT COUNT(*) FROM finance."__EFMigrationsHistory"
              WHERE "MigrationId"=@audit);
            """;
        query.Parameters.AddWithValue("audit", Audit);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal(0L, reader.GetInt64(2));
    }

    public async Task DisposeAsync()
    {
        if (!_created || _admin is null || _database is null ||
            !_dbName.StartsWith("gate65_audit_", StringComparison.Ordinal))
            return;
        using (var pool = new NpgsqlConnection(_database))
            NpgsqlConnection.ClearPool(pool);
        await using var admin = new NpgsqlConnection(_admin);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        _created = false;
    }
}
