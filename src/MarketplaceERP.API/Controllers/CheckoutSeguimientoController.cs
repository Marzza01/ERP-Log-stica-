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

        // Limita el volumen; los estados ambiguos del pago y reembolso tienen
        // prioridad, seguidos por órdenes sin cierre o canceladas recientes.
        var candidatos = await _finance.Ordenes.AsNoTracking()
            .Where(o => _finance.IntentosPago.Any(i => i.OrdenId == o.Id &&
                        (i.Estado == EstadoIntentoPago.Enviado ||
                         i.Estado == EstadoIntentoPago.Incierto ||
                         i.Estado == EstadoIntentoPago.RequiereConciliacion ||
                         i.Estado == EstadoIntentoPago.Aprobado ||
                         (i.Estado == EstadoIntentoPago.Completado && o.Estado == EstadoOrden.Pendiente))) ||
                        _finance.ReembolsosPago.Any(r => r.OrdenId == o.Id &&
                         r.Estado != EstadoReembolsoPago.Finalizado) ||
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
        var cobroProbable = intento?.Estado is EstadoIntentoPago.Aprobado or
            EstadoIntentoPago.RequiereConciliacion or EstadoIntentoPago.Completado;
        var cobroActivo = cobroProbable || !string.IsNullOrWhiteSpace(intento?.TransaccionId);
        if (intento?.Estado is EstadoIntentoPago.Enviado or EstadoIntentoPago.Incierto or
            EstadoIntentoPago.RequiereConciliacion)
            alertas.Add("PAGO_REQUIERE_CONCILIACION");

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
