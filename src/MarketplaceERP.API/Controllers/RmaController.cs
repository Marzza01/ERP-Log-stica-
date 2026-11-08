/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
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
[Authorize(Roles = "Admin")]
public class RmaController : ControllerBase
{
    private readonly IRmaService _rmaService;
    private readonly ILogger<RmaController> _logger;
    private readonly FinanceDbContext _financeDb;
    private readonly CatalogDbContext _catalogDb;

    public RmaController(
        IRmaService rmaService,
        ILogger<RmaController> logger,
        FinanceDbContext financeDb,
        CatalogDbContext catalogDb)
    {
        _rmaService = rmaService;
        _logger = logger;
        _financeDb = financeDb;
        _catalogDb = catalogDb;
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
            // Una devolucion solo puede referirse a una venta real y entregada.
            // Validar antes de crear el documento de Catalog; no se confia en IDs del cliente.
            var orden = await _financeDb.Ordenes.AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == request.OrdenId);
            if (orden is null || orden.Estado != EstadoOrden.Entregada)
                return BadRequest(new { mensaje = "La devolucion requiere una orden entregada existente." });

            var cantidadVendida = orden.Items
                .Where(i => i.ProductoId == request.ProductoId)
                .Sum(i => i.Cantidad);
            if (request.Cantidad <= 0 || cantidadVendida == 0 || request.Cantidad > cantidadVendida)
                return BadRequest(new { mensaje = "Producto o cantidad no corresponden a la venta." });

            // Excluir RMAs rechazados del saldo solicitado. Una comprobacion concurrente
            // necesita una garantia transaccional adicional en PostgreSQL.
            var cantidadDevuelta = await _catalogDb.DevolucionesRMA.AsNoTracking()
                .Where(r => r.OrdenId == request.OrdenId &&
                            r.ProductoId == request.ProductoId &&
                            r.Estado != EstadoRMA.Rechazado)
                .SumAsync(r => (int?)r.Cantidad) ?? 0;
            if (request.Cantidad > cantidadVendida - cantidadDevuelta)
                return BadRequest(new { mensaje = "La cantidad excede el saldo disponible para devolucion." });

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
    [Authorize(Roles = "Admin")]
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
