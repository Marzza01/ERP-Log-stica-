using System.Security.Claims;
using System.Text.Json;
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

public class ProductosB2BPaginacionTests
{
    private static CatalogDbContext Db() => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseInMemoryDatabase("b2b-pagination-" + Guid.NewGuid()).Options);

    private static ProductosController Controller(CatalogDbContext db, string role, Guid? vendedorId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new(ClaimTypes.Role, role) };
        if (vendedorId.HasValue) claims.Add(new Claim("VendedorId", vendedorId.Value.ToString()));
        return new ProductosController(db, new Mock<ICatalogImportService>().Object, NullLogger<ProductosController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test-auth")) }
            }
        };
    }

    private static Producto Product(Guid owner, string sku) => new()
    {
        Id = Guid.NewGuid(), VendedorId = owner, Sku = sku, Nombre = sku,
        Descripcion = "Producto de prueba", CategoriaNombre = "Tecnología", Subcategoria = "Prueba",
        PrecioB2C = 20m, PrecioB2B = 10m, StockMinimo = 1, ProveedorNombre = "Proveedor",
        PaisOrigen = "El Salvador", PesoKg = 1, DimensionesCm = "1x1x1", UnidadMedida = "Unidad",
        EstadoProducto = "Activo", EstaPublicado = true,
        CategoriaId = Guid.NewGuid(), FechaCreacion = DateTime.UtcNow
    };

    [Fact]
    public async Task Vendedor_PaginasB2B_ExcluyenProductosAjenosYSonDeterministas()
    {
        await using var db = Db();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        db.Productos.AddRange(Product(a, "A-OWN"), Product(a, "B-OWN"), Product(b, "C-OTHER"));
        await db.SaveChangesAsync();
        var seller = Controller(db, "Vendedor", a);
        var p1 = Assert.IsType<OkObjectResult>(await seller.GetCatalogoB2B(null, 1, 1));
        var p2 = Assert.IsType<OkObjectResult>(await seller.GetCatalogoB2B(null, 2, 1));
        var p3 = Assert.IsType<OkObjectResult>(await seller.GetCatalogoB2B(null, 3, 1));
        var j1 = JsonSerializer.Serialize(p1.Value);
        var j2 = JsonSerializer.Serialize(p2.Value);
        Assert.StartsWith("[", j1); // Conservar respuesta JSON array, no introducir wrapper.
        Assert.Contains("A-OWN", j1); Assert.DoesNotContain("B-OWN", j1);
        Assert.Contains("B-OWN", j2); Assert.DoesNotContain("A-OWN", j2);
        Assert.DoesNotContain("C-OTHER", j1 + j2);
        Assert.Equal("[]", JsonSerializer.Serialize(p3.Value));

        var admin = Controller(db, "Admin");
        var global = Assert.IsType<OkObjectResult>(await admin.GetCatalogoB2B(null, 1, 3));
        Assert.Contains("C-OTHER", JsonSerializer.Serialize(global.Value));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 501)]
    [InlineData(int.MaxValue, 500)]
    public async Task B2B_RechazaParametrosInvalidos(int pagina, int tamano)
    {
        await using var db = Db();
        var admin = Controller(db, "Admin");
        Assert.IsType<BadRequestObjectResult>(await admin.GetCatalogoB2B(null, pagina, tamano));
    }

    [Fact]
    public async Task B2B_DefaultConservaArrayYCapacidadDe500Elementos()
    {
        await using var db = Db();
        var controller = Controller(db, "Admin");
        var response = Assert.IsType<OkObjectResult>(await controller.GetCatalogoB2B(null));
        Assert.Equal("[]", JsonSerializer.Serialize(response.Value));
        Assert.IsType<OkObjectResult>(await controller.GetCatalogoB2B(null, 1, 500));
    }
}
