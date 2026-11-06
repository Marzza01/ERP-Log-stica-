using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class StagingCatalogEanTests
{
    [Fact]
    public async Task PublishedProductWithoutEan_CanBeListedAndSearched()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"staging-ean-{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);

        var ean = db.Model.FindEntityType(typeof(Producto))!
            .FindProperty(nameof(Producto.CodigoEAN));
        Assert.NotNull(ean);
        Assert.True(ean.IsNullable);

        db.Productos.Add(new Producto
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-NULL",
            Nombre = "Producto sin EAN",
            EstaPublicado = true,
            CodigoEAN = null
        });
        await db.SaveChangesAsync();

        var controller = new ProductosController(db, Mock.Of<ICatalogImportService>(),
            NullLogger<ProductosController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Role, "Admin") }, "staging-test"))
                }
            }
        };

        var listed = Assert.IsType<OkObjectResult>(await controller.GetPublicados());
        Assert.Contains("\"CodigoEAN\":null", JsonSerializer.Serialize(listed.Value));

        var searched = Assert.IsType<OkObjectResult>(await controller.Buscar(
            "SKU-NULL", null, null, null, null, null));
        Assert.Contains("SKU-NULL", JsonSerializer.Serialize(searched.Value));
    }
}
