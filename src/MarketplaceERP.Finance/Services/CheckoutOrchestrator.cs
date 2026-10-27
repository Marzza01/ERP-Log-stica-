/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
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
public sealed class CheckoutOrchestrator
{
    private readonly IStockService _stockService;
    private readonly ICommissionService _commissionService;
    private readonly IPaymentGateway _paymentGateway;
    private readonly FinanceDbContext _dbContext;
    private readonly IContabilidadService? _contabilidad;
    private readonly ILogger<CheckoutOrchestrator> _logger;
    private readonly IServiceProvider? _serviceProvider;

    public CheckoutOrchestrator(
        IStockService stockService,
        ICommissionService commissionService,
        IPaymentGateway paymentGateway,
        FinanceDbContext dbContext,
        ILogger<CheckoutOrchestrator> logger,
        IContabilidadService? contabilidad = null,
        IServiceProvider? serviceProvider = null)
    {
        _stockService = stockService;
        _commissionService = commissionService;
        _paymentGateway = paymentGateway;
        _dbContext = dbContext;
        _logger = logger;
        _contabilidad = contabilidad;
        _serviceProvider = serviceProvider;
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
        catch (InvalidOperationException ex) when (ex.Message.Contains("pasarela de pagos no está configurada") || ex.Message.Contains("503"))
        {
            _logger.LogError(ex, "Saga Paso 2: Pasarela no disponible. Liberando reserva {ReservaId}.", reservaId);
            await _stockService.LiberarStockAsync(reservaId);
            throw;
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

        try
        {
            // ── PASO 3: Calcular Comisión (OPCIÓN A) ──────────────────────────────
            _logger.LogDebug("Saga Paso 3: CalcularComision. VendedorId={VendedorId}", command.VendedorId);

            // Se carga la orden existente de la BD para obtener SubTotal, ImpuestoIva y CostoEnvio separados
            // sin necesidad de inferirlos ni dividir entre 1.13.
            var ordenDb = await _dbContext.Ordenes.FindAsync(new object[] { command.OrdenId })
                ?? throw new InvalidOperationException($"La orden {command.OrdenId} no existe en la base de datos para calcular la comisión y split.");

            decimal subTotal = ordenDb.SubTotal;
            decimal ivaOrden = ordenDb.ImpuestoIva;
            decimal costoEnvio = ordenDb.CostoEnvio;
            decimal totalOrden = ordenDb.MontoTotal;

            // DECISIÓN-PENDIENTE / validar con contador:
            // (a) Comisión calculada sobre el SubTotal neto (sin IVA ni CostoEnvio).
            // (b) MontoVendedor = SubTotal − comisión (el IVA no pertenece al vendedor, va íntegro a 2105).
            // (c) MontoMarketplace = comisión + CostoEnvio.
            var montoComision = await _commissionService.CalcularComisionAsync(
                command.VendedorId,
                subTotal);

            var montoVendedor = Math.Round(subTotal - montoComision, 2, MidpointRounding.AwayFromZero);
            var montoMarketplace = Math.Round(montoComision + costoEnvio, 2, MidpointRounding.AwayFromZero);

            // Validación estricta en el orquestador: MontoVendedor + MontoMarketplace + ImpuestoIva == MontoTotal (tolerancia 0)
            var balanceVerif = montoVendedor + montoMarketplace + ivaOrden;
            if (balanceVerif != totalOrden)
            {
                throw new InvalidOperationException(
                    $"Descuadre en split de pago: MontoVendedor ({montoVendedor:F2}) + MontoMarketplace ({montoMarketplace:F2}) + IVA ({ivaOrden:F2}) = {balanceVerif:F2} != MontoTotal ({totalOrden:F2}).");
            }

            var porcentajeVendedor = totalOrden > 0
                ? Math.Round((montoVendedor / totalOrden) * 100, 2, MidpointRounding.AwayFromZero)
                : 0m;
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
                MontoTotal = totalOrden,
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

            // ── PASO 5: Generar Asiento Contable de Venta (idempotente) ───────────
            if (_contabilidad != null)
            {
                _logger.LogDebug("Saga Paso 5: Generando asiento de venta para OrdenId={OrdenId}", command.OrdenId);
                await _contabilidad.GenerarAsientoVentaAsync(
                    command.OrdenId,
                    ordenDb.SubTotal,
                    ordenDb.ImpuestoIva,
                    ordenDb.TipoComprobante);
            }

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
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Saga ERROR TRAS COBRO EXITOSO para OrdenId={OrdenId}, TransaccionId={TransaccionId}. Iniciando compensación...",
                command.OrdenId, pagoResult.TransaccionId);

            // 1. Compensar reserva de stock liberando las unidades
            try
            {
                await _stockService.LiberarStockAsync(reservaId);
                _logger.LogInformation("Compensación de stock exitosa para ReservaId={ReservaId}.", reservaId);
            }
            catch (Exception stockEx)
            {
                _logger.LogCritical(stockEx, "CRITICAL: Fallo al liberar reserva {ReservaId} durante compensación de saga.", reservaId);
            }

            // 2. Revertir el cobro en la pasarela o registrar pago huérfano en tabla de conciliación
            await RevertirCobroOConciliarAsync(command.OrdenId, pagoResult.TransaccionId!, command.MontoTotal, command.Moneda, ex.Message);

            throw;
        }
    }

