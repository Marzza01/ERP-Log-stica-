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

    [Fact]
    public async Task Empacar_ReintentoConDatosDistintos_Devuelve409SinCambiarGuiaFinance()
    {
        await using var db = Context();
        var order = NewOrder(EstadoOrden.EnProceso);
        db.Ordenes.Add(order);
        await db.SaveChangesAsync();

        var wms = new Mock<IFulfillmentService>(MockBehavior.Strict);
        wms.Setup(s => s.EmpacarYRotularAsync(
                It.IsAny<EmpacarDespachoRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Ya existe un paquete para la orden con datos de empaque distintos."));
        var controller = new FulfillmentController(wms.Object, db);
        var body = new EmpacarDespachoRequest(
            order.Id, order.NumeroOrden, 1m, 10m, 10m, 10m,
            "Transportista", "San Salvador", "Centro",
            "Direccion diferente", "Contacto", "7000-0000");

        var result = await controller.EmpacarYRotular(
            order.Id, body, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        var saved = await db.Ordenes.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoOrden.EnProceso, saved.Estado);
        Assert.True(string.IsNullOrWhiteSpace(saved.GuiaEnvio));
        wms.Verify(s => s.EmpacarYRotularAsync(
            It.IsAny<EmpacarDespachoRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        wms.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfirmarPicking_Completada_Retorna409SinOcultarError()
    {
        await using var db = Context();
        var wms = new Mock<IFulfillmentService>(MockBehavior.Strict);
        var pickId = Guid.NewGuid();
        wms.Setup(s => s.ConfirmarPickingAsync(
                pickId, It.IsAny<List<ConfirmarItemPickingRequest>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Una PickList completada no admite modificaciones."));
        var controller = new FulfillmentController(wms.Object, db);
        var result = await controller.ConfirmarPicking(
            pickId, new List<ConfirmarItemPickingRequest>(), CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("PICKING_COMPLETADO", conflict.Value?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);
        wms.Verify(s => s.ConfirmarPickingAsync(
            pickId, It.IsAny<List<ConfirmarItemPickingRequest>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmarPicking_CantidadInvalida_Retorna400()
    {
        await using var db = Context();
        var wms = new Mock<IFulfillmentService>(MockBehavior.Strict);
        var pickId = Guid.NewGuid();
        wms.Setup(s => s.ConfirmarPickingAsync(
                pickId, It.IsAny<List<ConfirmarItemPickingRequest>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Cantidad de picking inválida para el item."));
        var controller = new FulfillmentController(wms.Object, db);
        var result = await controller.ConfirmarPicking(
            pickId, new List<ConfirmarItemPickingRequest>(), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
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
