using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class ComprasSkuDuplicadoTests
{
    [Theory]
    [InlineData("ALM-0001", "ALM-0001")]
    [InlineData("ALM-0001", "alm-0001")]
    [InlineData("ALM-0001", " ALM-0001 ")]
    public async Task CrearPO_RechazaSkuDuplicado_SinGuardarLaOrden(
        string primerSku, string segundoSku)
    {
        var id = Guid.NewGuid().ToString("N");
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("finance-po-duplicada-" + id).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("core-po-duplicada-" + id).Options;
        await using var finance = new FinanceDbContext(financeOptions);
        await using var core = new CoreDbContext(coreOptions);
        var controller = new ComprasController(
            finance,
            Mock.Of<ITresWayMatchingService>(),
            Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(),
            core,
            NullLogger<ComprasController>.Instance);

        var req = new CrearPORequest(
            Guid.NewGuid(), "Proveedor Demo", null, "Prueba de SKU duplicado",
            null, false,
            new List<ItemPORequest>
            {
                new(primerSku, "Producto de prueba", 2, 10.00m),
                new(segundoSku, "Producto de prueba", 3, 10.00m)
            });

        var result = await controller.CrearPO(req, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await finance.OrdenesCompra.ToListAsync());
    }
}
