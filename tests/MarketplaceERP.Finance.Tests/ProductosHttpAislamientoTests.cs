using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
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
/// HTTP real: TestServer ejecuta enrutamiento, JWT, autorización y serialización
/// del controlador de producción. Ninguna conexión de Neon/Render ni servidor de red.
/// </summary>
public class ProductosHttpAislamientoTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TestServer _server;
        private readonly JwtSettings _jwt;
        public Guid SellerA { get; } = Guid.NewGuid();
        public Guid SellerB { get; } = Guid.NewGuid();
        public Guid ProductA { get; } = Guid.NewGuid();
        public Guid ProductB { get; } = Guid.NewGuid();
        public Guid DraftB { get; } = Guid.NewGuid();

        private Fixture(TestServer server, JwtSettings jwt)
        {
            _server = server;
            _jwt = jwt;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var database = "http-tenant-" + Guid.NewGuid().ToString("N");
            var jwt = new JwtSettings
            {
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                Issuer = "http-tenant-tests.invalid",
                Audience = "http-test-client.invalid",
                ExpirationMinutes = 20
            };

            var server = new TestServer(new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddDbContext<CatalogDbContext>(options =>
                        options.UseInMemoryDatabase(database));
                    services.AddSingleton(Mock.Of<ICatalogImportService>());
                    services.AddMarketplaceSecurity(jwt);
                    services.AddControllers().AddApplicationPart(typeof(ProductosController).Assembly);
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
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            db.Productos.AddRange(
                fixture.Product(fixture.ProductA, fixture.SellerA, "HTTP-OWN-A", "Laptop propia", true, null),
                fixture.Product(fixture.ProductB, fixture.SellerB, "HTTP-FOREIGN-B", "Laptop ajena", true, "7891234567895"),
                fixture.Product(fixture.DraftB, fixture.SellerB, "HTTP-DRAFT-B", "Laptop borrador", false, null));
            await db.SaveChangesAsync();
            return fixture;
        }

        private Producto Product(Guid id, Guid seller, string sku, string name, bool published, string? ean) =>
            new()
            {
                Id = id,
                VendedorId = seller,
                CategoriaId = Guid.NewGuid(),
                Sku = sku,
                Nombre = name,
                Descripcion = "Descripción " + name,
                CategoriaNombre = "Tecnología",
                Subcategoria = "Laptops",
                PrecioB2C = 900m,
                PrecioB2B = 444m,
                CodigoEAN = ean,
                ProveedorNombre = "PROVEEDOR-PRIVADO-B",
                EstadoProducto = "Activo",
                EstaPublicado = published
            };

        private string Token(string role, string? vendedorId)
        {
            var id = Guid.NewGuid().ToString();
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, id),
                new(ClaimTypes.NameIdentifier, id),
                new(ClaimTypes.Role, role)
            };
            if (vendedorId is not null)
                claims.Add(new Claim("VendedorId", vendedorId));

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)),
                SecurityAlgorithms.HmacSha256);

            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: _jwt.Issuer, audience: _jwt.Audience, claims: claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: credentials));
        }

        public HttpClient Client(string? role = null, string? vendedorId = null)
        {
            var client = _server.CreateClient();
            if (role is not null)
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", Token(role, vendedorId));
            return client;
        }

        public async Task VerificarSinStockInyectadoAsync(string sku, Guid stockId)
        {
            using var scope = _server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var producto = await db.Productos.Include(p => p.Stocks)
                .SingleAsync(p => p.Sku == sku);
            Assert.Equal(SellerA, producto.VendedorId);
            Assert.Empty(producto.Stocks);
            Assert.False(await db.StockFisico.AnyAsync(s => s.Id == stockId));
        }

        public async Task VerificarRechazoSinCambiosAsync(string sku, Guid stockId)
        {
            using var scope = _server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.False(await db.Productos.AnyAsync(p => p.Sku == sku));
            Assert.False(await db.StockFisico.AnyAsync(s => s.Id == stockId));
        }

        public void Dispose() => _server.Dispose();
    }

    [Fact]
    public async Task Http_VendedorA_NoLeeCatalogoNiBorradoresDeVendedorB()
    {
        using var fixture = await Fixture.CreateAsync();
        using var sellerA = fixture.Client("Vendedor", fixture.SellerA.ToString());

        var list = await sellerA.GetAsync("/api/productos?pagina=1&tamano=20");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listJson = await list.Content.ReadAsStringAsync();
        Assert.Contains("HTTP-OWN-A", listJson);
        Assert.DoesNotContain("HTTP-FOREIGN-B", listJson);
        Assert.DoesNotContain("HTTP-DRAFT-B", listJson);

        var search = await sellerA.GetAsync("/api/productos/buscar?q=Laptop");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        var searchJson = await search.Content.ReadAsStringAsync();
        Assert.Contains("HTTP-OWN-A", searchJson);
        Assert.DoesNotContain("HTTP-FOREIGN-B", searchJson);
        Assert.DoesNotContain("HTTP-DRAFT-B", searchJson);

        var b2b = await sellerA.GetAsync("/api/productos/b2b");
        Assert.Equal(HttpStatusCode.OK, b2b.StatusCode);
        var b2bJson = await b2b.Content.ReadAsStringAsync();
        Assert.Contains("HTTP-OWN-A", b2bJson);
        Assert.DoesNotContain("HTTP-FOREIGN-B", b2bJson);

        Assert.Equal(HttpStatusCode.OK,
            (await sellerA.GetAsync($"/api/productos/{fixture.ProductA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sellerA.GetAsync($"/api/productos/{fixture.ProductB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sellerA.GetAsync($"/api/productos/{fixture.DraftB}")).StatusCode);
    }

    [Fact]
    public async Task Http_CompradorSoloVePublicadosSinDatosPrivados_YAdminConservaAcceso()
    {
        using var fixture = await Fixture.CreateAsync();
        using var buyer = fixture.Client("Comprador");

        var list = await buyer.GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Contains("HTTP-OWN-A", listBody);
        Assert.Contains("HTTP-FOREIGN-B", listBody);
        Assert.DoesNotContain("HTTP-DRAFT-B", listBody);

        var detail = await buyer.GetAsync($"/api/productos/{fixture.ProductB}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var buyerBody = await detail.Content.ReadAsStringAsync();
        foreach (var body in new[] { listBody, buyerBody })
        {
            Assert.DoesNotContain("PROVEEDOR-PRIVADO-B", body);
            Assert.DoesNotContain("precioB2B", body);
            Assert.DoesNotContain("PrecioB2B", body);
            Assert.DoesNotContain("vendedorId", body);
            Assert.DoesNotContain("VendedorId", body);
        }

        Assert.Equal(HttpStatusCode.NotFound,
            (await buyer.GetAsync($"/api/productos/{fixture.DraftB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await buyer.GetAsync("/api/productos/b2b")).StatusCode);

        using var admin = fixture.Client("Admin");
        var adminList = await admin.GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);
        Assert.Contains("HTTP-FOREIGN-B", await adminList.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/productos/{fixture.DraftB}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/productos/b2b")).StatusCode);
    }

    [Fact]
    public async Task Http_SinClaimValido_DeNiegaLecturasYNoPermiteSaltarseJwt()
    {
        using var fixture = await Fixture.CreateAsync();
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/productos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/productos/{fixture.ProductA}")).StatusCode);

        using var noSeller = fixture.Client("Vendedor");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noSeller.GetAsync("/api/productos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noSeller.GetAsync("/api/productos/b2b")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noSeller.GetAsync($"/api/productos/{fixture.ProductA}")).StatusCode);

        using var malformedSeller = fixture.Client("Vendedor", "invalid-guid");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await malformedSeller.GetAsync("/api/productos/buscar?q=Laptop")).StatusCode);

        using var tampered = fixture.Client();
        tampered.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "a.b.c");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await tampered.GetAsync("/api/productos")).StatusCode);
    }

    [Fact]
    public async Task Http_EanNulo_EnBusquedaNoProvoca500NiFiltraCoincidenciasAjenas()
    {
        using var fixture = await Fixture.CreateAsync();
        using var sellerA = fixture.Client("Vendedor", fixture.SellerA.ToString());

        var result = await sellerA.GetAsync("/api/productos/buscar?q=7891234567895");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var body = await result.Content.ReadAsStringAsync();
        Assert.DoesNotContain("HTTP-FOREIGN-B", body);
        Assert.DoesNotContain("PROVEEDOR-PRIVADO-B", body);
    }
    [Fact]
    public async Task Http_CrearProducto_RechazaGrafoMaliciosoYPreservaAltaLegitima()
    {
        using var fixture = await Fixture.CreateAsync();
        using var seller = fixture.Client("Vendedor", fixture.SellerA.ToString());
        var stockId = Guid.NewGuid();
        const string sku = "HTTP-NO-OVERPOST";

        // Stock es una propiedad de compatibilidad con setter público.
        // Un grafo de inventario enviado desde HTTP debe rechazarse o
        // descartarse sin ninguna escritura; aquí el model binder lo rechaza.
        var maliciosa = new
        {
            sku,
            nombre = "Producto con intento de stock",
            descripcion = "Ficha del producto",
            categoriaNombre = "Tecnología",
            subcategoria = "Laptops",
            categoriaId = Guid.NewGuid(),
            vendedorId = fixture.SellerA,
            precioB2C = 50m,
            precioB2B = 40m,
            proveedorNombre = "Proveedor de prueba",
            paisOrigen = "El Salvador",
            dimensionesCm = "10x10x10",
            unidadMedida = "Unidad",
            estadoProducto = "Activo",
            estaPublicado = true,
            stock = new
            {
                id = stockId,
                productoId = fixture.ProductB,
                cantidadTotal = 9999,
                cantidadReservada = 0,
                cantidadBloqueada = 0,
                ubicacionCodigo = "STOCK-FAKE-CLIENT"
            }
        };
        var invalidResponse = await seller.PostAsJsonAsync("/api/productos", maliciosa);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        await fixture.VerificarRechazoSinCambiosAsync(sku, stockId);

        // Un producto maestro válido sí se puede crear, sin alta WMS automática.
        var legitima = new
        {
            sku,
            nombre = "Producto nuevo sin inventario automático",
            descripcion = "Ficha del producto",
            categoriaNombre = "Tecnología",
            subcategoria = "Laptops",
            categoriaId = Guid.NewGuid(),
            vendedorId = fixture.SellerA,
            precioB2C = 50m,
            precioB2B = 40m,
            proveedorNombre = "Proveedor de prueba",
            paisOrigen = "El Salvador",
            dimensionesCm = "10x10x10",
            unidadMedida = "Unidad",
            estadoProducto = "Activo",
            estaPublicado = true
        };
        var response = await seller.PostAsJsonAsync("/api/productos", legitima);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await fixture.VerificarSinStockInyectadoAsync(sku, stockId);

        var location = response.Headers.Location?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(location));

        using var otherSeller = fixture.Client("Vendedor", fixture.SellerB.ToString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherSeller.GetAsync(location)).StatusCode);
    }

}
