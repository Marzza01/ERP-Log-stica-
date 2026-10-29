/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

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
/// 2. ProcesarPago    → Rechazo confirmado: liberar reserva; resultado incierto: conservarla.
/// 3. CalcularComision
/// 4. Guardar SplitPago (90% vendedor / 10% marketplace)
/// 5. ConfirmarReserva
/// 6. Retornar CheckoutResult
/// </summary>
public sealed class CheckoutOrchestrator
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

        // No reintentar cobros de una misma orden aunque invoquen directamente
        // al orquestador: consultar primero la huella durable.
        if (await _dbContext.IntentosPago.AnyAsync(i => i.OrdenId == command.OrdenId))
            throw new PagoResultadoInciertoException(command.OrdenId,
                "Existe un intento de pago previo; consultar su estado antes de otro cobro.");

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

        // El registro es obligatorio ANTES de invocar a la pasarela. Si falla
        // la escritura, no se envía cobro y se libera la reserva recién creada.
        var intento = new IntentoPago
        {
            Id = Guid.NewGuid(),
            OrdenId = command.OrdenId,
            ReservaId = reservaId,
            ReferenciaOperacion = $"PAY-{command.OrdenId:N}",
            Monto = command.MontoTotal,
            Moneda = command.Moneda,
            Estado = EstadoIntentoPago.Preparado
        };
        try
        {
            _dbContext.IntentosPago.Add(intento);
            await _dbContext.SaveChangesAsync();
            intento.Estado = EstadoIntentoPago.Enviado;
            intento.ActualizadoUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible registrar intento de pago para OrdenId={OrdenId}", command.OrdenId);
            try { await _stockService.LiberarStockAsync(reservaId); }
            catch (Exception liberarEx)
            {
                _logger.LogError(liberarEx, "No fue posible liberar reserva al fallar registro del intento. OrdenId={OrdenId}", command.OrdenId);
            }
            throw new PagoResultadoInciertoException(command.OrdenId,
                "El intento no pudo registrarse correctamente; es necesario revisar la reserva antes de continuar.", ex);
        }

        // ── PASO 2: Procesar Pago ─────────────────────────────────────────────
        // Si falla: LiberarStock (compensación) → loggear → lanzar PagoFallidoException
        _logger.LogDebug("Saga Paso 2: ProcesarPago. OrdenId={OrdenId}", command.OrdenId);

        var pagoRequest = new PagoRequest(
            OrdenId: command.OrdenId,
            CompradorId: command.CompradorId,
            MontoTotal: command.MontoTotal,
            Moneda: command.Moneda,
            ClaveIdempotencia: intento.ReferenciaOperacion);

        PagoResult pagoResult;
        try
        {
            pagoResult = await _paymentGateway.ProcesarPagoAsync(pagoRequest);
        }
        catch (Exception ex)
        {
            // Una excepción de red NO equivale a un rechazo. Es posible que la
            // pasarela haya cobrado y perdido la respuesta. Conservar la reserva
            // hasta consultar el resultado; liberarla podría causar sobreventa.
            _logger.LogError(ex,
                "Saga Paso 2 INCIERTO: orden {OrdenId}, reserva {ReservaId}. No liberar sin conciliación.",
                command.OrdenId, reservaId);

            await MarcarInciertoSinOcultarErrorAsync(intento, EstadoIntentoPago.Incierto);
            throw new PagoResultadoInciertoException(
                command.OrdenId,
                "La pasarela no confirmó el resultado del pago; requiere conciliación.",
                ex);
        }

        if (!pagoResult.Exitoso)
        {
            _logger.LogWarning(
                "Saga Paso 2 FALLIDO: Pago rechazado. OrdenId={OrdenId}, Error={Error}. Compensando stock ReservaId={ReservaId}.",
                command.OrdenId, pagoResult.MensajeError, reservaId);

            // Documentar rechazo confirmado antes de liberar inventario.
            // Si falla DB no se libera: el registro queda Enviado para investigar.
            try { await CambiarEstadoAsync(intento, EstadoIntentoPago.Rechazado); }
            catch (Exception ex)
            {
                throw new PagoResultadoInciertoException(command.OrdenId,
                    "No se pudo persistir el rechazo; requiere revisión antes de liberar la reserva.", ex);
            }

            // COMPENSACIÓN: liberar el stock reservado
            await _stockService.LiberarStockAsync(reservaId);

            // NOTA: NO se implementa reversión del pago en esta fase (solo log + excepción)
            throw new PagoFallidoException(command.OrdenId, pagoResult.MensajeError);
        }

        if (string.IsNullOrWhiteSpace(pagoResult.TransaccionId))
        {
            _logger.LogError(
                "Saga Paso 2 INCIERTO: la pasarela informó éxito sin TransaccionId. OrdenId={OrdenId}",
                command.OrdenId);
            await MarcarInciertoSinOcultarErrorAsync(intento, EstadoIntentoPago.Incierto);
            throw new PagoResultadoInciertoException(
                command.OrdenId,
                "El pago podría estar aprobado, pero no existe identificador de transacción para conciliar.");
        }

        // Antes de ejecutar comisión o confirmar inventario, guardar el
        // identificador de transacción devuelto. Una caída posterior podrá
        // reconstruir el estado a partir de finance.intentos_pago.
        intento.TransaccionId = pagoResult.TransaccionId;
        try { await CambiarEstadoAsync(intento, EstadoIntentoPago.Aprobado); }
        catch (Exception ex)
        {
            throw new PagoResultadoInciertoException(command.OrdenId,
                "Pago aparentemente aprobado pero sin confirmación durable; requiere conciliación.", ex);
        }

        _logger.LogDebug(
            "Saga Paso 2 OK. TransaccionId={TransaccionId}", pagoResult.TransaccionId);

        // El pago ya pudo haberse efectuado. Cualquier excepción posterior es
        // INCIERTA: no liberar stock ni reintentar el cobro de forma automática.
        try
        {
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

            // ── PASO 5: Confirmar Reserva ─────────────────────────────────────────
            _logger.LogDebug("Saga Paso 5: ConfirmarReserva. ReservaId={ReservaId}", reservaId);

            await _stockService.ConfirmarReservaAsync(reservaId);

            _logger.LogInformation(
                "Checkout Saga COMPLETADO. OrdenId={OrdenId}, TransaccionId={TransaccionId}, " +
                "MontoVendedor={MontoVendedor:C}, MontoMarketplace={MontoMarketplace:C}",
                command.OrdenId, pagoResult.TransaccionId, montoVendedor, montoMarketplace);

            await CambiarEstadoAsync(intento, EstadoIntentoPago.Completado);

            // ── PASO 6: Retornar resultado exitoso ────────────────────────────────
            return new CheckoutResult(
                OrdenId: command.OrdenId,
                TransaccionId: pagoResult.TransaccionId!,
                MontoVendedor: montoVendedor,
                MontoMarketplace: montoMarketplace
            );
        }
        catch (Exception ex)
        {
            // No dar la compra por cancelada si el pago se efectuó. El próximo
            // bloque incorporará un registro durable y reembolso idempotente.
            _logger.LogError(ex,
                "Saga POST-COBRO INCIERTA: OrdenId={OrdenId}, TransaccionId={TransaccionId}. Requiere conciliación.",
                command.OrdenId, pagoResult.TransaccionId);
            await MarcarInciertoSinOcultarErrorAsync(intento, EstadoIntentoPago.RequiereConciliacion);
            throw new PagoResultadoInciertoException(
                command.OrdenId,
                "El pago fue aceptado, pero el Checkout no pudo completarse; requiere conciliación.",
                ex);
        }
    }

    private async Task CambiarEstadoAsync(IntentoPago intento, EstadoIntentoPago estado)
    {
        intento.Estado = estado;
        intento.ActualizadoUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    private async Task MarcarInciertoSinOcultarErrorAsync(IntentoPago intento, EstadoIntentoPago estado)
    {
        try { await CambiarEstadoAsync(intento, estado); }
        catch (Exception persistenciaEx)
        {
            // El último estado durable (Enviado/Aprobado) conserva la señal de
            // incertidumbre. NO reintentar el cobro ni liberar la reserva.
            _logger.LogError(persistenciaEx,
                "No se pudo actualizar el intento de pago incierto. OrdenId={OrdenId}", intento.OrdenId);
        }
    }
}
