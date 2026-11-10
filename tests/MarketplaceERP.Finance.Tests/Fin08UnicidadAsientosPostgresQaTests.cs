using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Pruebas destructivas SOLO sobre base local efimera creada con prefijo ci_fin08_.
/// No permiten Neon, host externo, bases compartidas ni pagos reales.
/// </summary>
public sealed class Fin08UnicidadAsientosPostgresQaTests : IAsyncLifetime
{
    private readonly string _database = "ci_fin08_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("FIN08 QA exige TEST_PG_CONNECTION local.");

        var admin = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(admin.Host, StringComparer.OrdinalIgnoreCase) ||
            admin.Port != 5432 ||
            admin.Database != "postgres" ||
            admin.Username != "postgres" ||
            string.IsNullOrWhiteSpace(admin.Password))
            throw new InvalidOperationException(
                "FIN08 permite solo postgres@localhost:5432/postgres.");

        _adminConnection = admin.ConnectionString;
        admin.Database = _database;
        _testConnection = admin.ConnectionString;

        await using var conn = new NpgsqlConnection(_adminConnection);
        await conn.OpenAsync();
        await using var create = conn.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{_database}\"";
        await create.ExecuteNonQueryAsync();
        _created = true;
    }

    private FinanceDbContext NewContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!).Options);

    private async Task<Guid> InsertAsync(string operation, Guid? reference)
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_testConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO finance.partidas_contables
                ("Id", "NumeroPartida", "Fecha", "Concepto", "TipoOperacion",
                 "ReferenciaOrigenId", "DocumentoReferencia", "TotalDebe", "TotalHaber", "FechaRegistro")
            VALUES (@id, @number, NOW(), 'FIN08 QA', @operation,
                    @reference, 'qa', 10, 10, NOW());
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("number", "QA-" + id.ToString("N"));
        cmd.Parameters.AddWithValue("operation", operation);
        cmd.Parameters.Add("reference", NpgsqlDbType.Uuid).Value =
            reference.HasValue ? reference.Value : DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<bool> TryInsertAsync(string operation, Guid reference)
    {
        try
        {
            await InsertAsync(operation, reference);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    private async Task<long> CountAsync(string operation, Guid reference)
    {
        await using var conn = new NpgsqlConnection(_testConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM finance.partidas_contables
             WHERE "TipoOperacion"=@operation AND "ReferenciaOrigenId"=@reference;
            """;
        cmd.Parameters.AddWithValue("operation", operation);
        cmd.Parameters.AddWithValue("reference", reference);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task DeleteByIdAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_testConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM finance.partidas_contables WHERE \"Id\"=@id";
        cmd.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private static PostgresException? PostgreSqlException(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException pg) return pg;
        return null;
    }

    [Fact]
    public async Task MigracionRechazaDuplicadosPreviosSinBorrarlosYLuegoSeAplica()
    {
        Assert.True(_created);
        await using (var initial = NewContext())
            await initial.GetService<IMigrator>()
                .MigrateAsync("20261107022000_FIN07DetalleFacturaProveedor");

        var refId = Guid.NewGuid();
        await InsertAsync("Venta", refId);
        var segundo = await InsertAsync("Venta", refId);
        Assert.Equal(2, await CountAsync("Venta", refId));

        var ex = await Record.ExceptionAsync(async () =>
        {
            await using var blocked = NewContext();
            await blocked.Database.MigrateAsync();
        });
        Assert.NotNull(ex);
        var pg = PostgreSqlException(ex!);
        Assert.NotNull(pg);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg!.SqlState);
        Assert.Contains("FIN08", pg.Message);
        Assert.Equal(2, await CountAsync("Venta", refId));

        await DeleteByIdAsync(segundo); // Eliminacion exclusivamente de fixture efimero.
        await using (var retried = NewContext())
            await retried.Database.MigrateAsync();
        Assert.Equal(1, await CountAsync("Venta", refId));
    }

    [Fact]
    public async Task IndicesUnicosBloqueanCarrerasVentaYLiquidacionSinCruzarTipos()
    {
        Assert.True(_created);
        await using (var initial = NewContext())
            await initial.Database.MigrateAsync();

        foreach (var type in new[] { "Venta", "Liquidacion" })
        {
            var refId = Guid.NewGuid();
            var outcomes = await Task.WhenAll(
                TryInsertAsync(type, refId),
                TryInsertAsync(type, refId));
            Assert.Single(outcomes, x => x);
            Assert.Single(outcomes, x => !x);
            Assert.Equal(1, await CountAsync(type, refId));

            // Asegurar que la unicidad se aplica por TIPO, no entre tipos.
            var other = type == "Venta" ? "Liquidacion" : "Venta";
            await InsertAsync(other, refId);
            Assert.Equal(1, await CountAsync(other, refId));
        }

        // Los asientos manuales carecen de RefOrigenId y siguen admitidos.
        await InsertAsync("Ajuste", null);
        await InsertAsync("Ajuste", null);

        // FIN06 Compra conserva su propio indice, no fue eliminado.
        await using var conn = new NpgsqlConnection(_testConnection);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM pg_indexes WHERE schemaname='finance'
             AND indexname IN (
              'UX_FIN06_asiento_compra_po',
              'UX_FIN08_asiento_venta_orden',
              'UX_FIN08_asiento_liquidacion');
            """;
        Assert.Equal(3L, (long)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ApiAprobarMismaOrden_EnCarrera_RegistraUnAsientoYRespondeSin500()
    {
        Assert.True(_created);
        await using (var migrate = NewContext())
            await migrate.Database.MigrateAsync();

        var orderId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.Ordenes.Add(new MarketplaceERP.Finance.Entities.Orden
            {
                Id = orderId,
                NumeroOrden = "QA-FIN08-" + Guid.NewGuid().ToString("N"),
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                SubTotal = 100m,
                ImpuestoIva = 13m,
                MontoTotal = 113m,
                TipoCliente = MarketplaceERP.Finance.Entities.TipoCliente.B2C,
                Estado = MarketplaceERP.Finance.Entities.EstadoOrden.Pendiente,
                TipoComprobante = "FacturaConsumidorFinal",
                Moneda = "USD"
            });
            await seed.SaveChangesAsync();
        }

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Microsoft.AspNetCore.Mvc.IActionResult> Aprobar()
        {
            await using var db = NewContext();
            var accounting = new MarketplaceERP.Finance.Services.ContabilidadService(
                db, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    MarketplaceERP.Finance.Services.ContabilidadService>.Instance);
            var controller = new MarketplaceERP.API.Controllers.OrdenesController(
                db, accounting, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    MarketplaceERP.API.Controllers.OrdenesController>.Instance);
            await gate.Task;
            return await controller.TransicionarEstado(
                orderId, new MarketplaceERP.API.Controllers.TransicionEstadoRequest("Aprobada", null),
                CancellationToken.None);
        }

        var first = Aprobar();
        var second = Aprobar();
        gate.SetResult(true);
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, r => r is Microsoft.AspNetCore.Mvc.OkObjectResult);
        Assert.Single(results, r => r is Microsoft.AspNetCore.Mvc.ConflictObjectResult);

        await using var verify = NewContext();
        var order = await verify.Ordenes.SingleAsync(o => o.Id == orderId);
        Assert.Equal(MarketplaceERP.Finance.Entities.EstadoOrden.Aprobada, order.Estado);
        var journal = await verify.PartidasContables.Include(p => p.Lineas)
            .Where(p => p.TipoOperacion == "Venta" && p.ReferenciaOrigenId == orderId)
            .ToListAsync();
        var part = Assert.Single(journal);
        Assert.Equal(113m, part.TotalDebe);
        Assert.Equal(part.TotalDebe, part.TotalHaber);
        Assert.Equal(3, part.Lineas.Count);
    }

    [Fact]
    public async Task ApiAprobarConAsientoDeMontosDistintos_Rechaza409YNoCambiaOrden()
    {
        Assert.True(_created);
        await using (var migrated = NewContext())
            await migrated.Database.MigrateAsync();

        var orderId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.Ordenes.Add(new MarketplaceERP.Finance.Entities.Orden
            {
                Id = orderId,
                NumeroOrden = "QA-FIN08-" + Guid.NewGuid().ToString("N"),
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                SubTotal = 100m,
                ImpuestoIva = 13m,
                MontoTotal = 113m,
                TipoCliente = MarketplaceERP.Finance.Entities.TipoCliente.B2C,
                Estado = MarketplaceERP.Finance.Entities.EstadoOrden.Pendiente,
                TipoComprobante = "FacturaConsumidorFinal",
                Moneda = "USD"
            });
            await seed.SaveChangesAsync();
        }

        await using (var old = NewContext())
        {
            var accounting = new MarketplaceERP.Finance.Services.ContabilidadService(
                old, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    MarketplaceERP.Finance.Services.ContabilidadService>.Instance);
            await accounting.GenerarAsientoVentaAsync(
                orderId, 70m, 9.1m, "FacturaConsumidorFinal");
        }

        await using (var request = NewContext())
        {
            var accounting = new MarketplaceERP.Finance.Services.ContabilidadService(
                request, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    MarketplaceERP.Finance.Services.ContabilidadService>.Instance);
            var controller = new MarketplaceERP.API.Controllers.OrdenesController(
                request, accounting, Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    MarketplaceERP.API.Controllers.OrdenesController>.Instance);
            var result = await controller.TransicionarEstado(
                orderId, new MarketplaceERP.API.Controllers.TransicionEstadoRequest("Aprobada", null),
                CancellationToken.None);
            var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(result);
            Assert.Contains("ASIENTO_VENTA_INCONSISTENTE",
                System.Text.Json.JsonSerializer.Serialize(conflict.Value));
        }

        await using var verify = NewContext();
        Assert.Equal(MarketplaceERP.Finance.Entities.EstadoOrden.Pendiente,
            (await verify.Ordenes.SingleAsync(o => o.Id == orderId)).Estado);
        var journal = await verify.PartidasContables
            .Where(p => p.TipoOperacion == "Venta" && p.ReferenciaOrigenId == orderId)
            .ToListAsync();
        Assert.Equal(79.1m, Assert.Single(journal).TotalDebe);
    }

    [Fact]
    public async Task HttpJwtAdmin_DosAprobacionesConcurrentes_200Y409SinDobleAsiento()
    {
        Assert.True(_created);
        await using (var migrated = NewContext())
            await migrated.Database.MigrateAsync();

        var orderId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.Ordenes.Add(new MarketplaceERP.Finance.Entities.Orden
            {
                Id = orderId,
                NumeroOrden = "HTTP-QA-" + Guid.NewGuid().ToString("N"),
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                SubTotal = 100m,
                ImpuestoIva = 13m,
                MontoTotal = 113m,
                TipoCliente = MarketplaceERP.Finance.Entities.TipoCliente.B2C,
                Estado = MarketplaceERP.Finance.Entities.EstadoOrden.Pendiente,
                TipoComprobante = "FacturaConsumidorFinal",
                Moneda = "USD"
            });
            await seed.SaveChangesAsync();
        }

        const string issuer = "fin08-qa";
        const string secret = "FIN08-HTTP-TEST-KEY-ONLY-NOT-FOR-PRODUCTION-2026";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(_testConnection));
                services.AddScoped<IContabilidadService, ContabilidadService>();
                services.AddSingleton(new PaymentOperationsMode(false));
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = secret, Issuer = issuer, Audience = issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers().AddApplicationPart(typeof(OrdenesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e => e.MapControllers());
            }));

        string Token(string role)
        {
            var jwt = new JwtSecurityToken(
                issuer: issuer, audience: issuer,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, role)
                },
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        var route = $"/api/ordenes/{orderId}/estado";
        var payload = new TransicionEstadoRequest("Aprobada", null);
        using (var anonymous = server.CreateClient())
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anonymous.PutAsJsonAsync(route, payload)).StatusCode);

        using (var seller = server.CreateClient())
        {
            seller.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token("Vendedor"));
            Assert.Equal(HttpStatusCode.Forbidden,
                (await seller.PutAsJsonAsync(route, payload)).StatusCode);
        }

        using var admin = server.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token("Admin"));

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Approve()
        {
            await gate.Task;
            return await admin.PutAsJsonAsync(route, payload);
        }

        var one = Approve();
        var two = Approve();
        gate.SetResult(true);
        var responses = await Task.WhenAll(one, two);
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            var duplicate = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Contains("ESTADO_YA_APLICADO",
                await duplicate.Content.ReadAsStringAsync());
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        await using var verify = NewContext();
        Assert.Equal(MarketplaceERP.Finance.Entities.EstadoOrden.Aprobada,
            (await verify.Ordenes.SingleAsync(o => o.Id == orderId)).Estado);
        var entry = await verify.PartidasContables.Include(x => x.Lineas)
            .Where(x => x.TipoOperacion == "Venta" && x.ReferenciaOrigenId == orderId)
            .ToListAsync();
        var journal = Assert.Single(entry);
        Assert.Equal(3, journal.Lineas.Count);
        Assert.Equal(113m, journal.TotalDebe);
        Assert.Equal(journal.TotalDebe, journal.TotalHaber);
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null || _testConnection is null ||
            !_database.StartsWith("ci_fin08_", StringComparison.Ordinal))
            return;

        using (var ephemeral = new NpgsqlConnection(_testConnection))
            NpgsqlConnection.ClearPool(ephemeral);
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }
}
