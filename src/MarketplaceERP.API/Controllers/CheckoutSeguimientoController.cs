using System.Security.Claims;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>Lectura integrada de orden, cobro, reserva WMS y recuperación financiera.</summary>
/// <remarks>No cambia estados, no despacha, no cobra y no reembolsa.</remarks>
public sealed record SeguimientoCheckoutDto(
    Guid OrdenId,
    string NumeroOrden,
    string EstadoOrden,
    string EstadoPago,
    string EstadoReserva,
    string? EstadoReembolso,
    int CantidadReservadaPendiente,
    int CantidadDespachada,
    decimal MontoTotal,
    string Moneda,
    bool RequiereRevision,
    IReadOnlyList<string> Alertas);

[ApiController]
[Route("api/checkout")]
public sealed class CheckoutSeguimientoController : ControllerBase
{
    private readonly FinanceDbContext _finance;
    private readonly CatalogDbContext _catalog;

    public CheckoutSeguimientoController(FinanceDbContext finance, CatalogDbContext catalog)
    {
        _finance = finance;
        _catalog = catalog;
    }

    /// <summary>Admin ve cualquiera; Comprador únicamente sus propias órdenes.</summary>
    [HttpGet("ordenes/{ordenId:guid}/seguimiento")]
    [Authorize(Roles = "Admin,Comprador")]
    public async Task<IActionResult> Seguimiento(Guid ordenId, CancellationToken ct = default)
    {
        if (ordenId == Guid.Empty) return BadRequest(new { mensaje = "OrdenId inválido." });
        var orden = await _finance.Ordenes.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null) return NotFound();

