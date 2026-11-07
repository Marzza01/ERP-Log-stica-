/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.API.Services;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Contabilidad General — Célula B.
/// Expone el Libro Diario, Libro Mayor y Balance de Comprobación.
/// Requisito: PDF1 R-CONT-01 a R-CONT-05
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
[Produces("application/json")]
public class ContabilidadController : ControllerBase
{
    private readonly IContabilidadService _contabilidad;
    private readonly ILogger<ContabilidadController> _logger;

    public ContabilidadController(IContabilidadService contabilidad, ILogger<ContabilidadController> logger)
    {
        _contabilidad = contabilidad;
        _logger = logger;
    }

    // ── GET /api/contabilidad/libro-diario ────────────────────────────────────
    /// <summary>
    /// Retorna el Libro Diario con todos los asientos de partida doble.
    /// Filtrable por rango de fechas.
    /// </summary>
    [HttpGet("libro-diario", Name = "LibroDiario")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> LibroDiario(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        CancellationToken cancellationToken = default)
    {
        var partidas = await _contabilidad.ObtenerLibroDiarioAsync(desde, hasta, cancellationToken);

        return Ok(new
        {
            filtro = new { desde, hasta },
            totalPartidas = partidas.Count(),
            partidas
        });
    }

    // ── GET /api/contabilidad/libro-mayor ─────────────────────────────────────
    /// <summary>
    /// Retorna el Libro Mayor agrupado por cuenta contable.
    /// Opcionalmente filtrado por código de cuenta.
    /// </summary>
    [HttpGet("libro-mayor", Name = "LibroMayor")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> LibroMayor(
        [FromQuery] string? cuenta = null,
        CancellationToken cancellationToken = default)
    {
        var cuentas = await _contabilidad.ObtenerLibroMayorAsync(cuenta, cancellationToken);

        return Ok(new
        {
            cuentaFiltro = cuenta ?? "Todas",
            totalCuentas = cuentas.Count(),
            cuentas
        });
    }

    // ── GET /api/contabilidad/balance-comprobacion ────────────────────────────
    /// <summary>
    /// Retorna el Balance de Comprobación (sumas de Debe y Haber por cuenta).
    /// Verifica la ecuación contable fundamental: Debe == Haber.
    /// </summary>
    [HttpGet("balance-comprobacion", Name = "BalanceComprobacion")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> BalanceComprobacion(CancellationToken cancellationToken)
    {
        var balance = await _contabilidad.ObtenerBalanceComprobacionAsync(cancellationToken);

        return Ok(new
        {
            generadoEn = DateTime.UtcNow,
            totalDebe = balance.TotalDebe,
            totalHaber = balance.TotalHaber,
            diferenciaAjuste = balance.TotalDebe - balance.TotalHaber,
            estaCuadrado = balance.EstaCuadrado,
            estadoAuditoria = balance.EstaCuadrado ? "✓ Contabilidad cuadrada" : "⚠ Discrepancia detectada",
            cuentas = balance.Cuentas
        });
    }

    // ── POST /api/contabilidad/asiento-manual ─────────────────────────────────
    /// <summary>
    /// Registra un asiento de ajuste manual (Debe == Haber obligatorio).
    /// Útil para reversiones, correcciones o asientos de cierre.
    /// </summary>
    [HttpPost("asiento-manual", Name = "AsientoManual")]
    [PaymentOperationsGuard]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AsientoManual(
        [FromBody] AsientoManualRequest req, CancellationToken cancellationToken)
    {
        if (req is null || req.Lineas is null || req.Lineas.Any(l => l is null))
            return BadRequest(new { mensaje = "Debe proporcionar el asiento y todas sus líneas." });

        try
        {
            var lineas = req.Lineas.Select(l => new LineaAsientoInput(
                l.CodigoCuenta, l.Debe, l.Haber, l.Glosa)).ToList();

            // No usar GenerarAsientoVentaAsync: cambiaría las cuentas enviadas.
            var partida = await _contabilidad.GenerarAsientoManualAsync(
                req.Concepto, req.TipoOperacion, lineas, cancellationToken);

            return CreatedAtAction(nameof(LibroDiario), null, new
            {
                partida.Id,
                partida.NumeroPartida,
                partida.Concepto,
                totalDebe = partida.TotalDebe,
                totalHaber = partida.TotalHaber,
                lineas = partida.Lineas.Select(l => new
                {
                    l.CodigoCuenta, l.NombreCuenta, l.Debe, l.Haber, l.Glosa
                })
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────────
public record LineaAsientoRequest(string CodigoCuenta, string NombreCuenta, decimal Debe, decimal Haber, string Glosa);
public record AsientoManualRequest(string Concepto, string TipoOperacion, List<LineaAsientoRequest> Lineas);
