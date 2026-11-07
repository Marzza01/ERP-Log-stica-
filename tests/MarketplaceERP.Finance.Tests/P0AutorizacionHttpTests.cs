using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Shared.Settings;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
/// P0: validación de la política HTTP real [Authorize(Roles="Admin")]
/// con tokens JWT firmados para la prueba; nunca acepta headers de rol falsos.
/// </summary>
public class P0AutorizacionHttpTests
{
    private const string Issuer = "p0-ci";
    private const string Audience = "p0-ci";
    private const string Secret = "P0-ONLY-IN-CI-NEVER-PRODUCTION-KEY-32-CHARS-MINIMUM";

    [Fact]
    public async Task ApiCompras_RechazaSinToken_TokenFalso_YRolNoAdmin()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var matching = new Mock<ITresWayMatchingService>(MockBehavior.Strict);
        var liquidaciones = new Mock<ILiquidacionService>(MockBehavior.Strict);
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<FinanceDbContext>(o =>
                    o.UseInMemoryDatabase("p0-auth-finance-" + suffix));
                services.AddDbContext<CoreDbContext>(o =>
                    o.UseInMemoryDatabase("p0-auth-core-" + suffix));
                services.AddSingleton(matching.Object);
                services.AddSingleton(liquidaciones.Object);
                services.AddSingleton(new PaymentOperationsMode(true));
                services.AddSingleton(Mock.Of<IContabilidadService>());
                services.AddSingleton(Mock.Of<IInventoryService>());

                // Ejercitar la MISMA configuración de autenticación y roles
                // que utiliza Program.cs, no una política JWT inventada por CI.
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = Secret, Issuer = Issuer, Audience = Audience,
                    ExpirationMinutes = 5
                });
                services.AddControllers().AddApplicationPart(typeof(ComprasController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

        using var anon = server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.GetAsync("/api/compras/po")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsync("/api/compras/po",
                new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);

        using var falsificado = server.CreateClient();
        falsificado.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid.signature");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await falsificado.GetAsync("/api/compras/po")).StatusCode);

        using var vendedor = server.CreateClient();
        vendedor.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CrearJwt("Vendedor", key));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await vendedor.GetAsync("/api/compras/po")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await vendedor.PostAsync("/api/compras/po",
                new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);

        // Denegar también el libro contable, el OMS administrativo y
        // operaciones financieras. El filtro de pago nunca debe activarse
        // para usuarios sin autorización.
        foreach (var route in new[]
        {
            "/api/contabilidad/libro-diario",
            "/api/ordenes"
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await anon.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await vendedor.GetAsync(route)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsync("/api/liquidaciones/generar",
                new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await vendedor.PostAsync("/api/liquidaciones/generar",
                new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);

        using var admin = server.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CrearJwt("Admin", key));
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/compras/po")).StatusCode);

        // Incluso un JWT Admin válido no puede ejecutar efectos financieros
        // si el modo de pagos se encuentra deshabilitado.
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await admin.PostAsync("/api/liquidaciones/generar",
                new StringContent(
                    "{\\"vendedorId\\":\\"" + Guid.NewGuid() + "\\"}",
                    Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await admin.PostAsync(
                "/api/liquidaciones/procesar-pago/" + Guid.NewGuid(), null)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await admin.PostAsync("/api/compras/match",
                new StringContent(
                    "{\\"ordenCompraId\\":\\"" + Guid.NewGuid() +
                    "\\",\\"facturaProveedorId\\":\\"" + Guid.NewGuid() +
                    "\\",\\"cantidadRecibidaBodega\\":1}",
                    Encoding.UTF8, "application/json"))).StatusCode);

        matching.Verify(m => m.EjecutarMatchAsync(
            It.IsAny<Validar3WayMatchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        liquidaciones.Verify(l => l.GenerarLiquidacionAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        liquidaciones.Verify(l => l.ProcesarPagoAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        using var scope = server.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(0, await finance.OrdenesCompra.CountAsync());
        Assert.Equal(0, await finance.FacturasProveedor.CountAsync());
        Assert.Equal(0, await finance.MatchesTridireccionales.CountAsync());
        Assert.Equal(0, await finance.PartidasContables.CountAsync());
    }

    private static string CrearJwt(string rol, SecurityKey key)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, rol)
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
