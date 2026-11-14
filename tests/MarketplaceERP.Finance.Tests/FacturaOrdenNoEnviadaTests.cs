using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class FacturaOrdenNoEnviadaTests
{
    [Theory]
    [InlineData(EstadoOrdenCompra.Borrador)]
    [InlineData(EstadoOrdenCompra.Cancelada)]
    [InlineData(EstadoOrdenCompra.Pagada)]
    public async Task RegistrarFactura_RechazaEstadoNoFacturable_SinPersistirFactura(
        EstadoOrdenCompra estado)
    {
        var unico = Guid.NewGuid().ToString("N");
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("factura-estado-" + unico).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("factura-core-" + unico).Options;

        await using var finance = new FinanceDbContext(financeOptions);
        await using var core = new CoreDbContext(coreOptions);
        var id = Guid.NewGuid();
        finance.OrdenesCompra.Add(new OrdenCompra
        {
            Id = id,
            NumeroPO = "PO-ESTADO-" + unico,
            ProveedorId = Guid.NewGuid(),
            ProveedorNombre = "Proveedor de evaluación",
            Estado = estado
        });
        await finance.SaveChangesAsync();

        var controller = new ComprasController(
            finance, Mock.Of<ITresWayMatchingService>(),
            Mock.Of<IContabilidadService>(), Mock.Of<IInventoryService>(),
            core, NullLogger<ComprasController>.Instance);

        var solicitud = new RegistrarFacturaRequest(
            id, "F-TEST-001", 100m, 113m, null, null, null);
        var respuesta = await controller.RegistrarFactura(solicitud, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(respuesta);
        Assert.Empty(await finance.FacturasProveedor.ToListAsync());
    }
}
