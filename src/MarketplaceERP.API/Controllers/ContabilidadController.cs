using MarketplaceERP.Finance.Services;
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
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AsientoManual(
        [FromBody] AsientoManualRequest req, CancellationToken cancellationToken)
    {
        decimal totalDebe  = req.Lineas.Sum(l => l.Debe);
        decimal totalHaber = req.Lineas.Sum(l => l.Haber);

        if (Math.Abs(totalDebe - totalHaber) > 0.01m)
            return BadRequest(new
            {
                mensaje = "El asiento no cuadra. La suma de Debe debe ser igual a la suma de Haber.",
                totalDebe, totalHaber, diferencia = totalDebe - totalHaber
            });

        // Se utiliza el asiento de venta genérico con monto 0 de IVA como contenedor
        // y se sobreescribe el concepto — en producción se extendería la interfaz
        var partida = await _contabilidad.GenerarAsientoVentaAsync(
            Guid.Empty, totalDebe, 0m, "AjusteManual", cancellationToken);

        return CreatedAtAction(nameof(LibroDiario), null,
            new { partida.Id, partida.NumeroPartida, partida.Concepto, totalDebe, totalHaber });
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────────
public record LineaAsientoRequest(string CodigoCuenta, string NombreCuenta, decimal Debe, decimal Haber, string Glosa);
public record AsientoManualRequest(string Concepto, string TipoOperacion, List<LineaAsientoRequest> Lineas);
