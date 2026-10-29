using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class PagoInciertoI2B2ATests
{
    private static CheckoutCommand Command() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 25m, "USD");

    private static FinanceDbContext Finance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I2B2A_{Guid.NewGuid():N}").Options);

    private static (CheckoutOrchestrator sut, Mock<IStockService> stock,
        Mock<IPaymentGateway> gateway, Mock<ICommissionService> commission)
        Preparar(FinanceDbContext db)
    {
        var stock = new Mock<IStockService>();
        var gateway = new Mock<IPaymentGateway>();
        var commission = new Mock<ICommissionService>();
        stock.Setup(x => x.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ReservaResult(Guid.NewGuid(), true, null));
        stock.Setup(x => x.LiberarStockAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        stock.Setup(x => x.ConfirmarReservaAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        commission.Setup(x => x.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()))
            .ReturnsAsync(2.5m);
        return (new CheckoutOrchestrator(stock.Object, commission.Object,
                gateway.Object, db, NullLogger<CheckoutOrchestrator>.Instance),
            stock, gateway, commission);
    }

    [Fact]
    public async Task ExcepcionDePasarela_ConservaReservaYMarcaIncertidumbre()
    {
        using var db = Finance();
        var (sut, stock, gateway, _) = Preparar(db);
        gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ThrowsAsync(new TimeoutException("Resultado desconocido"));
        await Assert.ThrowsAsync<PagoResultadoInciertoException>(
            () => sut.ProcesarCheckoutAsync(Command()));
        stock.Verify(x => x.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
        stock.Verify(x => x.ConfirmarReservaAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task PagoExplicitamenteRechazado_LiberaReserva()
    {
        using var db = Finance();
        var (sut, stock, gateway, _) = Preparar(db);
        gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(false, null, "Rechazado"));
        await Assert.ThrowsAsync<PagoFallidoException>(
            () => sut.ProcesarCheckoutAsync(Command()));
        stock.Verify(x => x.LiberarStockAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task ExitoSinTransaccion_ConservaReservaParaConciliar()
    {
        using var db = Finance();
        var (sut, stock, gateway, _) = Preparar(db);
        gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, null, null));
        await Assert.ThrowsAsync<PagoResultadoInciertoException>(
            () => sut.ProcesarCheckoutAsync(Command()));
        stock.Verify(x => x.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
        stock.Verify(x => x.ConfirmarReservaAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ErrorDespuesDelPago_NoLiberaReservaNiMarcaExito()
    {
        using var db = Finance();
        var (sut, stock, gateway, commission) = Preparar(db);
        gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, "TXN-I2B2A", null));
        commission.Setup(x => x.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()))
            .ThrowsAsync(new InvalidOperationException("Fallo posterior al cobro"));
        await Assert.ThrowsAsync<PagoResultadoInciertoException>(
            () => sut.ProcesarCheckoutAsync(Command()));
        stock.Verify(x => x.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
        stock.Verify(x => x.ConfirmarReservaAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Controller_DistingueIncertidumbreDeRechazoYNoReintenta(bool incierto)
    {
        using var finance = Finance();
        using var catalog = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase($"Catalog_I2B2A_{Guid.NewGuid():N}").Options);
        var buyer = Guid.NewGuid();
        var productId = Guid.NewGuid();
        catalog.Productos.Add(new Producto
        {
            Id = productId, VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid(),
            Sku = "SKU-I2B2A", Nombre = "Producto QA", CategoriaNombre = "QA",
            PrecioB2C = 10m, PrecioB2B = 10m, Moneda = "USD",
            EstaPublicado = true, EstadoProducto = "Activo"
        });
        await catalog.SaveChangesAsync();

        var (orchestrator, stock, gateway, _) = Preparar(finance);
        if (incierto)
            gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
                .ThrowsAsync(new TimeoutException("Cobro incierto"));
        else
            gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
                .ReturnsAsync(new PagoResult(false, null, "Rechazado"));

        var controller = new CheckoutController(orchestrator, finance, catalog,
            NullLogger<CheckoutController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, buyer.ToString()),
                        new Claim(ClaimTypes.Role, "Comprador")
                    }, "Prueba"))
                }
            }
        };
        var key = "CLAVE_I2B2A_12345678";
        Assert.IsType<ConflictObjectResult>(await controller.ProcesarCheckout(
            new CheckoutRequest(productId, 1), key));
        Assert.IsType<ConflictObjectResult>(await controller.ProcesarCheckout(
            new CheckoutRequest(productId, 1), key));
        var orden = await finance.Ordenes.SingleAsync();
        Assert.Equal(incierto ? EstadoOrden.Pendiente : EstadoOrden.Cancelada, orden.Estado);
        gateway.Verify(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once);
        stock.Verify(x => x.LiberarStockAsync(It.IsAny<Guid>()),
            incierto ? Times.Never() : Times.Once());
    }
}
