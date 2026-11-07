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

public class ReintentosInboundComprasTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reintento_MismoAcumulado_RequiereConfirmacionLedger(bool operacionConfirmada)
    {
        var unico = Guid.NewGuid().ToString("N");
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("replay-fin-" + unico).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("replay-core-" + unico).Options;

        var poId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var ubicacionId = Guid.NewGuid();
        var operacionId = Guid.NewGuid();
        await using var finance = new FinanceDbContext(financeOptions);
        await using var core = new CoreDbContext(coreOptions);
        finance.OrdenesCompra.Add(new OrdenCompra
        {
            Id = poId,
            NumeroPO = "PO-REPLAY-001",
            ProveedorId = Guid.NewGuid(),
            ProveedorNombre = "Proveedor de prueba",
            Estado = EstadoOrdenCompra.RecibidaCompleta,
            Items =
            {
                new ItemOrdenCompra
                {
                    Id = itemId,
                    OrdenCompraId = poId,
                    Sku = "ALM-0001",
                    NombreProducto = "Arroz",
                    CantidadSolicitada = 24,
                    CantidadRecibida = 24,
                    PrecioUnitarioAcordado = 3.55m
                }
            }
        });
        await finance.SaveChangesAsync();

        var inventario = new Mock<IInventoryService>(MockBehavior.Strict);
        inventario.Setup(i => i.ConfirmarInboundProcesadoAsync(
                "ALM-0001", 24, ubicacionId, poId, itemId, operacionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(operacionConfirmada);

        var controller = new ComprasController(finance,
            Mock.Of<ITresWayMatchingService>(),
            Mock.Of<IContabilidadService>(),
            inventario.Object,
            core,
            NullLogger<ComprasController>.Instance);

        var respuesta = await controller.RecibirMercaderia(poId,
            new RecibirMercaderiaRequest(new List<RecibirMercaderiaItemRequest>
            {
                new(itemId, 24, 24, ubicacionId, operacionId)
            }, "Reintento"), CancellationToken.None);

        if (operacionConfirmada)
            Assert.IsType<OkObjectResult>(respuesta);
        else
            Assert.IsType<ConflictObjectResult>(respuesta);

        inventario.Verify(i => i.RecibirInboundAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Guid>(),
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var comprobacion = new FinanceDbContext(financeOptions);
        var guardado = await comprobacion.ItemsOrdenCompra.SingleAsync();
        Assert.Equal(24, guardado.CantidadRecibida);
    }
}
