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

public class RecepcionParcialComprasTests
{
    [Fact]
    public async Task RecibirMercaderia_SegundaLineaFalla_PrimeraQuedaPersistida()
    {
        var id = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("recepcion-parcial-" + id).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("core-recepcion-parcial-" + id).Options;

        var poId = Guid.NewGuid();
        var primeroId = Guid.NewGuid();
        var segundoId = Guid.NewGuid();
        await using (var seed = new FinanceDbContext(options))
        {
            seed.OrdenesCompra.Add(new OrdenCompra
            {
                Id = poId,
                NumeroPO = "PO-TEST-REC-001",
                ProveedorId = Guid.NewGuid(),
                ProveedorNombre = "Proveedor de prueba",
                Estado = EstadoOrdenCompra.Enviada,
                Items =
                {
                    new ItemOrdenCompra { Id = primeroId, OrdenCompraId = poId,
                        Sku = "ALM-0001", NombreProducto = "Arroz",
                        CantidadSolicitada = 24, PrecioUnitarioAcordado = 3.55m },
                    new ItemOrdenCompra { Id = segundoId, OrdenCompraId = poId,
                        Sku = "ALM-0005", NombreProducto = "Café",
                        CantidadSolicitada = 12, PrecioUnitarioAcordado = 10.06m }
                }
            });
            await seed.SaveChangesAsync();
        }

        await using var finance = new FinanceDbContext(options);
        await using var core = new CoreDbContext(coreOptions);
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(i => i.RecibirInboundAsync(
            It.Is<string>(sku => sku == "ALM-0001"),
            It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecepcionInventarioResult(
                true, false, Guid.NewGuid(), Guid.NewGuid(), 24, "Recibido"));
        inventory.Setup(i => i.RecibirInboundAsync(
            It.Is<string>(sku => sku == "ALM-0005"),
            It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecepcionInventarioResult(
                false, false, Guid.Empty, Guid.Empty, 0, "Capacidad insuficiente"));

        var controller = new ComprasController(finance,
            Mock.Of<ITresWayMatchingService>(), Mock.Of<IContabilidadService>(),
            inventory.Object, core, NullLogger<ComprasController>.Instance);

        var solicitud = new RecibirMercaderiaRequest(
            new List<RecibirMercaderiaItemRequest>
            {
                new(primeroId, 24, 24, Guid.NewGuid(), Guid.NewGuid()),
                new(segundoId, 12, 12, Guid.NewGuid(), Guid.NewGuid())
            }, "Prueba controlada");

        var respuesta = await controller.RecibirMercaderia(poId, solicitud, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(respuesta);

        // Comprobar con otro DbContext: no basta con observar entidades rastreadas.
        await using var verificacion = new FinanceDbContext(options);
        var guardada = await verificacion.OrdenesCompra.Include(p => p.Items).SingleAsync();
        Assert.Equal(EstadoOrdenCompra.RecibidaParcial, guardada.Estado);
        Assert.Equal(24, guardada.Items.Single(i => i.Id == primeroId).CantidadRecibida);
        Assert.Equal(0, guardada.Items.Single(i => i.Id == segundoId).CantidadRecibida);
        Assert.NotNull(guardada.FechaRecepcionReal);
    }
}
