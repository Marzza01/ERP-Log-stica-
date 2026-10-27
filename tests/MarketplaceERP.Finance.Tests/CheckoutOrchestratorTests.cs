/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Tests unitarios para el CheckoutOrchestrator (Equipo 3).
/// Usa InMemory EF Core para aislar la base de datos.
/// </summary>
public class CheckoutOrchestratorTests
{
    private static FinanceDbContext CrearDbContextEnMemoria()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;
        return new FinanceDbContext(options);
    }

    [Fact]
    public async Task ProcesarCheckoutAsync_FlujoCorrecto_DebeRetornarResultadoExitoso()
    {
        // Arrange
        var mockStock = new Mock<IStockService>();
        var mockComision = new Mock<ICommissionService>();
        var mockPago = new Mock<IPaymentGateway>();

        var reservaId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();

        mockStock.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ReservaResult(reservaId, true, null));

        mockPago.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, "TXN-TEST-001", null));

        mockComision.Setup(c => c.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()))
            .ReturnsAsync(10m); // 10% de comisión sobre 100

        mockStock.Setup(s => s.ConfirmarDespachoAsync(reservaId))
            .Returns(Task.CompletedTask);

        using var dbContext = CrearDbContextEnMemoria();
        var orden = new Orden
        {
            Id = ordenId,
            SubTotal = 100m,
            ImpuestoIva = 0m,
            CostoEnvio = 0m,
            MontoTotal = 100m
        };
        dbContext.Ordenes.Add(orden);
        await dbContext.SaveChangesAsync();

        var sut = new CheckoutOrchestrator(
            mockStock.Object, mockComision.Object, mockPago.Object,
            dbContext, NullLogger<CheckoutOrchestrator>.Instance);

        var command = new CheckoutCommand(
            OrdenId: ordenId,
            CompradorId: Guid.NewGuid(),
            VendedorId: Guid.NewGuid(),
            ProductoId: Guid.NewGuid(),
            Cantidad: 2,
            MontoTotal: 100m,
            Moneda: "USD");

        // Act
        var resultado = await sut.ProcesarCheckoutAsync(command);

        // Assert
        resultado.Should().NotBeNull();
        resultado.OrdenId.Should().Be(ordenId);
        resultado.TransaccionId.Should().Be("TXN-TEST-001");
        resultado.MontoVendedor.Should().Be(90m);
        resultado.MontoMarketplace.Should().Be(10m);

        // Verificar que el SplitPago se guardó en DB
        var split = await dbContext.SplitsPago.FirstOrDefaultAsync(s => s.OrdenId == ordenId);
        split.Should().NotBeNull();
        split!.MontoVendedor.Should().Be(90m);
        split.MontoMarketplace.Should().Be(10m);
    }

    [Fact]
    public async Task ProcesarCheckoutAsync_OrdenNoExisteEnDB_DebeLanzarInvalidOperationException()
    {
        // Arrange
        var mockStock = new Mock<IStockService>();
        var mockComision = new Mock<ICommissionService>();
        var mockPago = new Mock<IPaymentGateway>();

        var reservaId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();

        mockStock.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ReservaResult(reservaId, true, null));
        mockPago.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, "TXN-TEST-002", null));

        using var dbContext = CrearDbContextEnMemoria();
        var sut = new CheckoutOrchestrator(
            mockStock.Object, mockComision.Object, mockPago.Object,
            dbContext, NullLogger<CheckoutOrchestrator>.Instance);

        var command = new CheckoutCommand(
            OrdenId: ordenId,
            CompradorId: Guid.NewGuid(),
            VendedorId: Guid.NewGuid(),
            ProductoId: Guid.NewGuid(),
            Cantidad: 1,
            MontoTotal: 100m,
            Moneda: "USD");

        // Act
        var act = () => sut.ProcesarCheckoutAsync(command);

        // Assert: no debe fallbackear, debe lanzar InvalidOperationException
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*La orden {ordenId} no existe en la base de datos*");
    }

    [Fact]
    public async Task ProcesarCheckoutAsync_PagoFallido_DebeLiberarStockYLanzarPagoFallidoException()
    {
        // Arrange
        var mockStock = new Mock<IStockService>();
        var mockComision = new Mock<ICommissionService>();
        var mockPago = new Mock<IPaymentGateway>();

        var reservaId = Guid.NewGuid();

        mockStock.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ReservaResult(reservaId, true, null));

        mockPago.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(false, null, "Fondos insuficientes"));

        mockStock.Setup(s => s.LiberarStockAsync(reservaId))
            .Returns(Task.CompletedTask);

        using var dbContext = CrearDbContextEnMemoria();

        var sut = new CheckoutOrchestrator(
            mockStock.Object, mockComision.Object, mockPago.Object,
            dbContext, NullLogger<CheckoutOrchestrator>.Instance);

        var command = new CheckoutCommand(
            OrdenId: Guid.NewGuid(),
            CompradorId: Guid.NewGuid(),
            VendedorId: Guid.NewGuid(),
            ProductoId: Guid.NewGuid(),
            Cantidad: 1,
            MontoTotal: 50m,
            Moneda: "USD");

        // Act
        var act = async () => await sut.ProcesarCheckoutAsync(command);

        // Assert
        await act.Should().ThrowAsync<PagoFallidoException>();

        // Verificar compensación: el stock fue liberado
        mockStock.Verify(s => s.LiberarStockAsync(reservaId), Times.Once);
        mockComision.Verify(c => c.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task ProcesarCheckoutAsync_StockNoDisponible_DebeLanzarStockInsuficienteException()
    {
        // Arrange
        var mockStock = new Mock<IStockService>();
        var mockComision = new Mock<ICommissionService>();
        var mockPago = new Mock<IPaymentGateway>();

        mockStock.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ReservaResult(Guid.Empty, false, "Sin stock disponible"));

        using var dbContext = CrearDbContextEnMemoria();

        var sut = new CheckoutOrchestrator(
            mockStock.Object, mockComision.Object, mockPago.Object,
            dbContext, NullLogger<CheckoutOrchestrator>.Instance);

        var command = new CheckoutCommand(
            OrdenId: Guid.NewGuid(),
            CompradorId: Guid.NewGuid(),
            VendedorId: Guid.NewGuid(),
            ProductoId: Guid.NewGuid(),
            Cantidad: 999,
            MontoTotal: 9999m,
            Moneda: "USD");

        // Act
        var act = async () => await sut.ProcesarCheckoutAsync(command);

        // Assert
        await act.Should().ThrowAsync<StockInsuficienteException>();

        // Verificar que NO se intentó procesar el pago
        mockPago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
    }
}
