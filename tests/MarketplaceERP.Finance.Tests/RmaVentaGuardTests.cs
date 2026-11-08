using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class RmaVentaGuardTests
{
    private static FinanceDbContext Finance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("rma-f-" + Guid.NewGuid()).Options);

    private static CatalogDbContext Catalog() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("rma-c-" + Guid.NewGuid()).Options);

    private static RmaController Controller(
        Mock<IRmaService> service, FinanceDbContext finance, CatalogDbContext catalog) =>
        new(service.Object, NullLogger<RmaController>.Instance, finance, catalog);

    private static Orden Order(Guid productId, int quantity, EstadoOrden state = EstadoOrden.Entregada)
    {
        var id = Guid.NewGuid();
        return new Orden
        {
            Id = id, NumeroOrden = "ORD-" + id.ToString("N"),
            CompradorId = Guid.NewGuid(), VendedorId = Guid.NewGuid(), Estado = state,
            Items = new List<ItemOrden>
            {
                new() { Id = Guid.NewGuid(), ProductoId = productId, Cantidad = quantity,
                    NombreProducto = "Synthetic", SkuReferencia = "TEST", PrecioUnitario = 2m }
            }
        };
    }

    [Fact]
    public async Task SolicitudRma_DeOrdenInexistente_NoCreaRma()
    {
        await using var finance = Finance();
        await using var catalog = Catalog();
        var service = new Mock<IRmaService>(MockBehavior.Strict);
        var req = new RegistrarRmaRequest(Guid.NewGuid(), Guid.NewGuid(), 1, "Test");

        var response = await Controller(service, finance, catalog).SolicitarDevolucion(req);

        Assert.IsType<BadRequestObjectResult>(response);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(EstadoOrden.Cancelada)]
    [InlineData(EstadoOrden.Pendiente)]
    public async Task SolicitudRma_DeOrdenNoEntregada_NoCreaRma(EstadoOrden state)
    {
        await using var finance = Finance();
        await using var catalog = Catalog();
        var productId = Guid.NewGuid();
        var order = Order(productId, 2, state);
        finance.Ordenes.Add(order);
        await finance.SaveChangesAsync();
        var service = new Mock<IRmaService>(MockBehavior.Strict);

        var response = await Controller(service, finance, catalog)
            .SolicitarDevolucion(new RegistrarRmaRequest(order.Id, productId, 1, "Test"));

        Assert.IsType<BadRequestObjectResult>(response);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SolicitudRma_DeProductoNoVendido_NoCreaRma()
    {
        await using var finance = Finance();
        await using var catalog = Catalog();
        var order = Order(Guid.NewGuid(), 2);
        finance.Ordenes.Add(order);
        await finance.SaveChangesAsync();
        var service = new Mock<IRmaService>(MockBehavior.Strict);

        var response = await Controller(service, finance, catalog).SolicitarDevolucion(
            new RegistrarRmaRequest(order.Id, Guid.NewGuid(), 1, "Test"));

        Assert.IsType<BadRequestObjectResult>(response);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SolicitudRma_RechazaCantidadAcumuladaMayorALaVendida()
    {
        await using var finance = Finance();
        await using var catalog = Catalog();
        var productId = Guid.NewGuid();
        var order = Order(productId, 3);
        finance.Ordenes.Add(order);
        catalog.DevolucionesRMA.Add(new DevolucionRMA
        {
            Id = Guid.NewGuid(), NumeroRMA = "RMA-TEST-EXISTING",
            OrdenId = order.Id, ProductoId = productId,
            Cantidad = 2, MotivoCliente = "Existing", Estado = EstadoRMA.Solicitado
        });
        await finance.SaveChangesAsync();
        await catalog.SaveChangesAsync();
        var service = new Mock<IRmaService>(MockBehavior.Strict);

        var response = await Controller(service, finance, catalog).SolicitarDevolucion(
            new RegistrarRmaRequest(order.Id, productId, 2, "Test"));

        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(1, await catalog.DevolucionesRMA.CountAsync());
        service.VerifyNoOtherCalls();
    }
}
