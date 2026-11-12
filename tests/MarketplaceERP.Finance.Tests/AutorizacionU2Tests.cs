using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.API.Services;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

// Real authentication/authorization middleware, in memory. No routing server, action body,
// application entry point, database, EF provider or network listener is instantiated.
public class AutorizacionU2Tests
{
    private static JwtSettings Settings() => new()
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        Issuer = "u2-tests.invalid", Audience = "u2-client.invalid", ExpirationMinutes = 60
    };

    private static string Token(JwtSettings settings, string role = "Admin", string mode = "valid")
    {
        var id = Guid.NewGuid().ToString();
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, id), new(ClaimTypes.Role, role) };
        // Login issues both sub and NameIdentifier. Test the actual dual representation too.
        claims.Add(new Claim(ClaimTypes.NameIdentifier, mode == "conflicting-id" ? Guid.NewGuid().ToString() : id));
        if (mode == "invalid-id") claims = new() { new(JwtRegisteredClaimNames.Sub, "invalid"), new(ClaimTypes.Role, role) };
        if (mode == "missing-id") claims = new() { new(ClaimTypes.Role, role) };
        var key = mode == "signature" ? Settings().Key : settings.Key;
        var token = new JwtSecurityToken(
            issuer: mode == "issuer" ? "other.invalid" : settings.Issuer,
            audience: mode == "audience" ? "other-client.invalid" : settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: mode == "no-expiry" ? null : mode == "expired" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                mode == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<int> Probe(Type controller, string method, string? role, string mode = "valid")
    {
        var settings = Settings();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddMarketplaceSecurity(settings);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var action = controller.GetMethod(method, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Missing action");
        var metadata = controller.GetCustomAttributes(inherit: true)
            .Concat(action.GetCustomAttributes(inherit: true)).ToArray();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "U2 probe"));
        if (role is not null) context.Request.Headers.Authorization = "Bearer " +
            (mode == "malformed" ? "not-a-jwt" : Token(settings, role, mode));
        context.Response.Body = new MemoryStream();
        var app = new ApplicationBuilder(provider);
        app.UseAuthentication();
        app.UseAuthorization();
        app.Run(ctx => { ctx.Response.StatusCode = StatusCodes.Status200OK; return Task.CompletedTask; });
        await app.Build()(context);
        return context.Response.StatusCode;
    }

    [Theory]
    [InlineData(typeof(ComprasController), "ListarPO")]
    [InlineData(typeof(ContabilidadController), "LibroDiario")]
    [InlineData(typeof(KpisDashboardController), "ObtenerDashboard")]
    [InlineData(typeof(LiquidacionesController), "Historial")]
    [InlineData(typeof(UsuariosController), "Crear")]
    public async Task AdministrativeActions_RejectAnonymousAndNonAdmin(Type controller, string action)
    {
        Assert.NotNull(controller.GetMethod(action));
        foreach (var method in controller.GetMethods().Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
        {
            Assert.Equal(401, await Probe(controller, method.Name, null));
            Assert.Equal(403, await Probe(controller, method.Name, "Comprador"));
            Assert.Equal(403, await Probe(controller, method.Name, "Vendedor"));
            Assert.Equal(200, await Probe(controller, method.Name, "Admin"));
        }
    }

    [Theory]
    [InlineData(typeof(ComprasController), "ListarPO", false)]
    [InlineData(typeof(ComprasController), "EjecutarMatch", false)]
    [InlineData(typeof(ContabilidadController), "LibroDiario", false)]
    [InlineData(typeof(ContabilidadController), "AsientoManual", true)]
    [InlineData(typeof(LiquidacionesController), "Historial", false)]
    [InlineData(typeof(LiquidacionesController), "Generar", true)]
    [InlineData(typeof(LiquidacionesController), "ProcesarPago", true)]
    [InlineData(typeof(KpisDashboardController), "ObtenerDashboard", false)]
    public async Task PhaseOneAdministrativeRoutes_EnforceRolesBeforeActionBody(
        Type controller, string actionName, bool requiresPaymentGuard)
    {
        var action = controller.GetMethod(actionName)
            ?? throw new InvalidOperationException("Missing action");
        Assert.Equal("Admin", controller.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        Assert.NotEmpty(action.GetCustomAttributes<HttpMethodAttribute>());
        Assert.Equal(requiresPaymentGuard,
            action.IsDefined(typeof(PaymentOperationsGuardAttribute), inherit: true));

        // Probe invokes authentication and authorization middleware, then a dummy endpoint.
        // A 200 here proves admission through the policy, not successful action execution.
        Assert.Equal(401, await Probe(controller, actionName, null));
        Assert.Equal(403, await Probe(controller, actionName, "Comprador"));
        Assert.Equal(403, await Probe(controller, actionName, "Vendedor"));
        Assert.Equal(200, await Probe(controller, actionName, "Admin"));
    }

    [Theory]
    [InlineData(typeof(WmsController))]
    [InlineData(typeof(RmaController))]
    public async Task OperationalActions_RejectBuyers(Type controller)
    {
        foreach (var method in controller.GetMethods().Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
        {
            Assert.Equal(401, await Probe(controller, method.Name, null));
            Assert.Equal(403, await Probe(controller, method.Name, "Comprador"));
            Assert.Equal(403, await Probe(controller, method.Name, "Vendedor"));
            Assert.Equal(200, await Probe(controller, method.Name, "Admin"));
        }
    }

    [Fact]
    public async Task OrderCreation_RequiresAdminWithoutExecutingOrderLogic()
    {
        Assert.Equal(401, await Probe(typeof(OrdenesController), "Crear", null));
        Assert.Equal(403, await Probe(typeof(OrdenesController), "Crear", "Vendedor"));
        Assert.Equal(200, await Probe(typeof(OrdenesController), "Crear", "Admin"));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("algorithm")]
    [InlineData("invalid-id")]
    [InlineData("missing-id")]
    [InlineData("conflicting-id")]
    [InlineData("no-expiry")]
    [InlineData("malformed")]
    public async Task InvalidTokens_AreRejected(string mode) =>
        Assert.Equal(401, await Probe(typeof(UsuariosController), "Crear", "Admin", mode));

    [Fact]
    public async Task PublicLogin_MetadataAllowsAnonymousWithoutRunningLogin() =>
        Assert.Equal(200, await Probe(typeof(AuthController), "Login", null));

    [Fact]
    public async Task EveryPrivateControllerAction_RejectsAnonymous_IncludingFallbackPolicy()
    {
        foreach (var controller in typeof(AuthController).Assembly.GetTypes()
                     .Where(t => t.Namespace == typeof(AuthController).Namespace && t.Name.EndsWith("Controller")))
        foreach (var action in controller.GetMethods().Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
        {
            var anonymous = controller.IsDefined(typeof(AllowAnonymousAttribute), true) ||
                            action.IsDefined(typeof(AllowAnonymousAttribute), true);
            var publicLogin = controller == typeof(AuthController) && action.Name == nameof(AuthController.Login);
            Assert.Equal(publicLogin, anonymous); // An accidental AllowAnonymous must fail this test.
            Assert.Equal(publicLogin ? 200 : 401, await Probe(controller, action.Name, null));
        }
    }

    [Fact]
    public async Task EveryHttpAction_EnforcesExplicitRoleMatrix_IncludingAllRouteAliases()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(AuthController).Namespace && t.Name.EndsWith("Controller")).ToArray();
        var actions = 0;
        var routes = 0;
        foreach (var controller in controllers)
        foreach (var action in controller.GetMethods().Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
        {
            actions++;
            var publicLogin = controller == typeof(AuthController) && action.Name == nameof(AuthController.Login);
            // Las dos acciones V2 solo existen en sandbox QA y conservan
            // exactamente la misma matriz de Comprador que checkout V1.
            // El acceso de Admin, Vendedor y anónimo debe seguir prohibido.
            var buyerCheckout =
                (controller == typeof(CheckoutController) &&
                    action.Name == nameof(CheckoutController.ProcesarCheckout)) ||
                (controller == typeof(CheckoutV2QaQuoteController) &&
                    action.Name == nameof(CheckoutV2QaQuoteController.CotizarSoloQa)) ||
                (controller == typeof(CheckoutV2QaSimulatedController) &&
                    action.Name == nameof(CheckoutV2QaSimulatedController.ComprarSimulado));
            var buyerTracking = controller == typeof(CheckoutSeguimientoController) && action.Name == nameof(CheckoutSeguimientoController.Seguimiento);
            var buyerOrder = controller == typeof(OrdenesController) && action.Name == nameof(OrdenesController.Obtener);
            var sellerOwnOrders = controller == typeof(OrdenesController) && (
                action.Name == nameof(OrdenesController.ListarMisOrdenes) ||
                action.Name == nameof(OrdenesController.ObtenerMiOrden));
            if (sellerOwnOrders)
                Assert.Equal("Vendedor", action.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            var isProductController = controller == typeof(ProductosController);
            if (isProductController && action.Name == nameof(ProductosController.ValidarCsv))
            {
                // La simulación no escribe, pero sigue recibiendo archivos del usuario:
                // acceso explícito solo para Admin, nunca Vendedor/Comprador.
                Assert.Equal("Admin", action.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            }
            var isProductCatalogRead = isProductController && (
                action.Name == nameof(ProductosController.GetPublicados) ||
                action.Name == nameof(ProductosController.Buscar) ||
                action.Name == nameof(ProductosController.GetById));
            var isProductSellerOp = isProductController && (
                action.Name == nameof(ProductosController.OpcionesAlta) ||
                action.Name == nameof(ProductosController.Create) ||
                action.Name == nameof(ProductosController.Update) ||
                action.Name == nameof(ProductosController.Delete) ||
                action.Name == nameof(ProductosController.Publicar) ||
                action.Name == nameof(ProductosController.GetAlertasStock) ||
                action.Name == nameof(ProductosController.GetCatalogoB2B));

            var allowsSeller = publicLogin || isProductCatalogRead || isProductSellerOp || sellerOwnOrders;
            var allowsBuyer = publicLogin || buyerCheckout || buyerTracking || buyerOrder || isProductCatalogRead;

            var metadata = controller.GetCustomAttributes(inherit: true).Concat(action.GetCustomAttributes(inherit: true)).ToArray();
            Assert.Equal(publicLogin, metadata.OfType<IAllowAnonymous>().Any());
            foreach (var alias in action.GetCustomAttributes<HttpMethodAttribute>())
            foreach (var verb in alias.HttpMethods)
            {
                routes++;
                // Authorization is independent of route matching; each alias must carry the same metadata.
                Assert.False(string.IsNullOrWhiteSpace(verb));
                Assert.Equal(publicLogin ? 200 : 401, await Probe(controller, action.Name, null));
                Assert.Equal(allowsSeller ? 200 : 403, await Probe(controller, action.Name, "Vendedor"));
                Assert.Equal(allowsBuyer ? 200 : 403, await Probe(controller, action.Name, "Comprador"));
                Assert.Equal(buyerCheckout || sellerOwnOrders ? 403 : 200,
                    await Probe(controller, action.Name, "Admin"));
                if (!publicLogin)
                {
                    Assert.Equal(401, await Probe(controller, action.Name, "Admin", "expired"));
                    Assert.Equal(401, await Probe(controller, action.Name, "Admin", "signature"));
                }
            }
        }
        // Base original: 23 controladores / 85 acciones / 86 rutas.
        // SOLO en sandbox: +2 endpoints QA de comprador y +1 endpoint
        // de referencias reales de producto para Admin/Vendedor.
        // Ninguna autorización queda implícita.
        Assert.Equal(25, controllers.Length);
        // F103 agrega dos GET exclusivos de Vendedor sin ampliar rutas Admin.
        Assert.Equal(90, actions);
        Assert.Equal(91, routes); // Productos/GetAlertasStock tiene dos alias.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void InvalidExpiration_IsRejectedBeforeStartup(int minutes)
    {
        var settings = Settings(); settings.ExpirationMinutes = minutes;
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMarketplaceSecurity(settings));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("DEV_ONLY_placeholder_not_a_private_key")]
    [InlineData("CAMBIA_placeholder_not_a_private_key")]
    public void UnsafeKey_IsRejected(string key)
    {
        var settings = Settings(); settings.Key = key;
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMarketplaceSecurity(settings));
    }

    [Fact]
    public void EmptyIssuerOrAudience_IsRejected()
    {
        var settings = Settings(); settings.Issuer = "";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMarketplaceSecurity(settings));
        settings = Settings(); settings.Audience = " ";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddMarketplaceSecurity(settings));
    }
}
