/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// Comando que inicia el proceso de checkout.
/// </summary>
public record CheckoutCommand(
    Guid OrdenId,
    Guid CompradorId,
    Guid VendedorId,
    Guid ProductoId,
    int Cantidad,
    decimal MontoTotal,
    string Moneda
);

/// <summary>
/// Resultado de un checkout procesado exitosamente.
/// </summary>
public record CheckoutResult(
    Guid OrdenId,
    string TransaccionId,
    decimal MontoVendedor,
    decimal MontoMarketplace
);

/// <summary>
/// Orquestador del flujo de Checkout (patrón Saga básico).
/// Coordina la reserva de stock, el pago y el split de comisión.
///
/// Flujo Saga:
/// 1. ReservarStock   → Falla: lanza StockInsuficienteException (sin compensación)
/// 2. ProcesarPago    → Falla: LiberarStock (compensación) + lanza PagoFallidoException
/// 3. CalcularComision
/// 4. Guardar SplitPago (90% vendedor / 10% marketplace)
/// 5. ConfirmarDespacho
/// 6. Retornar CheckoutResult
/// </summary>
internal sealed class CheckoutOrchestrator
{
    private readonly IStockService _stockService;
    private readonly ICommissionService _commissionService;
    private readonly IPaymentGateway _paymentGateway;
    private readonly FinanceDbContext _dbContext;
    private readonly ILogger<CheckoutOrchestrator> _logger;

