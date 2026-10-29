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

public sealed class RecuperacionFinancieraI2B2CTests
{
    private static FinanceDbContext CrearDb() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I2B2C_{Guid.NewGuid():N}").Options);

    private static (Guid ordenId, Guid reservaId) Sembrar(
        FinanceDbContext db, EstadoIntentoPago estado = EstadoIntentoPago.RequiereConciliacion,
        string? transaccion = "TX-ORIGINAL")
    {
        var id = Guid.NewGuid();
        var reserva = Guid.NewGuid();
        db.Ordenes.Add(new Orden
        {
            Id = id, NumeroOrden = $"QA-{id:N}", CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(), Estado = EstadoOrden.Pendiente,
            MontoTotal = 25m, SubTotal = 25m, Moneda = "USD"
        });
        db.IntentosPago.Add(new IntentoPago
        {
            Id = Guid.NewGuid(), OrdenId = id, ReservaId = reserva,
            ReferenciaOperacion = $"PAY-{id:N}", Monto = 25m,
            Moneda = "USD", Estado = estado, TransaccionId = transaccion
        });
        db.SaveChanges();
        return (id, reserva);
    }

    private static (RecuperacionFinancieraService servicio,
        Mock<IPaymentRecoveryGateway> gateway, Mock<IStockService> stock) Preparar(FinanceDbContext db)
    {
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var stock = new Mock<IStockService>(MockBehavior.Strict);
        stock.Setup(s => s.LiberarStockAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        var evaluador = new EvaluadorConciliacionPago(db, gateway.Object,
            NullLogger<EvaluadorConciliacionPago>.Instance);
        var servicio = new RecuperacionFinancieraService(db, evaluador,
            gateway.Object, stock.Object, NullLogger<RecuperacionFinancieraService>.Instance);
        return (servicio, gateway, stock);
    }

    [Fact]
    public async Task ConciliacionConfirmada_SeAuditaSinAprobarNiMoverStock()
    {
        using var db = CrearDb();
        var (id, _) = Sembrar(db, EstadoIntentoPago.Incierto, null);
        var (servicio, gateway, stock) = Preparar(db);
        gateway.Setup(g => g.ConsultarEstadoAsync($"PAY-{id:N}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Confirmado, "TX-REAL"));

        var resultado = await servicio.ConciliarYRegistrarAsync(id);
        Assert.Equal(DecisionConciliacionPago.CobroConfirmadoPendienteCierre, resultado.Decision);
        Assert.Equal("TX-REAL", db.IntentosPago.Single().TransaccionId);
        Assert.Equal(EstadoIntentoPago.RequiereConciliacion, db.IntentosPago.Single().Estado);
        Assert.Equal(EstadoOrden.Pendiente, db.Ordenes.Single().Estado);
        Assert.Single(db.ConciliacionesPago);
        stock.VerifyNoOtherCalls();
        gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RechazoConciliado_NoLiberaReservaAutomaticamente()
    {
        using var db = CrearDb();
        var (id, _) = Sembrar(db, EstadoIntentoPago.Incierto, null);
        var (servicio, gateway, stock) = Preparar(db);
        gateway.Setup(g => g.ConsultarEstadoAsync($"PAY-{id:N}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Rechazado, null));
        var resultado = await servicio.ConciliarYRegistrarAsync(id);
        Assert.Equal(DecisionConciliacionPago.RechazoConfirmadoPendienteCompensacion, resultado.Decision);
        Assert.Single(db.ConciliacionesPago);
        Assert.Equal(EstadoOrden.Pendiente, db.Ordenes.Single().Estado);
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ReembolsoConfirmado_FinalizaInventarioYOrdenSinDuplicarSolicitud()
    {
        using var db = CrearDb();
        var (id, reserva) = Sembrar(db);
        var (servicio, gateway, stock) = Preparar(db);
        gateway.Setup(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, "RB-001"));

        var primero = await servicio.ReembolsarSeguroAsync(id);
        var segundo = await servicio.ReembolsarSeguroAsync(id);
        Assert.Equal(EstadoReembolsoProveedor.Completado, primero.Estado);
        Assert.Equal(EstadoReembolsoProveedor.Completado, segundo.Estado);
        Assert.Equal(EstadoReembolsoPago.Finalizado, db.ReembolsosPago.Single().Estado);
        Assert.Equal(EstadoOrden.Cancelada, db.Ordenes.Single().Estado);
        Assert.Equal($"REF-{id:N}", db.ReembolsosPago.Single().ClaveIdempotencia);
        stock.Verify(s => s.LiberarStockAsync(reserva), Times.Once);
        gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        gateway.Verify(g => g.ConsultarReembolsoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ErrorDeRed_NoReenviaReembolso_ConsultaPorClaveEnSegundoIntento()
    {
        using var db = CrearDb();
        var (id, reserva) = Sembrar(db);
        var (servicio, gateway, stock) = Preparar(db);
        gateway.Setup(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("sin respuesta"));
        gateway.Setup(g => g.ConsultarReembolsoAsync($"REF-{id:N}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, "RB-RECUPERADO"));

        var primero = await servicio.ReembolsarSeguroAsync(id);
        Assert.Equal(EstadoReembolsoProveedor.Desconocido, primero.Estado);
        Assert.Equal(EstadoReembolsoPago.Incierto, db.ReembolsosPago.Single().Estado);
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);

        var segundo = await servicio.ReembolsarSeguroAsync(id);
        Assert.Equal(EstadoReembolsoProveedor.Completado, segundo.Estado);
        Assert.Equal(EstadoReembolsoPago.Finalizado, db.ReembolsosPago.Single().Estado);
        gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        gateway.Verify(g => g.ConsultarReembolsoAsync($"REF-{id:N}", It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.LiberarStockAsync(reserva), Times.Once);
    }

    [Fact]
    public async Task RespuestaPendiente_ConsultaLuegoSinReenvio()
    {
        using var db = CrearDb();
        var (id, _) = Sembrar(db);
        var (servicio, gateway, stock) = Preparar(db);
        gateway.Setup(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Pendiente, "RB-P"));
        gateway.Setup(g => g.ConsultarReembolsoAsync($"REF-{id:N}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Pendiente, "RB-P"));
        await servicio.ReembolsarSeguroAsync(id);
        await servicio.ReembolsarSeguroAsync(id);
        Assert.Equal(EstadoReembolsoPago.Pendiente, db.ReembolsosPago.Single().Estado);
        gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task FaltaReserva_NoInvocaProveedor()
    {
        using var db = CrearDb();
        var (id, _) = Sembrar(db);
        db.IntentosPago.Single().ReservaId = null;
        await db.SaveChangesAsync();
        var (servicio, gateway, stock) = Preparar(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReembolsarSeguroAsync(id));
        Assert.Empty(db.ReembolsosPago);
        gateway.VerifyNoOtherCalls();
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task OrdenAprobada_NoReembolsaNiLiberaInventario()
    {
        using var db = CrearDb();
        var (id, _) = Sembrar(db);
        db.Ordenes.Single().Estado = EstadoOrden.Aprobada;
        await db.SaveChangesAsync();
        var (servicio, gateway, stock) = Preparar(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReembolsarSeguroAsync(id));
        gateway.VerifyNoOtherCalls();
        stock.Verify(s => s.LiberarStockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task SplitFinancieroExistente_BloqueaReembolsoAutomatico()
    {
        using var db = CrearDb();
        var (id, _) = Sembrar(db);
        db.SplitsPago.Add(new SplitPago { Id = Guid.NewGuid(), OrdenId = id,
            MontoTotal = 25m, MontoVendedor = 22.5m, MontoMarketplace = 2.5m,
            TransaccionId = "TX-ORIGINAL" });
        await db.SaveChangesAsync();
        var (servicio, gateway, _) = Preparar(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReembolsarSeguroAsync(id));
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReembolsoConfirmadoPeroLiberacionFalla_RecuperaSinNuevoReembolso()
    {
        using var db = CrearDb();
        var (id, reserva) = Sembrar(db);
        var gateway = new Mock<IPaymentRecoveryGateway>(MockBehavior.Strict);
        var stock = new Mock<IStockService>(MockBehavior.Strict);
        gateway.Setup(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, "RB-OK"));
        stock.SetupSequence(s => s.LiberarStockAsync(reserva))
            .ThrowsAsync(new InvalidOperationException("stock indisponible"))
            .Returns(Task.CompletedTask);
        var evaluador = new EvaluadorConciliacionPago(db, gateway.Object,
            NullLogger<EvaluadorConciliacionPago>.Instance);
        var servicio = new RecuperacionFinancieraService(db, evaluador, gateway.Object, stock.Object,
            NullLogger<RecuperacionFinancieraService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ReembolsarSeguroAsync(id));
        Assert.Equal(EstadoReembolsoPago.Confirmado, db.ReembolsosPago.Single().Estado);
        Assert.Equal(EstadoOrden.Pendiente, db.Ordenes.Single().Estado);
        var resultado = await servicio.ReembolsarSeguroAsync(id);
        Assert.Equal(EstadoReembolsoProveedor.Completado, resultado.Estado);
        Assert.Equal(EstadoReembolsoPago.Finalizado, db.ReembolsosPago.Single().Estado);
        gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.LiberarStockAsync(reserva), Times.Exactly(2));
    }

    [Fact]
    public void ModeloContieneIndicesYTokenDeConcurrencia()
    {
        using var db = CrearDb();
        var reembolso = db.Model.FindEntityType(typeof(ReembolsoPago));
        Assert.NotNull(reembolso);
        Assert.True(reembolso!.FindProperty(nameof(ReembolsoPago.Version))!.IsConcurrencyToken);
        Assert.Contains(reembolso.GetIndexes(), i => i.IsUnique &&
            i.Properties.Single().Name == nameof(ReembolsoPago.OrdenId));
        Assert.Contains(reembolso.GetIndexes(), i => i.IsUnique &&
            i.Properties.Single().Name == nameof(ReembolsoPago.ClaveIdempotencia));
        Assert.NotNull(db.Model.FindEntityType(typeof(ConciliacionPago)));
        Assert.NotNull(db.Model.FindEntityType(typeof(IntentoPago))!
            .FindProperty(nameof(IntentoPago.ReservaId)));
    }
}
