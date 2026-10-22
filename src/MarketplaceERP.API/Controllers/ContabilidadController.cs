/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

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
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize(Roles = "Admin")]
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
    /// Retorna el Libro Diario con todos los asientos de partida doble paginados.
    /// Filtrable por rango de fechas, page y pageSize (máximo 200).
    /// </summary>
    [HttpGet("libro-diario", Name = "LibroDiario")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> LibroDiario(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _contabilidad.ObtenerLibroDiarioPaginadoAsync(desde, hasta, page, pageSize, cancellationToken);

        return Ok(new
        {
            filtro = new { desde, hasta },
            totalPartidas = resultado.Total,
            page = resultado.Page,
            pageSize = resultado.PageSize,
            totalPaginas = (int)Math.Ceiling((double)resultado.Total / resultado.PageSize),
            partidas = resultado.Partidas
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
    /// Valida cuentas, no duplicidad de signos en líneas, y persiste exactamente las líneas enviadas.
    /// </summary>
    [HttpPost("asiento-manual", Name = "AsientoManual")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AsientoManual(
        [FromBody] AsientoManualRequest req, CancellationToken cancellationToken)
    {
        if (req == null || req.Lineas == null || !req.Lineas.Any())
            return BadRequest(new { mensaje = "Debe proporcionar los datos del asiento y sus líneas." });

        try
        {
            var lineasInput = req.Lineas.Select(l => new LineaAsientoInput(
                l.CodigoCuenta,
                l.Debe,
                l.Haber,
                l.Glosa)).ToList();

            var partida = await _contabilidad.GenerarAsientoManualAsync(
                concepto: req.Concepto,
                tipoOperacion: string.IsNullOrWhiteSpace(req.TipoOperacion) ? "Ajuste" : req.TipoOperacion,
                lineas: lineasInput,
                referenciaOrigenId: null,
                documentoReferencia: null,
                cancellationToken: cancellationToken);

            return CreatedAtAction(nameof(LibroDiario), null,
                new
                {
                    partida.Id,
                    partida.NumeroPartida,
                    partida.Concepto,
                    totalDebe = partida.TotalDebe,
                    totalHaber = partida.TotalHaber,
                    lineas = partida.Lineas
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────────
public record LineaAsientoRequest(string CodigoCuenta, string? NombreCuenta, decimal Debe, decimal Haber, string? Glosa);
public record AsientoManualRequest(string Concepto, string TipoOperacion, List<LineaAsientoRequest> Lineas);