        if (!User.IsInRole("Admin"))
        {
            if (!User.IsInRole("Comprador")) return Forbid();
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(claim, out var usuarioId) || usuarioId == Guid.Empty)
                return Unauthorized();
            // No revelar la existencia de órdenes ajenas.
            if (orden.CompradorId != usuarioId) return NotFound();
        }

        return Ok(await ConstruirAsync(orden, ct));
    }

    /// <summary>Cola de supervisión, solo Admin. Examina hasta 200 órdenes candidatas recientes.</summary>
    [HttpGet("pendientes-conciliacion")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PendientesConciliacion([FromQuery] int limite = 50,
        CancellationToken ct = default)
    {
        if (!User.IsInRole("Admin")) return Forbid();
        if (limite is < 1 or > 100)
            return BadRequest(new { mensaje = "El límite debe estar entre 1 y 100." });

        // Una orden de Checkout con clave de idempotencia y sin intento de pago
        // puede quedar huérfana tras un fallo al persistir el intento. No se
        // considera huérfana durante los primeros 5 minutos: podría estar activa.
        // Esta vista es de SOLO LECTURA: jamás autoriza repetir un cobro.
        var umbralSinIntentoUtc = DateTime.UtcNow.AddMinutes(-5);
        // Limita el volumen; estados de pago ambiguo, reembolsos y órdenes
        // huérfanas son candidatos para revisión, sin cambiar sus estados.
        var candidatos = await _finance.Ordenes.AsNoTracking()
            .Where(o => _finance.IntentosPago.Any(i => i.OrdenId == o.Id &&
                        (i.Estado == EstadoIntentoPago.Enviado ||
                         i.Estado == EstadoIntentoPago.Incierto ||
                         i.Estado == EstadoIntentoPago.RequiereConciliacion ||
                         (i.Estado == EstadoIntentoPago.CanceladoAntesEnvio &&
                          o.Estado != EstadoOrden.Cancelada) ||
                         i.Estado == EstadoIntentoPago.Aprobado ||
                         (i.Estado == EstadoIntentoPago.Completado && o.Estado == EstadoOrden.Pendiente))) ||
                        _finance.ReembolsosPago.Any(r => r.OrdenId == o.Id &&
                         r.Estado != EstadoReembolsoPago.Finalizado) ||
                        (o.Estado == EstadoOrden.Pendiente &&
                         o.IdempotencyKey != null && o.IdempotencyKey != "" &&
                         o.FechaCreacion < umbralSinIntentoUtc &&
                         !_finance.IntentosPago.Any(i => i.OrdenId == o.Id)) ||
                        o.Estado == EstadoOrden.Cancelada ||
                        (o.Estado != EstadoOrden.Pendiente &&
                         _finance.IntentosPago.Any(i => i.OrdenId == o.Id &&
                         i.Estado == EstadoIntentoPago.Rechazado)))
            .OrderByDescending(o => o.FechaCreacion)
            .Take(200)
            .ToListAsync(ct);

        var pendientes = new List<SeguimientoCheckoutDto>();
        foreach (var orden in candidatos)
        {
            var resultado = await ConstruirAsync(orden, ct);
            if (resultado.RequiereRevision)
                pendientes.Add(resultado);
            if (pendientes.Count == limite) break;
        }
        return Ok(pendientes);
    }

    private async Task<SeguimientoCheckoutDto> ConstruirAsync(Orden orden, CancellationToken ct)
    {
        var intento = await _finance.IntentosPago.AsNoTracking()
            .SingleOrDefaultAsync(i => i.OrdenId == orden.Id, ct);
        var reembolso = await _finance.ReembolsosPago.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OrdenId == orden.Id, ct);
        var reservas = await _catalog.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == orden.Id).ToListAsync(ct);

        var alertas = new List<string>();
        if (intento is null && orden.Estado == EstadoOrden.Pendiente &&
            !string.IsNullOrWhiteSpace(orden.IdempotencyKey) &&
            orden.FechaCreacion < DateTime.UtcNow.AddMinutes(-5))
            alertas.Add("CHECKOUT_PENDIENTE_SIN_INTENTO");
        var cobroProbable = intento?.Estado is EstadoIntentoPago.Aprobado or
            EstadoIntentoPago.RequiereConciliacion or EstadoIntentoPago.Completado;
        var cobroActivo = cobroProbable || !string.IsNullOrWhiteSpace(intento?.TransaccionId);
        if (intento?.Estado is EstadoIntentoPago.Enviado or EstadoIntentoPago.Incierto or
            EstadoIntentoPago.RequiereConciliacion)
            alertas.Add("PAGO_REQUIERE_CONCILIACION");

        if (intento?.Estado == EstadoIntentoPago.CanceladoAntesEnvio)
        {
            // Una cadena de estado en la respuesta NO basta para desbloquear
            // otra compra. Requerir evidencia consistente de la misma
            // transacción de cancelación ANTES del envío.
            var auditoria = await _finance.CancelacionesPreEnvioQa.AsNoTracking()
                .SingleOrDefaultAsync(a => a.OrdenId == orden.Id, ct);
            if (orden.Estado != EstadoOrden.Cancelada)
                alertas.Add("CANCELACION_PREENVIO_INCONSISTENTE");
            if (auditoria is null)
                alertas.Add("CANCELACION_PREENVIO_SIN_AUDITORIA");
            else if (auditoria.IntentoPagoId != intento.Id ||
                     auditoria.OperadorQaId == Guid.Empty ||
                     !Enum.IsDefined(auditoria.Motivo) ||
                     auditoria.RegistradoUtc == default ||
                     auditoria.ReservasLiberadas != reservas.Count ||
                     reservas.Any(r => r.Estado != EstadoReserva.Liberada ||
                         r.CantidadLiberada != r.CantidadOriginal ||
                         r.CantidadConsumida != 0))
                alertas.Add("CANCELACION_PREENVIO_AUDITORIA_INCONSISTENTE");
            if (reservas.Any(r => r.CantidadPendiente > 0))
                alertas.Add("CANCELACION_PREENVIO_CON_RESERVA");
            if (!string.IsNullOrWhiteSpace(intento.TransaccionId) ||
                intento.ReservaId.HasValue ||
                reembolso is not null ||
                await _finance.SplitsPago.AnyAsync(s => s.OrdenId == orden.Id, ct) ||
                await _finance.ConciliacionesPago.AnyAsync(c => c.OrdenId == orden.Id, ct) ||
                await _finance.PartidasContables.AnyAsync(p =>
                    p.ReferenciaOrigenId == orden.Id && p.TipoOperacion == "Venta", ct))
                alertas.Add("CANCELACION_PREENVIO_CON_TRAZA_DE_COBRO");
        }

        if (intento?.Estado == EstadoIntentoPago.Rechazado &&
            reservas.Any(r => r.CantidadPendiente > 0))
            alertas.Add("PAGO_RECHAZADO_CON_RESERVA");
        if (intento?.Estado == EstadoIntentoPago.Rechazado &&
            (orden.Estado is EstadoOrden.Aprobada or EstadoOrden.EnProceso or
             EstadoOrden.Enviada or EstadoOrden.Entregada))
            alertas.Add("ORDEN_ACTIVA_CON_PAGO_RECHAZADO");
        if (cobroActivo && orden.Estado == EstadoOrden.Pendiente)
            alertas.Add("COBRO_SIN_CIERRE_COMERCIAL");
        if (cobroActivo && reservas.Count == 0)
            alertas.Add("COBRO_SIN_RESERVA");
        if (cobroActivo && reservas.Any(r => r.Estado is EstadoReserva.Liberada or
            EstadoReserva.Expirada))
            alertas.Add("COBRO_CON_RESERVA_INVALIDA");
        if (orden.Estado == EstadoOrden.Cancelada && reservas.Any(r => r.CantidadPendiente > 0))
            alertas.Add("ORDEN_CANCELADA_CON_RESERVA");
        if (orden.Estado == EstadoOrden.Cancelada && cobroActivo &&
            reembolso?.Estado != EstadoReembolsoPago.Finalizado)
            alertas.Add("ORDEN_CANCELADA_CON_COBRO_SIN_REEMBOLSO");
        if (reembolso is not null && reembolso.Estado != EstadoReembolsoPago.Finalizado &&
            reembolso.Estado != EstadoReembolsoPago.Rechazado)
            alertas.Add("REEMBOLSO_PENDIENTE");
        if (reembolso?.Estado == EstadoReembolsoPago.Finalizado && orden.Estado != EstadoOrden.Cancelada)
            alertas.Add("REEMBOLSO_FINALIZADO_ORDEN_NO_CANCELADA");

        // Solo el checkout COMERCIAL V2 usa el prefijo de orden CHK2-.
        // El simulador QA usa QA-V2- y puede dejar mapas 1:N incompletos
        // tras fallos pre-envío intencionales; su auditoría independiente
        // determina si la cancelación y compensación son válidas.
        var esCheckoutMultiSku = intento is not null &&
            !intento.ReservaId.HasValue &&
            !string.IsNullOrWhiteSpace(orden.IdempotencyKey) &&
            orden.NumeroOrden.StartsWith("CHK2-", StringComparison.Ordinal) &&
            orden.NumeroOrden.EndsWith("-" + orden.Id.ToString("N"), StringComparison.Ordinal) &&
            string.Equals(intento.ReferenciaOperacion, $"PAY-{orden.Id:N}", StringComparison.Ordinal);

        if (esCheckoutMultiSku)
        {
            var items = await _finance.ItemsOrden.AsNoTracking()
                .Where(i => i.OrdenId == orden.Id)
                .ToListAsync(ct);
            var links = await _finance.IntentoReservasPago.AsNoTracking()
                .Where(x => x.IntentoPagoId == intento!.Id)
                .ToListAsync(ct);

            var mapaValido = items.Count is >= 2 and <= 20 &&
                links.Count == items.Count &&
                reservas.Count == links.Count &&
                items.Select(i => i.ProductoId).Distinct().Count() == items.Count &&
                links.Select(l => l.ProductoId).Distinct().Count() == links.Count &&
                links.Select(l => l.ReservaId).Distinct().Count() == links.Count &&
                links.All(l => l.Procedencia == "CheckoutMultiSkuV2" &&
                    items.Any(i => i.ProductoId == l.ProductoId &&
                                   i.Cantidad == l.Cantidad)) &&
                reservas.All(r => links.Any(l => l.ReservaId == r.Id &&
                    l.ProductoId == r.ProductoId &&
                    l.Cantidad == r.CantidadOriginal));

            if (!mapaValido)
            {
                alertas.Add("CHECKOUT_MULTISKU_MAPA_RESERVAS_INCONSISTENTE");
            }
            else
            {
                EstadoReserva? estadoEsperado = intento!.Estado switch
                {
                    EstadoIntentoPago.Enviado or EstadoIntentoPago.Incierto or
                    EstadoIntentoPago.RequiereConciliacion or EstadoIntentoPago.Aprobado
                        => EstadoReserva.Pendiente,
                    EstadoIntentoPago.Completado => EstadoReserva.Confirmada,
                    EstadoIntentoPago.Rechazado => EstadoReserva.Liberada,
                    _ => null
                };

                if (estadoEsperado.HasValue &&
                    reservas.Any(r => r.Estado != estadoEsperado.Value ||
                        (estadoEsperado == EstadoReserva.Liberada
                            ? r.CantidadLiberada != r.CantidadOriginal || r.CantidadConsumida != 0
                            : r.CantidadLiberada != 0 || r.CantidadConsumida != 0)))
                    alertas.Add("CHECKOUT_MULTISKU_RESERVAS_EN_ESTADO_INESPERADO");
            }
        }

        var estadoReserva = reservas.Count == 0 ? "SinReserva" :
            reservas.Select(r => r.Estado).Distinct().Count() == 1 ?
                reservas[0].Estado.ToString() : "Mixta";

        return new SeguimientoCheckoutDto(
            orden.Id,
            orden.NumeroOrden,
            orden.Estado.ToString(),
            intento?.Estado.ToString() ?? "SinIntento",
            estadoReserva,
            reembolso?.Estado.ToString(),
            reservas.Sum(r => Math.Max(0, r.CantidadPendiente)),
            reservas.Sum(r => r.CantidadConsumida),
            orden.MontoTotal,
            orden.Moneda,
            alertas.Count > 0,
            alertas);
    }
}
