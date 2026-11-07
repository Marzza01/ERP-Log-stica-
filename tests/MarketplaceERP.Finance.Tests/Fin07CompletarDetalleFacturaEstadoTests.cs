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

public class Fin07CompletarDetalleFacturaEstadoTests
{
    [Theory]
    [InlineData(EstadoOrdenCompra.Borrador, false)]
    [InlineData(EstadoOrdenCompra.Cancelada, false)]
    [InlineData(EstadoOrdenCompra.Pagada, false)]
    [InlineData(EstadoOrdenCompra.Enviada, true)]
    public async Task CompletarDetalle_RechazaEstadoInvalidoOProveedorDistinto(
        EstadoOrdenCompra estado, bool proveedorDistinto)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("fin07-detalle-" + suffix).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("fin07-core-" + suffix).Options;

        var proveedorPo = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        await using var finance = new FinanceDbContext(financeOptions);
        await using var core = new CoreDbContext(coreOptions);
        finance.OrdenesCompra.Add(new OrdenCompra
        {
            Id = poId, NumeroPO = "PO-DET-" + suffix,
            ProveedorId = proveedorPo, ProveedorNombre = "Proveedor de prueba",
            Estado = estado,
            Items = { new ItemOrdenCompra {
                Id = itemId, OrdenCompraId = poId, Sku = "SKU-TEST",
                NombreProducto = "Artículo de prueba",
                CantidadSolicitada = 10, PrecioUnitarioAcordado = 10m } }
        });
        finance.FacturasProveedor.Add(new FacturaProveedor
        {
            Id = facturaId, OrdenCompraId = poId,
            ProveedorId = proveedorDistinto ? Guid.NewGuid() : proveedorPo,
            NumeroFactura = "FAC-DET-" + suffix,
            MontoSubTotal = 100m, MontoIva = 13m, MontoTotal = 113m
        });
        await finance.SaveChangesAsync();

        var controller = new ComprasController(finance,
            Mock.Of<ITresWayMatchingService>(), Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(), core, NullLogger<ComprasController>.Instance);
        var response = await controller.CompletarDetalleFactura(facturaId,
            new List<LineaFacturaProveedorRequest> { new(itemId, 10, 10m) },
            CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(response);

        await using var verification = new FinanceDbContext(financeOptions);
        Assert.Empty(await verification.ItemsFacturaProveedor.ToListAsync());
    }

    [Fact]
    public async Task CompletarDetalle_AceptaPoEnviadaYProveedorCorrecto()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("fin07-valido-" + suffix).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("fin07-core-valido-" + suffix).Options;
        var proveedorId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        await using var finance = new FinanceDbContext(financeOptions);
        await using var core = new CoreDbContext(coreOptions);
        finance.OrdenesCompra.Add(new OrdenCompra
        {
            Id = poId, NumeroPO = "PO-VALIDA-" + suffix,
            ProveedorId = proveedorId, ProveedorNombre = "Proveedor válido",
            Estado = EstadoOrdenCompra.Enviada,
            Items = { new ItemOrdenCompra {
                Id = itemId, OrdenCompraId = poId, Sku = "SKU-VALIDO",
                NombreProducto = "Artículo válido",
                CantidadSolicitada = 10, PrecioUnitarioAcordado = 10m } }
        });
        finance.FacturasProveedor.Add(new FacturaProveedor
        {
            Id = facturaId, OrdenCompraId = poId, ProveedorId = proveedorId,
            NumeroFactura = "FAC-VALIDA-" + suffix,
            MontoSubTotal = 100m, MontoIva = 13m, MontoTotal = 113m
        });
        await finance.SaveChangesAsync();

        var controller = new ComprasController(finance,
            Mock.Of<ITresWayMatchingService>(), Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(), core, NullLogger<ComprasController>.Instance);
        var response = await controller.CompletarDetalleFactura(facturaId,
            new List<LineaFacturaProveedorRequest> { new(itemId, 10, 10m) },
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(response);

        await using var verification = new FinanceDbContext(financeOptions);
        Assert.Single(await verification.ItemsFacturaProveedor.ToListAsync());
    }
}
