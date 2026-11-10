using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class ProductosPaginacionTests
{
    private static ProductosController Controller(CatalogDbContext db, string role, Guid? vendedorId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, role)
        };
        if (vendedorId.HasValue) claims.Add(new Claim("VendedorId", vendedorId.Value.ToString()));
        return new ProductosController(db, new Mock<ICatalogImportService>().Object,
            NullLogger<ProductosController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test-auth"))
                }
            }
        };
    }

    private static CatalogDbContext Db() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("pagination-" + Guid.NewGuid()).Options);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, -2)]
    [InlineData(1, 501)]
    [InlineData(int.MaxValue, 2)]
    [InlineData(21474838, 100)]
    public async Task PaginacionInvalida_Devuelve400EnAmbosEndpoints(int pagina, int tamano)
    {
        await using var db = Db();
        foreach (var (role, vendedor) in new[]
        {
            ("Comprador", (Guid?)null),
            ("Vendedor", (Guid?)Guid.NewGuid()),
            ("Admin", (Guid?)null)
        })
        {
            var controller = Controller(db, role, vendedor);
            Assert.IsType<BadRequestObjectResult>(await controller.GetPublicados(pagina, tamano));
            Assert.IsType<BadRequestObjectResult>(
                await controller.Buscar(null, null, null, null, null, null, pagina, tamano));
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(1, 500)]
    [InlineData(int.MaxValue, 1)]
    public async Task PaginacionValida_PermiteLimitesSinError(int pagina, int tamano)
    {
        await using var db = Db();
        var controller = Controller(db, "Admin");
        Assert.IsType<OkObjectResult>(await controller.GetPublicados(pagina, tamano));
        Assert.IsType<OkObjectResult>(
            await controller.Buscar(null, null, null, null, null, null, pagina, tamano));
    }

    private static Producto NuevoProducto(Guid vendedorId, string sku) => new()
    {
        Id = Guid.NewGuid(),
        VendedorId = vendedorId,
        Sku = sku,
        Nombre = "Producto " + sku,
        Descripcion = "Descripción " + sku,
        CategoriaNombre = "Tecnología",
        Subcategoria = "Pruebas",
        PrecioB2C = 20m,
        PrecioB2B = 18m,
        StockMinimo = 1,
        ProveedorNombre = "Proveedor de pruebas",
        PaisOrigen = "El Salvador",
        PesoKg = 1m,
        DimensionesCm = "10x10x10",
        UnidadMedida = "Unidad",
        EstadoProducto = "Activo",
        EstaPublicado = true,
        CategoriaId = Guid.NewGuid(),
        FechaCreacion = DateTime.UtcNow
    };

    [Fact]
    public async Task PaginacionDeVendedor_SoloDevuelveSuCatalogo()
    {
        await using var db = Db();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        db.Productos.AddRange(
            NuevoProducto(a, "OWN-PAGE-01"),
            NuevoProducto(b, "OTHER-PAGE-01"));
        await db.SaveChangesAsync();

        var controller = Controller(db, "Vendedor", a);
        foreach (var result in new IActionResult[]
        {
            await controller.GetPublicados(1, 1),
            await controller.Buscar(null, null, null, null, null, null, 1, 1)
        })
        {
            var response = Assert.IsType<OkObjectResult>(result);
            var json = System.Text.Json.JsonSerializer.Serialize(response.Value);
            Assert.Contains("OWN-PAGE-01", json);
            Assert.DoesNotContain("OTHER-PAGE-01", json);
        }
    }
}
