/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Logística Inversa y Devoluciones (RMA) (Célula A - PDF 1).
/// Flujo de inspección técnica: Re-stock, Merma (scrap) o Remate.
/// </summary>
[ApiController]
[Route("api/rma")]
[Authorize]
public class RmaController : ControllerBase
{
    private readonly IRmaService _rmaService;
    private readonly ILogger<RmaController> _logger;

    public RmaController(IRmaService rmaService, ILogger<RmaController> logger)
    {
        _rmaService = rmaService;
        _logger = logger;
    }

    /// <summary>
    /// Registra una nueva solicitud de devolución / RMA por parte de un cliente o post-venta.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DevolucionRMA), StatusCodes.Status201Created)]
    public async Task<IActionResult> SolicitarDevolucion([FromBody] RegistrarRmaRequest request)
    {
        try
        {
            var rma = await _rmaService.RegistrarSolicitudRmaAsync(request);
            return CreatedAtAction(nameof(Listar), new { id = rma.Id }, rma);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Ejecuta la inspección técnica de calidad en bodega y emite el dictamen (Re-stock, Scrap, Remate).
    /// </summary>
    [HttpPost("{id:guid}/inspeccionar")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(DevolucionRMA), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Inspeccionar(Guid id, [FromBody] DictaminarRmaRequest request)
    {
        try
        {
            var rma = await _rmaService.InspeccionarDevolucionAsync(id, request);
            if (rma is null)
            {
                return NotFound(new { mensaje = $"Solicitud RMA {id} no encontrada." });
            }

            return Ok(rma);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Lista el historial de devoluciones RMA registradas con filtro opcional por estado.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DevolucionRMA>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] EstadoRMA? estado)
    {
        var items = await _rmaService.ListarRmaAsync(estado);
        return Ok(items);
    }
}
