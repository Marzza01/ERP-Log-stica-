/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Warehouse Management System (WMS) para mapeo dinámico de bodega (Célula A - PDF 1 y PDF 2).
/// Gestiona Pasillos, Racks, Niveles y Posiciones de Almacenamiento.
/// </summary>
[ApiController]
[Route("api/wms")]
[Authorize(Roles = "Admin,Vendedor")]
public class WmsController : ControllerBase
{
    private readonly IWmsService _wmsService;
    private readonly ILogger<WmsController> _logger;

    public WmsController(IWmsService wmsService, ILogger<WmsController> logger)
    {
        _wmsService = wmsService;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene el mapa dinámico completo de la bodega central de LOGIMKT con métricas de ocupación.
    /// </summary>
    [HttpGet("mapa")]
    [ProducesResponseType(typeof(ResumenBodegaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMapaBodega()
    {
        var resumen = await _wmsService.ObtenerMapaBodegaAsync();
        return Ok(resumen);
    }

    /// <summary>
    /// Lista las ubicaciones físicas de almacenamiento con filtros por pasillo y disponibilidad.
    /// </summary>
    [HttpGet("ubicaciones")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarUbicaciones([FromQuery] string? pasillo, [FromQuery] bool? soloDisponibles)
    {
        var ubicaciones = await _wmsService.ListarUbicacionesAsync(pasillo, soloDisponibles);
        return Ok(ubicaciones);
    }

    /// <summary>
    /// Reubica un lote de stock físico a una nueva posición dentro del almacén.
    /// </summary>
    [HttpPost("reubicar")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reubicar([FromBody] ReubicarStockRequest request)
    {
        var exito = await _wmsService.ReubicarStockAsync(request.StockFisicoId, request.NuevaUbicacionId);
        if (!exito)
        {
            return BadRequest(new { mensaje = "No se pudo reubicar el stock. Verifique que los IDs existan." });
        }

        return Ok(new { mensaje = "Stock reubicado exitosamente en el WMS." });
    }
}

public record ReubicarStockRequest(Guid StockFisicoId, Guid NuevaUbicacionId);
