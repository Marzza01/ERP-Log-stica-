using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
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
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Comprobación de rutas OMS vs checkout, SOLO EF InMemory + TestServer.
/// Captura la semántica actual sin ejecutar pasarelas, PostgreSQL ni producción.
/// </summary>
public sealed class OrdenesPostAlternativoHttpQaTests
{
    private const string Issuer = "qa-ordenes-post";
    private const string Secret = "QA-ORDENES-POST-ALTERNATIVO-20261109-NO-REAL-PAYMENTS";

    private static string Token(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Issuer,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role)
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task PostOrdenes_SinTokenOCompradorOrVendedor_DeniegaAntesDeEscribir()
    {
        using var server = CreateServer(out _);
        var payload = new CrearOrdenRequest(Guid.NewGuid(), Guid.NewGuid(),
            TipoCliente.B2C, null, null, null, null, null, 5m,
            new List<ItemOrdenRequest>
            {
                new(Guid.NewGuid(), "SKU-QA", "QA", 1, 20m)
            });

        using var anon = server.CreateClient();
        using var responseAnon = await anon.PostAsJsonAsync("/api/ordenes", payload);
        Assert.Equal(HttpStatusCode.Unauthorized, responseAnon.StatusCode);
        foreach (var role in new[] { "Comprador", "Vendedor" })
        {
            using var client = server.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token(role));
            using var result = await client.PostAsJsonAsync("/api/ordenes", payload);
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        }

        using var scope = server.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Empty(await finance.Ordenes.AsNoTracking().ToListAsync());
        Assert.Empty(await finance.ItemsOrden.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PostOrdenes_AdminNoPuedePersistirImporteNegativo()
    {
        using var server = CreateServer(out _);
        var payload = new CrearOrdenRequest(Guid.NewGuid(), Guid.NewGuid(),
            TipoCliente.B2C, null, null, null, null, null, 0m,
            new List<ItemOrdenRequest>
            {
                new(Guid.NewGuid(), "SKU-NO-VALIDADO", "Importe origen cliente", 1, -99m)
            });
        using var admin = server.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token("Admin"));
        using var result = await admin.PostAsJsonAsync("/api/ordenes", payload);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var scope = server.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Empty(await finance.Ordenes.AsNoTracking().ToListAsync());
        Assert.Empty(await finance.ItemsOrden.AsNoTracking().ToListAsync());
        Assert.Empty(await finance.IntentosPago.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(-1, 1, 0)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 1, -1)]
    public async Task PostOrdenes_AdminRechazaImporteCantidadOEnvioInvalidos(
        int precio, int cantidad, int envio)
    {
        using var server = CreateServer(out _);
        using var admin = server.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token("Admin"));
        var payload = new CrearOrdenRequest(Guid.NewGuid(), Guid.NewGuid(),
            TipoCliente.B2C, null, null, null, null, null, envio,
            new List<ItemOrdenRequest>
            {
                new(Guid.NewGuid(), "SKU-QA", "QA", cantidad, precio)
            });
        using var result = await admin.PostAsJsonAsync("/api/ordenes", payload);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        using var scope = server.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<FinanceDbContext>()
            .Ordenes.CountAsync());
    }

    [Fact]
    public async Task PostOrdenes_AdminValidoConserva201_PeroOperacionFinancieraBloqueada()
    {
        using var server = CreateServer(out _);
        using var admin = server.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token("Admin"));
        var payload = new CrearOrdenRequest(Guid.NewGuid(), Guid.NewGuid(),
            TipoCliente.B2C, null, null, null, null, null, 5m,
            new List<ItemOrdenRequest>
            {
                new(Guid.NewGuid(), "SKU-QA", "QA", 2, 10m)
            });
        using var create = await admin.PostAsJsonAsync("/api/ordenes", payload);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Guid orderId;
        using (var scope = server.Services.CreateScope())
        {
            var order = await scope.ServiceProvider.GetRequiredService<FinanceDbContext>()
                .Ordenes.AsNoTracking().SingleAsync();
            orderId = order.Id;
            Assert.Equal($"ORD-{order.FechaCreacion:yyyyMMdd}-{order.Id:N}", order.NumeroOrden);
            Assert.Equal(45, order.NumeroOrden.Length);
            Assert.Equal(20m, order.SubTotal);
            Assert.Equal(2.60m, order.ImpuestoIva);
            Assert.Equal(27.60m, order.MontoTotal);
            Assert.Equal(EstadoOrden.Pendiente, order.Estado);
        }
        using var transition = await admin.PutAsJsonAsync(
            $"/api/ordenes/{orderId}/estado",
            new TransicionEstadoRequest("Aprobada", null));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, transition.StatusCode);
        using var verifyScope = server.Services.CreateScope();
        var finance = verifyScope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(EstadoOrden.Pendiente,
            (await finance.Ordenes.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(0, await finance.PartidasContables.CountAsync());
    }

    private static TestServer CreateServer(out string databaseName)
    {
        databaseName = "qa-orders-" + Guid.NewGuid().ToString("N");
        string name = databaseName;
        return new TestServer(new WebHostBuilder()
            .UseEnvironment("Production")
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<FinanceDbContext>(options =>
                    options.UseInMemoryDatabase(name));
                services.AddSingleton(new PaymentOperationsMode(true));
                services.AddSingleton(Mock.Of<IContabilidadService>());
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = Secret, Issuer = Issuer, Audience = Issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers()
                    .AddApplicationPart(typeof(OrdenesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));
    }
}
