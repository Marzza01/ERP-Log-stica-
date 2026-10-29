using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class EvaluadorConciliacionI2B2C1Tests
{
    private static FinanceDbContext Db() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I2B2C1_{Guid.NewGuid():N}").Options);

    private static IntentoPago Intento(Guid orden, EstadoIntentoPago estado,
        string? transaccion = null) => new()
    {
        OrdenId = orden,
        ReferenciaOperacion = $"PAY-{orden:N}",
        Estado = estado,
        Monto = 125m,
        Moneda = "USD",
        TransaccionId = transaccion
    };

    private static EvaluadorConciliacionPago Servicio(
        FinanceDbContext db, Mock<IPaymentRecoveryGateway> gateway) =>
        new(db, gateway.Object, NullLogger<EvaluadorConciliacionPago>.Instance);

    [Fact]
    public async Task SinIntento_NoConsultaProveedor()
    {
        using var db = Db();
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var actual = await Servicio(db, gateway).EvaluarAsync(Guid.NewGuid());
        Assert.Equal(DecisionConciliacionPago.SinIntento, actual.Decision);
        gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(EstadoIntentoPago.Preparado, DecisionConciliacionPago.SinEnvioConfirmado)]
    [InlineData(EstadoIntentoPago.Rechazado, DecisionConciliacionPago.YaCerrado)]
    [InlineData(EstadoIntentoPago.Completado, DecisionConciliacionPago.YaCerrado)]
    public async Task EstadosLocalesSinConsultaExterna(
        EstadoIntentoPago estado, DecisionConciliacionPago esperado)
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, estado));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(esperado, actual.Decision);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReferenciaCorrupta_NoConsultaAlProveedor()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        var intento = Intento(id, EstadoIntentoPago.Incierto);
        intento.ReferenciaOperacion = "PAY-OTRA-ORDEN";
        db.IntentosPago.Add(intento);
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.RequiereRevisionManual, actual.Decision);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CargoConfirmado_EsConsultivoYNoCierraLaOrden()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.Incierto));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        gateway.Setup(g => g.ConsultarEstadoAsync($"PAY-{id:N}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Confirmado, "TXN-ABC"));
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.CobroConfirmadoPendienteCierre, actual.Decision);
        Assert.Equal("TXN-ABC", actual.TransaccionId);
        Assert.Equal(EstadoIntentoPago.Incierto, db.IntentosPago.Single().Estado);
        Assert.Null(db.IntentosPago.Single().TransaccionId);
        gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RechazoConfirmado_NoLiberaStockNiMutaIntento()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.Enviado));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        gateway.Setup(g => g.ConsultarEstadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Rechazado, null));
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.RechazoConfirmadoPendienteCompensacion, actual.Decision);
        Assert.Equal(EstadoIntentoPago.Enviado, db.IntentosPago.Single().Estado);
    }

    [Theory]
    [InlineData(EstadoCobroProveedor.Pendiente)]
    [InlineData(EstadoCobroProveedor.Desconocido)]
    public async Task SinConfirmacion_NoAutorizaOtroCobro(EstadoCobroProveedor estado)
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.Incierto));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        gateway.Setup(g => g.ConsultarEstadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(estado, null));
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.PendienteProveedor, actual.Decision);
        Assert.Equal(EstadoIntentoPago.Incierto, db.IntentosPago.Single().Estado);
    }

    [Theory]
    [InlineData(EstadoCobroProveedor.Rechazado)]
    [InlineData(EstadoCobroProveedor.Pendiente)]
    public async Task PagoYaAprobado_NoDegradaPorConsultaAmbigua(EstadoCobroProveedor estado)
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.RequiereConciliacion, "TXN-ANT"));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        gateway.Setup(g => g.ConsultarEstadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(estado, null));
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.RequiereRevisionManual, actual.Decision);
        Assert.Equal("TXN-ANT", db.IntentosPago.Single().TransaccionId);
    }

    [Fact]
    public async Task IdDeTransaccionDiferente_ExigeRevisionManual()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.Aprobado, "TXN-A"));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        gateway.Setup(g => g.ConsultarEstadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Confirmado, "TXN-B"));
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.RequiereRevisionManual, actual.Decision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ConfirmadoSinIdentificador_NoSeDaPorFinalizado(string? transaccion)
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.Enviado));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        gateway.Setup(g => g.ConsultarEstadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Confirmado, transaccion));
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.RequiereRevisionManual, actual.Decision);
    }

    [Fact]
    public async Task ErrorDeRed_EstadoQuedaSinCambios()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.Incierto));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        gateway.Setup(g => g.ConsultarEstadoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());
        var actual = await Servicio(db, gateway).EvaluarAsync(id);
        Assert.Equal(DecisionConciliacionPago.ResultadoIncierto, actual.Decision);
        Assert.Equal(EstadoIntentoPago.Incierto, db.IntentosPago.Single().Estado);
    }

    [Fact]
    public async Task CancelacionSePropagaYNoSePierde()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.IntentosPago.Add(Intento(id, EstadoIntentoPago.Incierto));
        await db.SaveChangesAsync();
        var gateway = new Mock<IPaymentRecoveryGateway>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Servicio(db, gateway).EvaluarAsync(id, cts.Token));
    }

    [Fact]
    public async Task IdInvalidoSeRechaza()
    {
        using var db = Db();
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio(db, gateway).EvaluarAsync(Guid.Empty));
        gateway.VerifyNoOtherCalls();
    }
}
