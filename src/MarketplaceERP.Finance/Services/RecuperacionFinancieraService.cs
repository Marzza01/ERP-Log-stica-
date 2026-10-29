using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// Motor de recuperación. No está registrado en DI ni expuesto por HTTP hasta contar
/// con una pasarela real verificable y autorización administrativa. Ningún reembolso
/// se envía dos veces: después de Enviado solo se consulta el mismo identificador.
/// </summary>
public sealed class RecuperacionFinancieraService
{
    private readonly FinanceDbContext _db;
    private readonly EvaluadorConciliacionPago _evaluador;
    private readonly IPaymentRecoveryGateway _gateway;
    private readonly IStockService _stock;
    private readonly ILogger<RecuperacionFinancieraService> _logger;

    public RecuperacionFinancieraService(
        FinanceDbContext db,
        EvaluadorConciliacionPago evaluador,
        IPaymentRecoveryGateway gateway,
        IStockService stock,
        ILogger<RecuperacionFinancieraService> logger)
    {
        _db = db;
        _evaluador = evaluador;
        _gateway = gateway;
        _stock = stock;
        _logger = logger;
    }

    /// <summary>
    /// Consulta de solo lectura externa. Guarda el veredicto en un histórico inmutable.
    /// Un cobro verificado se marca para revisión, nunca aprueba ni despacha la orden.
    /// </summary>
    public async Task<EvaluacionConciliacionPago> ConciliarYRegistrarAsync(
        Guid ordenId, CancellationToken cancellationToken = default)
    {
        var resultado = await _evaluador.EvaluarAsync(ordenId, cancellationToken);
        var intento = await _db.IntentosPago.SingleOrDefaultAsync(
            i => i.OrdenId == ordenId, cancellationToken);
        if (intento is null) return resultado;

        _db.ConciliacionesPago.Add(new ConciliacionPago
        {
            IntentoPagoId = intento.Id,
            OrdenId = ordenId,
            Decision = resultado.Decision.ToString(),
            TransaccionId = resultado.TransaccionId,
            ConsultadoUtc = DateTime.UtcNow
        });

        if (resultado.Decision == DecisionConciliacionPago.CobroConfirmadoPendienteCierre)
        {
            // No presumir pedido entregado; el siguiente paso requiere decisión
            // explícita entre terminar Checkout y devolver el cobro.
            intento.TransaccionId = resultado.TransaccionId;
            intento.Estado = EstadoIntentoPago.RequiereConciliacion;
            intento.ActualizadoUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return resultado;
    }

    /// <summary>
    /// Reembolso COMPLETO para una orden pendiente cuyo cargo fue confirmado.
    /// Se requiere decisión administrativa externa: NO se invoca automáticamente.
    /// Es un flujo de compensación, no una cancelación general de órdenes enviadas.
    /// </summary>
    public async Task<ReembolsoPagoResult> ReembolsarSeguroAsync(
        Guid ordenId, CancellationToken cancellationToken = default)
    {
        if (ordenId == Guid.Empty)
            throw new ArgumentException("OrdenId inválido.", nameof(ordenId));

        var reembolso = await _db.ReembolsosPago.SingleOrDefaultAsync(
            r => r.OrdenId == ordenId, cancellationToken);

        if (reembolso is null)
        {
            var orden = await _db.Ordenes.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == ordenId, cancellationToken)
                ?? throw new InvalidOperationException("La orden no existe.");
            var intento = await _db.IntentosPago.AsNoTracking()
                .SingleOrDefaultAsync(i => i.OrdenId == ordenId, cancellationToken)
                ?? throw new InvalidOperationException("No existe intento de pago para la orden.");

            if (orden.Estado != EstadoOrden.Pendiente ||
                orden.FechaEnvio.HasValue || orden.FechaEntregaReal.HasValue)
                throw new InvalidOperationException("Solo se compensa Checkout pendiente sin despacho.");
            if (await _db.SplitsPago.AnyAsync(s => s.OrdenId == ordenId, cancellationToken))
                throw new InvalidOperationException("La orden tiene split financiero: requiere revisión contable manual.");
            if (intento.Estado is not (EstadoIntentoPago.Aprobado or EstadoIntentoPago.RequiereConciliacion) ||
                string.IsNullOrWhiteSpace(intento.TransaccionId) || intento.TransaccionId.Length > 200 ||
                intento.ReservaId is not Guid reservaId || reservaId == Guid.Empty ||
                intento.Monto <= 0 || intento.Monto != orden.MontoTotal ||
                !string.Equals(intento.Moneda, orden.Moneda, StringComparison.Ordinal) ||
                !string.Equals(intento.ReferenciaOperacion, $"PAY-{ordenId:N}", StringComparison.Ordinal))
                throw new InvalidOperationException("El pago y la reserva no están verificados; revisión manual obligatoria.");

            reembolso = new ReembolsoPago
            {
                OrdenId = ordenId,
                ReservaId = reservaId,
                ClaveIdempotencia = $"REF-{ordenId:N}",
                TransaccionId = intento.TransaccionId,
                Monto = intento.Monto,
                Moneda = intento.Moneda,
                Estado = EstadoReembolsoPago.Preparado
            };
            _db.ReembolsosPago.Add(reembolso);
            // La UNIQUE(OrdenId) de PostgreSQL impide dos registros concurrentes;
            // nunca llamar al proveedor si falla la creación.
            await _db.SaveChangesAsync(cancellationToken);
        }

        switch (reembolso.Estado)
        {
            case EstadoReembolsoPago.Finalizado:
                return new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, reembolso.ReembolsoProveedorId);
            case EstadoReembolsoPago.Rechazado:
                return new ReembolsoPagoResult(EstadoReembolsoProveedor.Rechazado, reembolso.ReembolsoProveedorId);
            case EstadoReembolsoPago.Confirmado:
                await FinalizarCompensacionAsync(reembolso, cancellationToken);
                return new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, reembolso.ReembolsoProveedorId);
            case EstadoReembolsoPago.Preparado:
                // Claim persistido con token de concurrencia: en caso de carrera
                // solo un proceso consigue salir de Preparado antes del proveedor.
                await CambiarEstadoAsync(reembolso, EstadoReembolsoPago.Enviado, cancellationToken);
                return await InvocarProveedorAsync(reembolso, true, cancellationToken);
            case EstadoReembolsoPago.Enviado:
            case EstadoReembolsoPago.Incierto:
            case EstadoReembolsoPago.Pendiente:
                // Nunca reemitir ReembolsarAsync: consultar por la misma clave.
                return await InvocarProveedorAsync(reembolso, false, cancellationToken);
            default:
                throw new InvalidOperationException("Estado de reembolso no reconocido.");
        }
    }

    private async Task<ReembolsoPagoResult> InvocarProveedorAsync(
        ReembolsoPago reembolso, bool esPrimerEnvio, CancellationToken cancellationToken)
    {
        ReembolsoPagoResult? respuesta;
        try
        {
            if (esPrimerEnvio)
            {
                respuesta = await _gateway.ReembolsarAsync(new ReembolsoPagoRequest(
                    reembolso.OrdenId, reembolso.TransaccionId,
                    reembolso.ClaveIdempotencia, reembolso.Monto, reembolso.Moneda), cancellationToken);
            }
            else
            {
                respuesta = await _gateway.ConsultarReembolsoAsync(
                    reembolso.ClaveIdempotencia, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ya pudo salir al proveedor; registrar incertidumbre y no reenviar.
            await MarcarInciertoAsync(reembolso);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reembolso con resultado incierto. OrdenId={OrdenId}", reembolso.OrdenId);
            await MarcarInciertoAsync(reembolso);
            return new ReembolsoPagoResult(EstadoReembolsoProveedor.Desconocido, null);
        }

        if (respuesta is null ||
            (respuesta.Estado == EstadoReembolsoProveedor.Completado &&
             (string.IsNullOrWhiteSpace(respuesta.ReembolsoId) || respuesta.ReembolsoId.Length > 200)) ||
            (reembolso.ReembolsoProveedorId is not null && respuesta.ReembolsoId is not null &&
             reembolso.ReembolsoProveedorId != respuesta.ReembolsoId))
        {
            await CambiarEstadoAsync(reembolso, EstadoReembolsoPago.Incierto, CancellationToken.None);
            return new ReembolsoPagoResult(EstadoReembolsoProveedor.Desconocido, null);
        }

        if (respuesta.ReembolsoId is not null)
            reembolso.ReembolsoProveedorId = respuesta.ReembolsoId;
        var estado = respuesta.Estado switch
        {
            EstadoReembolsoProveedor.Completado => EstadoReembolsoPago.Confirmado,
            EstadoReembolsoProveedor.Pendiente => EstadoReembolsoPago.Pendiente,
            EstadoReembolsoProveedor.Rechazado => EstadoReembolsoPago.Rechazado,
            _ => EstadoReembolsoPago.Incierto
        };
        await CambiarEstadoAsync(reembolso, estado, CancellationToken.None);

        if (estado == EstadoReembolsoPago.Confirmado)
            await FinalizarCompensacionAsync(reembolso, cancellationToken);

        return respuesta;
    }

    private async Task FinalizarCompensacionAsync(
        ReembolsoPago reembolso, CancellationToken cancellationToken)
    {
        // El proveedor ya devolvió dinero. Una caída aquí no permite otro
        // reembolso: el estado Confirmado obliga a reintentar SOLO la liberación.
        var orden = await _db.Ordenes.SingleAsync(o => o.Id == reembolso.OrdenId, cancellationToken);
        if (orden.Estado != EstadoOrden.Pendiente ||
            orden.FechaEnvio.HasValue || orden.FechaEntregaReal.HasValue)
            throw new InvalidOperationException(
                "Reembolso confirmado; la orden cambió de estado. Requiere compensación administrativa.");

        await _stock.LiberarStockAsync(reembolso.ReservaId);
        orden.Estado = EstadoOrden.Cancelada;
        orden.FechaCancelacion ??= DateTime.UtcNow;
        await CambiarEstadoAsync(reembolso, EstadoReembolsoPago.Finalizado, cancellationToken);
    }

    private async Task MarcarInciertoAsync(ReembolsoPago reembolso)
    {
        try { await CambiarEstadoAsync(reembolso, EstadoReembolsoPago.Incierto, CancellationToken.None); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No fue posible persistir incertidumbre del reembolso. OrdenId={OrdenId}", reembolso.OrdenId);
        }
    }

    private async Task CambiarEstadoAsync(
        ReembolsoPago reembolso, EstadoReembolsoPago nuevoEstado, CancellationToken cancellationToken)
    {
        reembolso.Estado = nuevoEstado;
        reembolso.ActualizadoUtc = DateTime.UtcNow;
        reembolso.Version = Guid.NewGuid();
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException(
                "Otro proceso modificó el reembolso; consultar el estado persistido antes de continuar.", ex);
        }
    }
}
