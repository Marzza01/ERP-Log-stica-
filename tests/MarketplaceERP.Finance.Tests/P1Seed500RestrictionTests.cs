using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class P1Seed500RestrictionTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Seed500_NoEjecutaImportacionFueraDeDevelopment(string environmentName)
    {
        await using var catalog = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase("p1-seed-" + Guid.NewGuid().ToString("N")).Options);
        var importer = new Mock<ICatalogImportService>(MockBehavior.Strict);
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(environmentName);

        var controller = new ProductosController(
            catalog, importer.Object, NullLogger<ProductosController>.Instance);

        var result = await controller.Seed500(environment.Object);

        Assert.IsType<NotFoundObjectResult>(result);
        importer.VerifyNoOtherCalls();
        var action = typeof(ProductosController).GetMethod(nameof(ProductosController.Seed500))!;
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), a => a.Roles == "Admin");
    }
}
