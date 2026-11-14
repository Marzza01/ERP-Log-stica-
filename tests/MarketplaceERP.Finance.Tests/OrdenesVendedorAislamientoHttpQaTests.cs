using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

// F103: pruebas HTTP reales en memoria, sin red/Neon/pagos.
// Los dos vendedores tienen pedidos distintos; los ajenos nunca se proyectan.
public sealed class OrdenesVendedorAislamientoHttpQaTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestServer _server;
        private readonly JwtSettings _jwt;
        public Guid SellerA { get; } = Guid.NewGuid();
        public Guid SellerB { get; } = Guid.NewGuid();
        public Guid OrderA1 { get; } = Guid.NewGuid();
        public Guid OrderA2 { get; } = Guid.NewGuid();
        public Guid OrderB { get; } = Guid.NewGuid();

        private Fixture(TestServer server, JwtSettings jwt)
        {
            _server = server;
            _jwt = jwt;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var name = "seller-orders-f103-" + Guid.NewGuid().ToString("N");
            var jwt = new JwtSettings
            {
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                Issuer = "f103-orders.invalid",
                Audience = "f103-orders-client.invalid",
                ExpirationMinutes = 20
            };
            var server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddDbContext<FinanceDbContext>(options =>
                        options.UseInMemoryDatabase(name));
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
            var fixture = new Fixture(server, jwt);
            using var scope = server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

            var a1 = new Orden {
                Id = fixture.OrderA1, NumeroOrden = "F103-A-ONE",
                CompradorId = Guid.NewGuid(), VendedorId = fixture.SellerA,
                MontoTotal = 15m, Estado = EstadoOrden.Pendiente,
                FechaCreacion = DateTime.UtcNow.AddHours(-1),
                DireccionEntrega = "DATOS_PRIVADOS_COMPRADOR_A",
                IdempotencyKey = "SECRETO_IDEMPOTENCIA_A"
            };
            var a2 = new Orden {
                Id = fixture.OrderA2, NumeroOrden = "F103-A-TWO",
                CompradorId = Guid.NewGuid(), VendedorId = fixture.SellerA,
                MontoTotal = 25m, Estado = EstadoOrden.Cancelada,
                FechaCreacion = DateTime.UtcNow.AddHours(-2)
            };
            var b = new Orden {
                Id = fixture.OrderB, NumeroOrden = "F103-B-FOREIGN",
                CompradorId = Guid.NewGuid(), VendedorId = fixture.SellerB,
                MontoTotal = 999999m, Estado = EstadoOrden.Pendiente,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-30),
                DireccionEntrega = "DATOS_PRIVADOS_COMPRADOR_B",
                IdempotencyKey = "SECRETO_IDEMPOTENCIA_B"
            };
            db.Ordenes.AddRange(a1, a2, b);
            db.ItemsOrden.AddRange(
                new ItemOrden { Id = Guid.NewGuid(), OrdenId = a1.Id,
                    ProductoId = Guid.NewGuid(), SkuReferencia = "SKU-A",
                    NombreProducto = "Producto A", Cantidad = 2, PrecioUnitario = 7.5m },
                new ItemOrden { Id = Guid.NewGuid(), OrdenId = b.Id,
                    ProductoId = Guid.NewGuid(), SkuReferencia = "SKU-B-PRIVADO",
                    NombreProducto = "Producto privado B", Cantidad = 1, PrecioUnitario = 999999m });
            await db.SaveChangesAsync();
            return fixture;
        }

        public HttpClient Client(string? role = null, params string[] sellerClaims)
        {
            var client = _server.CreateClient();
            if (role is null) return client;
            var id = Guid.NewGuid().ToString();
            var claims = new List<Claim> {
                new(JwtRegisteredClaimNames.Sub, id),
                new(ClaimTypes.NameIdentifier, id),
                new(ClaimTypes.Role, role)
            };
            claims.AddRange(sellerClaims.Select(c => new Claim("VendedorId", c)));
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)),
                SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: _jwt.Issuer, audience: _jwt.Audience, claims: claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: credentials));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public void Dispose() => _server.Dispose();
    }

    [Fact]
    public async Task AnonimoYRolNoVendedor_NoPuedenListarNiConsultarDetalle()
    {
        using var f = await Fixture.CreateAsync();
        using var anonymous = f.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/ordenes/mis-ordenes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/ordenes/mis-ordenes/{f.OrderA1}")).StatusCode);
        using var buyer = f.Client("Comprador", f.SellerA.ToString());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await buyer.GetAsync("/api/ordenes/mis-ordenes")).StatusCode);
        using var admin = f.Client("Admin");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/ordenes/mis-ordenes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/ordenes")).StatusCode);
    }

    [Fact]
    public async Task VendedorA_ListaSoloSusOrdenesSinMetadatosPrivadosYAdminGlobalNoSeAbre()
    {
        using var f = await Fixture.CreateAsync();
        using var a = f.Client("Vendedor", f.SellerA.ToString());
        var response = await a.GetAsync("/api/ordenes/mis-ordenes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
        var ordenes = json.RootElement.GetProperty("ordenes").EnumerateArray().ToArray();
        Assert.Equal(2, ordenes.Length);
        Assert.Contains(ordenes, x => x.GetProperty("numeroOrden").GetString() == "F103-A-ONE");
        Assert.Contains(ordenes, x => x.GetProperty("numeroOrden").GetString() == "F103-A-TWO");
        Assert.All(ordenes, x => Assert.Equal(8, x.EnumerateObject().Count()));
        Assert.DoesNotContain("F103-B-FOREIGN", raw);
        Assert.DoesNotContain("DATOS_PRIVADOS", raw);
        Assert.DoesNotContain("SECRETO_IDEMPOTENCIA", raw);
        Assert.DoesNotContain("999999", raw);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await a.GetAsync("/api/ordenes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await a.GetAsync("/api/ordenes/kpis")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await a.GetAsync($"/api/ordenes/{f.OrderA1}")).StatusCode);
    }

    [Fact]
    public async Task VendedorB_NoPuedeVerDetalleDeA_YRespuestaNoDistingueInexistencia()
    {
        using var f = await Fixture.CreateAsync();
        using var b = f.Client("Vendedor", f.SellerB.ToString());
        var own = await b.GetAsync($"/api/ordenes/mis-ordenes/{f.OrderB}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var ownJson = await own.Content.ReadAsStringAsync();
        Assert.Contains("SKU-B-PRIVADO", ownJson);
        Assert.DoesNotContain("DATOS_PRIVADOS_COMPRADOR_B", ownJson);
        Assert.DoesNotContain("SECRETO_IDEMPOTENCIA_B", ownJson);
        var foreign = await b.GetAsync($"/api/ordenes/mis-ordenes/{f.OrderA1}");
        var missing = await b.GetAsync($"/api/ordenes/mis-ordenes/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // El traceId cambia en cada HTTP; el contrato 404 y sus campos publicos deben ser iguales.
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        using var foreignJson = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync());
        Assert.Equal(404, missingJson.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(404, foreignJson.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(missingJson.RootElement.GetProperty("title").GetString(),
                     foreignJson.RootElement.GetProperty("title").GetString());
        Assert.False(foreignJson.RootElement.TryGetProperty("numeroOrden", out _));
        using var a = f.Client("Vendedor", f.SellerA.ToString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await a.GetAsync($"/api/ordenes/mis-ordenes/{f.OrderB}")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task VendedorSinClaimCorrecto_NoPuedeLeer(string? vendedorId)
    {
        using var f = await Fixture.CreateAsync();
        using var c = vendedorId is null ? f.Client("Vendedor") : f.Client("Vendedor", vendedorId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.GetAsync("/api/ordenes/mis-ordenes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.GetAsync($"/api/ordenes/mis-ordenes/{f.OrderA1}")).StatusCode);
    }

    [Fact]
    public async Task ClaimsDuplicadosOContradictorios_NoSeAceptan()
    {
        using var f = await Fixture.CreateAsync();
        using var duplicate = f.Client("Vendedor", f.SellerA.ToString(), f.SellerA.ToString());
        using var contradictory = f.Client("Vendedor", f.SellerA.ToString(), f.SellerB.ToString());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await duplicate.GetAsync("/api/ordenes/mis-ordenes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await contradictory.GetAsync("/api/ordenes/mis-ordenes")).StatusCode);
    }

    [Theory]
    [InlineData("?pagina=0")]
    [InlineData("?tamano=0")]
    [InlineData("?tamano=101")]
    [InlineData("?pagina=2147483647&tamano=100")]
    [InlineData("?estado=NoExiste")]
    [InlineData("?estado=999")]
    public async Task FiltrosYPaginacionInvalidos_Devuelven400(string query)
    {
        using var f = await Fixture.CreateAsync();
        using var a = f.Client("Vendedor", f.SellerA.ToString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await a.GetAsync("/api/ordenes/mis-ordenes" + query)).StatusCode);
    }

    [Fact]
    public async Task FiltroEstadoYPaginacion_ConservanAislamientoYConteo()
    {
        using var f = await Fixture.CreateAsync();
        using var a = f.Client("Vendedor", f.SellerA.ToString());
        var filtered = await a.GetFromJsonAsync<JsonElement>(
            "/api/ordenes/mis-ordenes?estado=Pendiente&pagina=1&tamano=1");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal("F103-A-ONE",
            filtered.GetProperty("ordenes")[0].GetProperty("numeroOrden").GetString());
        var secondPage = await a.GetFromJsonAsync<JsonElement>(
            "/api/ordenes/mis-ordenes?pagina=2&tamano=1");
        Assert.Equal(2, secondPage.GetProperty("total").GetInt32());
        Assert.Equal("F103-A-TWO",
            secondPage.GetProperty("ordenes")[0].GetProperty("numeroOrden").GetString());
    }
}