    /// <summary>
    /// Intenta el reembolso automático con la pasarela. Si no hay API o falla,
    /// persiste un registro en la tabla de conciliación finance.pagos_conciliacion_huerfanos y emite LogError.
    /// </summary>
    public async Task RevertirCobroOConciliarAsync(
        Guid ordenId,
        string transaccionId,
        decimal monto,
        string moneda,
        string motivo)
    {
        bool reembolsado = false;
        string? reembolsoTxId = null;

        try
        {
            var resultadoReembolso = await _paymentGateway.ReembolsarAsync(transaccionId, monto, motivo);
            if (resultadoReembolso != null && resultadoReembolso.Exitoso)
            {
                reembolsado = true;
                reembolsoTxId = resultadoReembolso.TransaccionId;
                _logger.LogInformation("Reembolso automático exitoso para TransaccionId={TransaccionId}, ReembolsoId={ReembolsoId}.",
                    transaccionId, reembolsoTxId);
            }
            else
            {
                _logger.LogError("La pasarela no pudo reembolsar TransaccionId={TransaccionId}: {Error}",
                    transaccionId, resultadoReembolso?.MensajeError);
            }
        }
        catch (Exception refundEx)
        {
            _logger.LogError(refundEx, "Excepción al intentar reembolsar TransaccionId={TransaccionId}.", transaccionId);
        }

        try
        {
            var conciliacion = new PagoConciliacionHuerfano
            {
                Id = Guid.NewGuid(),
                OrdenId = ordenId,
                TransaccionId = transaccionId,
                Monto = monto,
                Moneda = moneda,
                MotivoFallo = motivo.Length > 500 ? motivo[..500] : motivo,
                Reembolsado = reembolsado,
                ReembolsoTransaccionId = reembolsoTxId,
                FechaRegistro = DateTime.UtcNow,
                FechaResolucion = reembolsado ? DateTime.UtcNow : null
            };

            if (_serviceProvider != null)
            {
                using var scope = _serviceProvider.CreateScope();
                var isolatedDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
                isolatedDb.PagosConciliacionHuerfanos.Add(conciliacion);
                await isolatedDb.SaveChangesAsync();
            }
            else
            {
                _dbContext.PagosConciliacionHuerfanos.Add(conciliacion);
                await _dbContext.SaveChangesAsync();
            }

            _logger.LogInformation("Registro de conciliación huérfano guardado (Id={Id}, Reembolsado={Reembolsado}).",
                conciliacion.Id, reembolsado);
        }
        catch (Exception dbEx)
        {
            _logger.LogCritical(dbEx,
                "CRITICAL: No se pudo guardar el registro de conciliación para pago huérfano OrdenId={OrdenId}, TxId={TransaccionId}, Monto={Monto}",
                ordenId, transaccionId, monto);
        }
    }
}
