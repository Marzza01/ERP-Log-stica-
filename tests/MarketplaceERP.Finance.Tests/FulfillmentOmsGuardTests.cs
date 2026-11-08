using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

// Regression tests: no external API, no payments, and no production database.
public class FulfillmentOmsGuardTests
{
    private static FinanceDbContext Context() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("fulfillment-guards-" + Guid.NewGuid()).Options);

    private static Orden NewOrder(EstadoOrden state = EstadoOrden.Aprobada) => new()
    {
        Id = Guid.NewGuid(),
        NumeroOrden = "ORD-" + Guid.NewGuid().ToString("N"),
        CompradorId = Guid.NewGuid(),
        VendedorId = Guid.NewGuid(),
        Estado = state
    };

    [Fact]
    public async Task Empacar_RejectsPathBodyOrderMismatch_WithoutWritingOrCallingWms()
    {
        await using var db = Context();
        var order = NewOrder(EstadoOrden.EnProceso);
        db.Ordenes.Add(order);
        await db.SaveChangesAsync();
        var wms = new Mock<IFulfillmentService>(MockBehavior.Strict);
        var controller = new FulfillmentController(wms.Object, db);
        var body = new EmpacarDespachoRequest(
            Guid.NewGuid(), order.NumeroOrden, 1m, 10m, 10m, 10m,
            "Transportista", "San Salvador", "Centro",
            "Direccion", "Contacto", "7000-0000");

        var result = await controller.EmpacarYRotular(order.Id, body, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(string.Empty, (await db.Ordenes.SingleAsync()).GuiaEnvio);
        wms.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(EstadoOrden.Cancelada)]
    [InlineData(EstadoOrden.Entregada)]
    [InlineData(EstadoOrden.Pendiente)]
    public async Task GenerarPickList_RejectsNonApprovedOrder_BeforeWmsMutation(EstadoOrden state)
    {
        await using var db = Context();
        var order = NewOrder(state);
        db.Ordenes.Add(order);
        await db.SaveChangesAsync();
        var wms = new Mock<IFulfillmentService>(MockBehavior.Strict);
        var controller = new FulfillmentController(wms.Object, db);

        var result = await controller.GenerarPickList(order.Id, "Bodega", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(state, (await db.Ordenes.SingleAsync()).Estado);
        wms.VerifyNoOtherCalls();
    }
}
