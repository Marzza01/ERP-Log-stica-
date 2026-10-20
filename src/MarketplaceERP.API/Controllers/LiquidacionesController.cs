/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Claims;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Liquidaciones a Vendedores — Célula B.
/// Gestiona el ciclo completo: generación, pago y consulta.
/// Split 90% vendedor / 10% marketplace. Genera asientos automáticos.
/// Requisito: PDF1 Módulo de Comisiones y Liquidaciones.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class LiquidacionesController : ControllerBase
{
    private readonly ILiquidacionService _liquidaciones;
    private readonly ILogger<LiquidacionesController> _logger;

    public LiquidacionesController(ILiquidacionService liquidaciones, ILogger<LiquidacionesController> logger)
    {
        _liquidaciones = liquidaciones;
        _logger = logger;
    }

    // ── POST /api/liquidaciones/generar ───────────────────────────────────────
    /// <summary>
    /// Genera la liquidación pendiente para un vendedor.
    /// Agrega todos los SplitPagos no liquidados de órdenes entregadas.
    /// </summary>
    [HttpPost("generar", Name = "GenerarLiquidacion")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Generar([FromBody] GenerarLiquidacionRequest req, CancellationToken ct)
    {
        try
        {
            var liq = await _liquidaciones.GenerarLiquidacionAsync(req.VendedorId, ct);
            return CreatedAtAction(nameof(Historial), null, new
            {
                liq.Id,
                liq.VendedorId,
                liq.MontoLiquidar,
                Estado = liq.Estado.ToString(),
                liq.FechaCreacion,
                nota = "Liquidación generada. Use POST /procesar-pago/{id} para completar el desembolso."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    // ── POST /api/liquidaciones/procesar-pago/{id} ────────────────────────────
    /// <summary>
    /// Procesa el pago de una liquidación pendiente.
    /// Genera el asiento contable: DEBE 2108 (Pasivo Vendedores) → HABER 1101 (Caja).
    /// </summary>
    [HttpPost("procesar-pago/{id:guid}", Name = "ProcesarPagoLiquidacion")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcesarPago(Guid id, CancellationToken ct)
    {
        try
        {
            var liq = await _liquidaciones.ProcesarPagoAsync(id, ct);
            return Ok(new
            {
                liq.Id,
                liq.VendedorId,
                liq.MontoLiquidar,
                Estado = liq.Estado.ToString(),
                liq.FechaLiquidacion,
                asientoGenerado = true,
                detalle = "DEBE: 2108 Pasivo Vendedores | HABER: 1101 Caja"
            });
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.Contains("no encontrada")
                ? NotFound(new { mensaje = ex.Message })
                : BadRequest(new { mensaje = ex.Message });
        }
    }

    // ── GET /api/liquidaciones/historial ──────────────────────────────────────
    /// <summary>
    /// Historial de liquidaciones. Filtrable por vendedorId.
    /// Admin ve todas o filtra por vendedorId. Vendedor solo ve las suyas.
    /// </summary>
    [HttpGet("historial", Name = "HistorialLiquidaciones")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Historial([FromQuery] Guid? vendedorId = null, CancellationToken ct = default)
    {
        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            // TODO-FASE2: filtrar por vendedor del claim
            var claimVal = User.FindFirstValue("VendedorId")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (Guid.TryParse(claimVal, out var parsedVendedorId))
            {
                vendedorId = parsedVendedorId;
            }
            else
            {
                // Si el claim no es un Guid válido de vendedor, no debe ver datos de otros
                return Ok(new List<object>());
            }
        }

        var historial = await _liquidaciones.ObtenerHistorialAsync(vendedorId, ct);
        var result = historial.Select(l => new
        {
            id = l.Id,
            vendedorId = l.VendedorId,
            periodo = l.FechaCreacion.ToString("'Semana' WW (dd MMM - ") + l.FechaLiquidacion?.ToString("dd MMM yyyy)") ?? l.FechaCreacion.ToString("'Período' yyyy-MM"),
            montoVentas = l.MontoLiquidar / 0.9m,
            comisionMarketplace = l.MontoLiquidar / 0.9m * 0.1m,
            montoLiquidar = l.MontoLiquidar,
            estado = l.Estado.ToString(),
            fechaCreacion = l.FechaCreacion,
            fechaProcesado = l.FechaLiquidacion
        }).ToList();
        return Ok(result);
    }

    // ── GET /api/liquidaciones/kpis-vendedores ────────────────────────────────
    /// <summary>KPIs de desempeño por vendedor: fill rate, órdenes y montos liquidados.</summary>
    [HttpGet("kpis-vendedores", Name = "KpisVendedores")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> KpisVendedores(CancellationToken ct)
    {
        var kpis = await _liquidaciones.ObtenerKpisVendedoresAsync(ct);
        return Ok(new
        {
            generadoEn = DateTime.UtcNow,
            vendedores = kpis.Select(k => new
            {
                k.VendedorId,
                k.TotalOrdenes,
                k.OrdenesEntregadas,
                k.TotalLiquidado,
                fillRatePct = k.FillRate,
                semaforoFillRate = k.FillRate >= 95 ? "Verde" : k.FillRate >= 85 ? "Amarillo" : "Rojo"
            })
        });
    }

    // ── GET /api/liquidaciones/pendientes ──────────────────────────────────
    /// <summary>Lista de liquidaciones con estado Pendiente de pago.</summary>
    [HttpGet("pendientes", Name = "LiquidacionesPendientes")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pendientes(CancellationToken ct)
    {
        var historial = await _liquidaciones.ObtenerHistorialAsync(null, ct);
        var pendientes = historial
            .Where(l => l.Estado == MarketplaceERP.Finance.Entities.EstadoLiquidacion.Pendiente)
            .Select(l => new
            {
                id = l.Id,
                vendedorId = l.VendedorId,
                periodo = l.FechaCreacion.ToString("yyyy-MM"),
                montoVentas = l.MontoLiquidar / 0.9m,
                comisionMarketplace = l.MontoLiquidar / 0.9m * 0.1m,
                montoLiquidar = l.MontoLiquidar,
                estado = l.Estado.ToString(),
                fechaCreacion = l.FechaCreacion,
                fechaProcesado = l.FechaLiquidacion
            }).ToList();
        return Ok(pendientes);
    }
}

public record GenerarLiquidacionRequest(Guid VendedorId);