    public CheckoutOrchestrator(
        IStockService stockService,
        ICommissionService commissionService,
        IPaymentGateway paymentGateway,
        FinanceDbContext dbContext,
        ILogger<CheckoutOrchestrator> logger)
    {
        _stockService = stockService;
        _commissionService = commissionService;
        _paymentGateway = paymentGateway;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Procesa el flujo completo de checkout siguiendo el patrón Saga.
    /// </summary>
    /// <param name="command">Datos del checkout a procesar.</param>
    /// <returns>Resultado exitoso con detalles del split de pago.</returns>
    /// <exception cref="StockInsuficienteException">Si no hay stock disponible para reservar.</exception>
    /// <exception cref="PagoFallidoException">Si el pago falla (el stock ya fue liberado como compensación).</exception>
    public async Task<CheckoutResult> ProcesarCheckoutAsync(CheckoutCommand command)
    {
        _logger.LogInformation(
            "Iniciando Checkout Saga. OrdenId={OrdenId}, Monto={Monto:C} {Moneda}",
            command.OrdenId, command.MontoTotal, command.Moneda);

        // ── PASO 1: Reservar Stock ────────────────────────────────────────────
        // Si falla: no había nada que revertir → lanzar directamente
        _logger.LogDebug("Saga Paso 1: ReservarStock. ProductoId={ProductoId}", command.ProductoId);

        var reservaResult = await _stockService.ReservarStockAsync(
            command.ProductoId,
            command.Cantidad,
            command.OrdenId);

        if (!reservaResult.Exitoso)
        {
            _logger.LogWarning(
                "Saga Paso 1 FALLIDO: Stock insuficiente. ProductoId={ProductoId}, Error={Error}",
                command.ProductoId, reservaResult.MensajeError);

            throw new StockInsuficienteException(reservaResult.MensajeError
                ?? $"No hay stock disponible para el producto {command.ProductoId}.");
        }

        var reservaId = reservaResult.ReservaId;
        _logger.LogDebug("Saga Paso 1 OK. ReservaId={ReservaId}", reservaId);

        // ── PASO 2: Procesar Pago ─────────────────────────────────────────────
        // Si falla: LiberarStock (compensación) → loggear → lanzar PagoFallidoException
        _logger.LogDebug("Saga Paso 2: ProcesarPago. OrdenId={OrdenId}", command.OrdenId);

        var pagoRequest = new PagoRequest(
            OrdenId: command.OrdenId,
            CompradorId: command.CompradorId,
            MontoTotal: command.MontoTotal,
            Moneda: command.Moneda);

        PagoResult pagoResult;
        try
        {
            pagoResult = await _paymentGateway.ProcesarPagoAsync(pagoRequest);
        }
        catch (Exception ex)
        {
            // Error inesperado en la pasarela → compensar reserva
            _logger.LogError(ex,
                "Saga Paso 2 EXCEPCIÓN: Error inesperado en pasarela de pagos. Liberando reserva {ReservaId}.",
                reservaId);

            await _stockService.LiberarStockAsync(reservaId);
            throw new PagoFallidoException(command.OrdenId, ex.Message);
        }

        if (!pagoResult.Exitoso)
        {
            _logger.LogWarning(
                "Saga Paso 2 FALLIDO: Pago rechazado. OrdenId={OrdenId}, Error={Error}. Compensando stock ReservaId={ReservaId}.",
                command.OrdenId, pagoResult.MensajeError, reservaId);

            // COMPENSACIÓN: liberar el stock reservado
            await _stockService.LiberarStockAsync(reservaId);

            // NOTA: NO se implementa reversión del pago en esta fase (solo log + excepción)
            throw new PagoFallidoException(command.OrdenId, pagoResult.MensajeError);
        }

        _logger.LogDebug(
            "Saga Paso 2 OK. TransaccionId={TransaccionId}", pagoResult.TransaccionId);

        // ── PASO 3: Calcular Comisión ─────────────────────────────────────────
        _logger.LogDebug("Saga Paso 3: CalcularComision. VendedorId={VendedorId}", command.VendedorId);

        var montoComision = await _commissionService.CalcularComisionAsync(
            command.VendedorId,
            command.MontoTotal);

        var montoVendedor = Math.Round(command.MontoTotal - montoComision, 2);
        var montoMarketplace = Math.Round(montoComision, 2);
        var porcentajeVendedor = command.MontoTotal > 0
            ? Math.Round((montoVendedor / command.MontoTotal) * 100, 2)
            : 90m;
        var porcentajeMarketplace = 100m - porcentajeVendedor;

        _logger.LogDebug(
            "Saga Paso 3 OK. MontoVendedor={MontoVendedor:C}, MontoMarketplace={MontoMarketplace:C}",
            montoVendedor, montoMarketplace);

        // ── PASO 4: Guardar SplitPago en DB ──────────────────────────────────
        _logger.LogDebug("Saga Paso 4: Guardando SplitPago en DB.");

        var splitPago = new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = command.OrdenId,
            MontoTotal = command.MontoTotal,
            PorcentajeVendedor = porcentajeVendedor,
            MontoVendedor = montoVendedor,
            PorcentajeMarketplace = porcentajeMarketplace,
            MontoMarketplace = montoMarketplace,
            TransaccionId = pagoResult.TransaccionId!,
            FechaProcesado = DateTime.UtcNow
        };

        _dbContext.SplitsPago.Add(splitPago);
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("Saga Paso 4 OK. SplitPagoId={SplitPagoId}", splitPago.Id);

        // ── PASO 5: Confirmar Despacho ────────────────────────────────────────
        _logger.LogDebug("Saga Paso 5: ConfirmarDespacho. ReservaId={ReservaId}", reservaId);

        await _stockService.ConfirmarDespachoAsync(reservaId);

        _logger.LogInformation(
            "Checkout Saga COMPLETADO. OrdenId={OrdenId}, TransaccionId={TransaccionId}, " +
            "MontoVendedor={MontoVendedor:C}, MontoMarketplace={MontoMarketplace:C}",
            command.OrdenId, pagoResult.TransaccionId, montoVendedor, montoMarketplace);

        // ── PASO 6: Retornar resultado exitoso ────────────────────────────────
        return new CheckoutResult(
            OrdenId: command.OrdenId,
            TransaccionId: pagoResult.TransaccionId!,
            MontoVendedor: montoVendedor,
            MontoMarketplace: montoMarketplace
        );
    }
}
