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

public sealed class RegistroDurableI2B2BTests
{
    private static FinanceDbContext CrearDb() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I2B2B_{Guid.NewGuid():N}").Options);

    private static CheckoutCommand Comando() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 25m, "USD");

    private static (CheckoutOrchestrator sut, Mock<IStockService> stock,
        Mock<IPaymentGateway> gateway, Mock<ICommissionService> commission)
        Preparar(FinanceDbContext db)
    {
        var stock = new Mock<IStockService>();
        stock.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ReservaResult(Guid.NewGuid(), true, null));
        stock.Setup(s => s.LiberarStockAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        stock.Setup(s => s.ConfirmarReservaAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        var gateway = new Mock<IPaymentGateway>();
        var commission = new Mock<ICommissionService>();
        commission.Setup(s => s.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()))
            .ReturnsAsync(2.5m);
        return (new CheckoutOrchestrator(stock.Object, commission.Object,
            gateway.Object, db, NullLogger<CheckoutOrchestrator>.Instance),
            stock, gateway, commission);
    }

    [Fact]
    public async Task LaPasarelaSoloSeInvocaDespuesDelRegistroDurable()
    {
        using var db = CrearDb();
        var (sut, _, gateway, _) = Preparar(db);
        var cmd = Comando();
        gateway.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .Callback<PagoRequest>(request =>
            {
                var intento = db.IntentosPago.Single();
                Assert.Equal(EstadoIntentoPago.Enviado, intento.Estado);
                Assert.Equal($"PAY-{cmd.OrdenId:N}", intento.ReferenciaOperacion);
                Assert.Equal(intento.ReferenciaOperacion, request.ClaveIdempotencia);
            })
            .ReturnsAsync(new PagoResult(false, null, "Rechazado"));
        await Assert.ThrowsAsync<PagoFallidoException>(() => sut.ProcesarCheckoutAsync(cmd));
        Assert.Equal(EstadoIntentoPago.Rechazado, db.IntentosPago.Single().Estado);
    }

    [Fact]
    public async Task ExcepcionDelProveedor_ConservaIntentoIncierto()
    {
        using var db = CrearDb();
        var (sut, stock, gateway, _) = Preparar(db);
        gateway.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ThrowsAsync(new TimeoutException("Tiempo agotado"));
        await Assert.ThrowsAsync<PagoResultadoInciertoException>(() => sut.ProcesarCheckoutAsync(Comando()));
        Assert.Equal(EstadoIntentoPago.Incierto, db.IntentosPago.Single().Estado);
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ExitoSinId_QuedaInciertoSinLiberar()
    {
        using var db = CrearDb();
        var (sut, stock, gateway, _) = Preparar(db);
        gateway.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, null, null));
        await Assert.ThrowsAsync<PagoResultadoInciertoException>(() => sut.ProcesarCheckoutAsync(Comando()));
        Assert.Equal(EstadoIntentoPago.Incierto, db.IntentosPago.Single().Estado);
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task FalloPosteriorAlCobro_ConservaIdYRequiereConciliacion()
    {
        using var db = CrearDb();
        var (sut, stock, gateway, commission) = Preparar(db);
        gateway.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, "TXN-DEMO", null));
        commission.Setup(c => c.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()))
            .ThrowsAsync(new InvalidOperationException("Error de comisión"));
        await Assert.ThrowsAsync<PagoResultadoInciertoException>(() => sut.ProcesarCheckoutAsync(Comando()));
        Assert.Equal(EstadoIntentoPago.RequiereConciliacion, db.IntentosPago.Single().Estado);
        Assert.Equal("TXN-DEMO", db.IntentosPago.Single().TransaccionId);
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task FlujoExitoso_RegistraCompletadoYTransaccion()
    {
        using var db = CrearDb();
        var (sut, stock, gateway, _) = Preparar(db);
        gateway.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, "TXN-OK", null));
        var cmd = Comando();
        await sut.ProcesarCheckoutAsync(cmd);
        var intento = db.IntentosPago.Single();
        Assert.Equal(EstadoIntentoPago.Completado, intento.Estado);
        Assert.Equal("TXN-OK", intento.TransaccionId);
        stock.Verify(s => s.ConfirmarReservaAsync(It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task IntentoPrevio_DetieneNuevoCobroAntesDeReservar()
    {
        using var db = CrearDb();
        var (sut, stock, gateway, _) = Preparar(db);
        var cmd = Comando();
        db.IntentosPago.Add(new IntentoPago
        {
            OrdenId = cmd.OrdenId, ReferenciaOperacion = $"PAY-{cmd.OrdenId:N}",
            Monto = cmd.MontoTotal, Estado = EstadoIntentoPago.Incierto
        });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<PagoResultadoInciertoException>(() => sut.ProcesarCheckoutAsync(cmd));
        gateway.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
        stock.Verify(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public void ModeloDefineIndiceUnicoYReferenciaReembolsos()
    {
        using var db = CrearDb();
        var tipo = db.Model.FindEntityType(typeof(IntentoPago));
        Assert.NotNull(tipo);
        Assert.Contains(tipo!.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "OrdenId" }));
        Assert.Contains(tipo.GetIndexes(), i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ReferenciaOperacion" }));
        var contrato = typeof(IPaymentRecoveryGateway);
        Assert.NotNull(contrato.GetMethod(nameof(IPaymentRecoveryGateway.ConsultarEstadoAsync)));
        Assert.NotNull(contrato.GetMethod(nameof(IPaymentRecoveryGateway.ReembolsarAsync)));
    }
}
