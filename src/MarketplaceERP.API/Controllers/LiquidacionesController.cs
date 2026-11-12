/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Liquidaciones a Vendedores — Célula B.
/// Gestiona generación, registro contable simulado y consulta. No integra transferencias bancarias.
/// Split 90% vendedor / 10% marketplace. Genera asientos automáticos.
/// Requisito: PDF1 Módulo de Comisiones y Liquidaciones.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
[Produces("application/json")]
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
    /// Genera un registro interno pendiente para un vendedor, sin desembolso externo.
    /// Agrega todos los SplitPagos no liquidados de órdenes entregadas.
    /// </summary>
    [HttpPost("generar", Name = "GenerarLiquidacion")]
    [PaymentOperationsGuard]
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
                nota = "Liquidación interna generada. El registro contable de prueba no confirma ningún desembolso bancario.",
                transferenciaBancariaConfirmada = false
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    // ── POST /api/liquidaciones/procesar-pago/{id} ────────────────────────────
    /// <summary>
    /// Registra contablemente una liquidación pendiente en modo de simulación, sin transferir fondos.
    /// Genera un asiento contable simulado: DEBE 2108 (Pasivo Vendedores) → HABER 1101 (Caja).
    /// </summary>
    [HttpPost("procesar-pago/{id:guid}", Name = "ProcesarPagoLiquidacion")]
    [PaymentOperationsGuard]
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
                registroContableSimulado = true,
                transferenciaBancariaConfirmada = false,
                aviso = "El asiento y el estado Completada corresponden exclusivamente al registro interno de simulación; no acreditan transferencia bancaria.",
                detalle = "Asiento simulado: DEBE 2108 Pasivo Vendedores | HABER 1101 Caja"
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
    /// Historial de registros contables internos, no de transferencias bancarias confirmadas. Filtrable por vendedorId.
    /// </summary>
    [HttpGet("historial", Name = "HistorialLiquidaciones")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Historial([FromQuery] Guid? vendedorId = null, CancellationToken ct = default)
    {
        var historial = await _liquidaciones.ObtenerHistorialAsync(vendedorId, ct);
        var result = historial.Select(ResumenConDesglose).ToList();
        return Ok(result);
    }

    // ── GET /api/liquidaciones/kpis-vendedores ────────────────────────────────
    /// <summary>KPIs de desempeño por vendedor: fill rate, órdenes y montos liquidados.</summary>
    [HttpGet("kpis-vendedores", Name = "KpisVendedores")]
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
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pendientes(CancellationToken ct)
    {
        var historial = await _liquidaciones.ObtenerHistorialAsync(null, ct);
        var pendientes = historial
            .Where(l => l.Estado == EstadoLiquidacion.Pendiente)
            .Select(ResumenConDesglose).ToList();
        return Ok(pendientes);
    }

    /// <summary>
    /// El desglose se obtiene de la FK 1:N, NO de la antigua ancla 1:1.
    /// Conserva los campos JSON históricos para clientes previos, pero identifica
    /// explícitamente una relación sin backfill o con importes incompatibles.
    /// </summary>
    private static object ResumenConDesglose(Liquidacion l)
    {
        var splits = l.SplitsPago
            .OrderBy(s => s.OrdenId)
            .ThenBy(s => s.Id)
            .ToList();
        var montoVendedor = splits.Sum(s => s.MontoVendedor);
        var verificado = splits.Count > 0 &&
            montoVendedor == l.MontoLiquidar &&
            splits.All(s => s.LiquidacionId == l.Id
                && s.Orden is not null && s.Orden.VendedorId == l.VendedorId
                && s.MontoTotal > 0m && s.MontoVendedor > 0m
                && s.MontoMarketplace >= 0m
                && Math.Abs(s.MontoTotal - s.MontoVendedor - s.MontoMarketplace) <= 0.01m);
        // Únicamente por compatibilidad de la respuesta antigua: si falta el
        // backfill, se marcan las cifras como NO verificadas, nunca como pagos.
        var ventas = verificado ? splits.Sum(s => s.MontoTotal) : l.MontoLiquidar / 0.9m;
        var comision = verificado ? splits.Sum(s => s.MontoMarketplace)
            : l.MontoLiquidar / 0.9m * 0.1m;
        return new
        {
            id = l.Id,
            vendedorId = l.VendedorId,
            periodo = l.FechaCreacion.ToString("yyyy-MM"),
            montoVentas = ventas,
            comisionMarketplace = comision,
            montoLiquidar = l.MontoLiquidar,
            estado = l.Estado.ToString(),
            fechaCreacion = l.FechaCreacion,
            fechaProcesado = l.FechaLiquidacion,
            cantidadSplits = splits.Count,
            desgloseVerificado = verificado,
            splits = splits.Select(s => new
            {
                splitPagoId = s.Id,
                ordenId = s.OrdenId,
                montoTotal = s.MontoTotal,
                montoVendedor = s.MontoVendedor,
                comisionMarketplace = s.MontoMarketplace
            }).ToList(),
            transferenciaBancariaConfirmada = false
        };
    }
}

public record GenerarLiquidacionRequest(Guid VendedorId);
