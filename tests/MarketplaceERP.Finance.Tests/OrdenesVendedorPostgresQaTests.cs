using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

// F103: comprueba traduccion SQL en PostgreSQL LOCAL; nunca crea DB en Neon.
// La BD se genera con GUID y se elimina solo si este test la ha creado.
public sealed class OrdenesVendedorPostgresQaTests
{
    [Fact]
    public async Task DosVendedores_ConsultasSqlFiltradasYDetalleAjenoOculto()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("F103 requiere TEST_PG_CONNECTION local; no omitir.");
        var baseConnection = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                 baseConnection.Host, StringComparer.OrdinalIgnoreCase) ||
            baseConnection.Port != 5432 || baseConnection.Database != "postgres" ||
            baseConnection.Username != "postgres" || string.IsNullOrWhiteSpace(baseConnection.Password))
            throw new InvalidOperationException("F103 solo postgres@localhost:5432/postgres.");
        var databaseName = "gate65_f103_seller_" + Guid.NewGuid().ToString("N");
        var adminConnection = baseConnection.ConnectionString;
        baseConnection.Database = databaseName;
        var testConnection = baseConnection.ConnectionString;
        var dbCreated = false;
        try
        {
            await using (var c = new NpgsqlConnection(adminConnection))
            {
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE DATABASE \"" + databaseName + "\"";
                await cmd.ExecuteNonQueryAsync();
                dbCreated = true;
            }

            await using (var core = new CoreDbContext(
                new DbContextOptionsBuilder<CoreDbContext>()
                    .UseNpgsql(testConnection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "core")).Options))
                await core.Database.MigrateAsync();
            await using (var catalog = new CatalogDbContext(
                new DbContextOptionsBuilder<CatalogDbContext>()
                    .UseNpgsql(testConnection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")).Options))
                await catalog.Database.MigrateAsync();

            var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseNpgsql(testConnection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "finance")).Options;
            var sellerA = Guid.NewGuid();
            var sellerB = Guid.NewGuid();
            var orderA = Guid.NewGuid();
            var orderB = Guid.NewGuid();
            await using (var db = new FinanceDbContext(financeOptions))
            {
                await db.Database.MigrateAsync();
                db.Ordenes.AddRange(
                    new Orden {
                        Id = orderA, VendedorId = sellerA, CompradorId = Guid.NewGuid(),
                        NumeroOrden = "F103-PG-A-" + Guid.NewGuid().ToString("N")[..8],
                        MontoTotal = 15m, Estado = EstadoOrden.Pendiente,
                        DireccionEntrega = "PRIVADO-PG-A"
                    },
                    new Orden {
                        Id = orderB, VendedorId = sellerB, CompradorId = Guid.NewGuid(),
                        NumeroOrden = "F103-PG-B-" + Guid.NewGuid().ToString("N")[..8],
                        MontoTotal = 9999m, Estado = EstadoOrden.Entregada,
                        DireccionEntrega = "PRIVADO-PG-B"
                    });
                db.ItemsOrden.AddRange(
                    new ItemOrden { Id = Guid.NewGuid(), OrdenId = orderA,
                        ProductoId = Guid.NewGuid(), SkuReferencia = "PG-A",
                        NombreProducto = "Articulo A", Cantidad = 1, PrecioUnitario = 15m },
                    new ItemOrden { Id = Guid.NewGuid(), OrdenId = orderB,
                        ProductoId = Guid.NewGuid(), SkuReferencia = "PG-B",
                        NombreProducto = "Articulo B", Cantidad = 1, PrecioUnitario = 9999m });
                await db.SaveChangesAsync();
            }

            var jwt = new JwtSettings {
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                Issuer = "f103-pg.invalid",
                Audience = "f103-pg-client.invalid",
                ExpirationMinutes = 20
            };
            using var server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddDbContext<FinanceDbContext>(options =>
                        options.UseNpgsql(testConnection, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
                    services.AddSingleton(Mock.Of<IContabilidadService>());
                    services.AddMarketplaceSecurity(jwt);
                    services.AddControllers().AddApplicationPart(typeof(OrdenesController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                }));

            using var client = server.CreateClient();
            var id = Guid.NewGuid().ToString();
            var claims = new List<Claim> {
                new(JwtRegisteredClaimNames.Sub, id),
                new(ClaimTypes.NameIdentifier, id),
                new(ClaimTypes.Role, "Vendedor"),
                new("VendedorId", sellerA.ToString())
            };
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: jwt.Issuer, audience: jwt.Audience, claims: claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    SecurityAlgorithms.HmacSha256)));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var list = await client.GetAsync("/api/ordenes/mis-ordenes");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var listRaw = await list.Content.ReadAsStringAsync();
            using var listDoc = JsonDocument.Parse(listRaw);
            Assert.Equal(1, listDoc.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(orderA.ToString(),
                listDoc.RootElement.GetProperty("ordenes")[0].GetProperty("id").GetString());
            Assert.Equal(1, listDoc.RootElement.GetProperty("ordenes")[0]
                .GetProperty("totalItems").GetInt32());
            Assert.DoesNotContain("PRIVADO-PG", listRaw);
            Assert.DoesNotContain("9999", listRaw);

            var detail = await client.GetAsync($"/api/ordenes/mis-ordenes/{orderA}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var detailRaw = await detail.Content.ReadAsStringAsync();
            Assert.Contains("PG-A", detailRaw);
            Assert.DoesNotContain("PRIVADO-PG-A", detailRaw);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/ordenes/mis-ordenes/{orderB}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.GetAsync("/api/ordenes")).StatusCode);

            await using var checkDb = new FinanceDbContext(financeOptions);
            Assert.Equal(2, await checkDb.Ordenes.CountAsync());
            Assert.Equal(2, await checkDb.ItemsOrden.CountAsync());
        }
        finally
        {
            if (dbCreated)
            {
                await using var admin = new NpgsqlConnection(adminConnection);
                await admin.OpenAsync();
                await using var drop = admin.CreateCommand();
                drop.CommandText = "DROP DATABASE IF EXISTS \"" + databaseName + "\" WITH (FORCE)";
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
}
