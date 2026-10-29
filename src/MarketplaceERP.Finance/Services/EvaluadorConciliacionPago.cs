using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// Veredicto CONSULTIVO: nunca representa una orden aprobada, un reembolso
/// ejecutado ni una autorizacion para liberar inventario.
/// </summary>
public enum DecisionConciliacionPago
{
    SinIntento,
    SinEnvioConfirmado,
    YaCerrado,
    PendienteProveedor,
    CobroConfirmadoPendienteCierre,
    RechazoConfirmadoPendienteCompensacion,
    ResultadoIncierto,
    RequiereRevisionManual
}

public sealed record EvaluacionConciliacionPago(
    Guid OrdenId,
    DecisionConciliacionPago Decision,
    string Motivo,
    string? TransaccionId = null);

/// <summary>
/// Consulta el estado externo de un intento de pago ya registrado, sin ejecutar
/// un nuevo cobro, reembolso, confirmacion o liberacion de stock.
///
/// NO registrar este servicio con un proveedor ficticio. Hasta incorporar un
/// proveedor de recuperacion real, se prueba solo con mocks en tests.
/// </summary>
public sealed class EvaluadorConciliacionPago
{
    private readonly FinanceDbContext _db;
    private readonly IPaymentRecoveryGateway _gateway;
    private readonly ILogger<EvaluadorConciliacionPago> _logger;

    public EvaluadorConciliacionPago(
        FinanceDbContext db,
        IPaymentRecoveryGateway gateway,
        ILogger<EvaluadorConciliacionPago> logger)
    {
        _db = db;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<EvaluacionConciliacionPago> EvaluarAsync(
        Guid ordenId, CancellationToken cancellationToken = default)
    {
        if (ordenId == Guid.Empty)
            throw new ArgumentException("La orden debe tener un identificador valido.", nameof(ordenId));

        var intento = await _db.IntentosPago.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrdenId == ordenId, cancellationToken);

        if (intento is null)
            return Evaluacion(ordenId, DecisionConciliacionPago.SinIntento,
                "No existe un intento durable para esta orden.");

        // Un intento preparado no demuestra que se enviara al proveedor.
        // Nunca inferir pago fallido a partir de ausencia de respuesta.
        if (intento.Estado == EstadoIntentoPago.Preparado)
            return Evaluacion(ordenId, DecisionConciliacionPago.SinEnvioConfirmado,
                "El intento figura como preparado; debe investigarse antes de actuar.");

        // Casos terminales: no realizar consultas externas innecesarias.
        if (intento.Estado is EstadoIntentoPago.Rechazado or EstadoIntentoPago.Completado)
            return Evaluacion(ordenId, DecisionConciliacionPago.YaCerrado,
                "El registro local esta cerrado; este evaluador no modifica su estado.",
                intento.TransaccionId);

        // Solo se puede consultar la referencia canonica creada por Checkout.
        // Evita emitir consultas a una referencia arbitraria o corrupta.
        if (!string.Equals(intento.ReferenciaOperacion, $"PAY-{ordenId:N}",
                StringComparison.Ordinal))
            return Evaluacion(ordenId, DecisionConciliacionPago.RequiereRevisionManual,
                "La referencia de pago local no coincide con la de la orden.");

        ConsultaPagoResult? resultado;
        try
        {
            // Solo CONSULTA. En particular, nunca llama ProcesarPagoAsync/ReembolsarAsync.
            resultado = await _gateway.ConsultarEstadoAsync(
                intento.ReferenciaOperacion, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar estado de pago. OrdenId={OrdenId}", ordenId);
            return Evaluacion(ordenId, DecisionConciliacionPago.ResultadoIncierto,
                "La consulta al proveedor fallo; no realizar acciones financieras automaticas.");
        }

        if (resultado is null)
            return Evaluacion(ordenId, DecisionConciliacionPago.ResultadoIncierto,
                "El proveedor no entrego una respuesta verificable.");

        switch (resultado.Estado)
        {
            case EstadoCobroProveedor.Confirmado:
            {
                var transaccion = resultado.TransaccionId?.Trim();
                if (string.IsNullOrWhiteSpace(transaccion) || transaccion.Length > 200)
                    return Evaluacion(ordenId, DecisionConciliacionPago.RequiereRevisionManual,
                        "El proveedor confirmo un cargo sin identificador de transaccion valido.");

                if (!string.IsNullOrWhiteSpace(intento.TransaccionId) &&
                    !string.Equals(intento.TransaccionId, transaccion, StringComparison.Ordinal))
                    return Evaluacion(ordenId, DecisionConciliacionPago.RequiereRevisionManual,
                        "La transaccion confirmada no coincide con la almacenada.");

                return Evaluacion(ordenId, DecisionConciliacionPago.CobroConfirmadoPendienteCierre,
                    "Pago confirmado por consulta; falta verificar el cierre de la orden y del stock.",
                    transaccion);
            }
            case EstadoCobroProveedor.Rechazado:
                if (intento.Estado is EstadoIntentoPago.Aprobado or EstadoIntentoPago.RequiereConciliacion ||
                    !string.IsNullOrWhiteSpace(intento.TransaccionId))
                    return Evaluacion(ordenId, DecisionConciliacionPago.RequiereRevisionManual,
                        "El rechazo externo contradice un cargo previamente registrado.");

                return Evaluacion(ordenId, DecisionConciliacionPago.RechazoConfirmadoPendienteCompensacion,
                    "Rechazo verificado; falta verificar y liberar la reserva mediante flujo idempotente.");

            case EstadoCobroProveedor.Pendiente:
            case EstadoCobroProveedor.Desconocido:
                if (intento.Estado is EstadoIntentoPago.Aprobado or EstadoIntentoPago.RequiereConciliacion ||
                    !string.IsNullOrWhiteSpace(intento.TransaccionId))
                    return Evaluacion(ordenId, DecisionConciliacionPago.RequiereRevisionManual,
                        "Hay indicios de cobro local y el proveedor aun no confirma un resultado.");

                return Evaluacion(ordenId, DecisionConciliacionPago.PendienteProveedor,
                    "Sin resultado concluyente; no reintentar cobro ni liberar reserva.");

            default:
                return Evaluacion(ordenId, DecisionConciliacionPago.ResultadoIncierto,
                    "El proveedor devolvio un estado no reconocido.");
        }
    }

    private static EvaluacionConciliacionPago Evaluacion(
        Guid id, DecisionConciliacionPago decision, string motivo, string? transaccion = null) =>
        new(id, decision, motivo, transaccion);
}
