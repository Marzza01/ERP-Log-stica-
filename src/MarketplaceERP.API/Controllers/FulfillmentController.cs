/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Fulfillment: Orquestación de Picking (rutas óptimas), Packing y Despacho.
/// Conecta el WMS (Célula A) con el OMS (Célula B).
/// </summary>
[ApiController]
[Route("api/fulfillment")]
[Produces("application/json")]
[Authorize(Roles = "Admin,Vendedor")]
public class FulfillmentController : ControllerBase
{
    private readonly IFulfillmentService _fulfillmentService;
    private readonly FinanceDbContext _financeDb;
    private readonly CatalogDbContext _catalogDb;
    private readonly MarketplaceERP.Core.Interfaces.IStockService _stockService;

    public FulfillmentController(
        IFulfillmentService fulfillmentService,
        FinanceDbContext financeDb,
        CatalogDbContext catalogDb,
        MarketplaceERP.Core.Interfaces.IStockService stockService)
    {
        _fulfillmentService = fulfillmentService;
        _financeDb = financeDb;
        _catalogDb = catalogDb;
        _stockService = stockService;
    }

    /// <summary>
    /// Genera la lista de recolección (Pick List) con ruta óptima S-Shape para una orden.
    /// Actualiza el estado de la orden en el OMS a EnProceso (Preparación).
    /// </summary>
    [HttpPost("ordenes/{ordenId:guid}/generar-picklist")]
    public async Task<IActionResult> GenerarPickList(Guid ordenId, [FromQuery] string? operador, CancellationToken cancellationToken)
    {
        var orden = await _financeDb.Ordenes
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == ordenId, cancellationToken);

        if (orden is null)
            return NotFound(new { mensaje = $"Orden con ID {ordenId} no encontrada." });

        var itemsSolicitud = orden.Items.Select(i => new ItemPickingSolicitud(
            ProductoId: i.ProductoId,
            Sku: $"SKU-{i.ProductoId.ToString()[..8].ToUpper()}",
            NombreProducto: i.NombreProducto,
            Cantidad: i.Cantidad
        )).ToList();

        var pickList = await _fulfillmentService.GenerarPickListAsync(new GenerarPickListRequest(
            OrdenId: orden.Id,
            NumeroOrden: orden.NumeroOrden,
            OperadorAsignado: operador ?? "Operador Bodega Central",
            Items: itemsSolicitud
        ), cancellationToken);

        // Transición OMS a EnProceso
        orden.Estado = EstadoOrden.EnProceso;
        await _financeDb.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            mensaje = "Pick List generada con ruta óptima.",
            pickListId = pickList.Id,
            numeroPickList = pickList.NumeroPickList,
            items = pickList.Items.Select(i => new
            {
                i.Id,
                i.Sku,
                i.NombreProducto,
                i.CantidadSolicitada,
                Ubicacion = i.UbicacionCodigo,
                Ruta = $"Pasillo {i.Pasillo}, Rack {i.Rack}, Nivel {i.Nivel}"
            })
        });
    }

    /// <summary>
    /// Confirma la recolección física de los productos en bodega.
    /// </summary>
    [HttpPost("picklists/{pickListId:guid}/confirmar")]
    public async Task<IActionResult> ConfirmarPicking(
        Guid pickListId,
        [FromBody] List<ConfirmarItemPickingRequest> confirmaciones,
        CancellationToken cancellationToken)
    {
        var pickList = await _fulfillmentService.ConfirmarPickingAsync(pickListId, confirmaciones, cancellationToken);
        if (pickList is null)
            return NotFound(new { mensaje = "Pick List no encontrada." });

        return Ok(new
        {
            mensaje = "Recolección actualizada.",
            pickList.NumeroPickList,
            Estado = pickList.Estado.ToString(),
            pickList.FechaInicio,
            pickList.FechaFin,
            TotalItems = pickList.Items.Count,
            Completados = pickList.Items.Count(i => i.Recogido)
        });
    }

    /// <summary>
    /// Estación de empaque y rotulado: calcula cubicaje, genera Guía de Envío y prepara para última milla.
    /// </summary>
    [HttpPost("ordenes/{ordenId:guid}/empacar")]
    public async Task<IActionResult> EmpacarYRotular(
        Guid ordenId,
        [FromBody] EmpacarDespachoRequest request,
        CancellationToken cancellationToken)
    {
        var orden = await _financeDb.Ordenes.FindAsync(new object[] { ordenId }, cancellationToken);
        if (orden is null)
            return NotFound(new { mensaje = "Orden no encontrada." });

        var paquete = await _fulfillmentService.EmpacarYRotularAsync(request, cancellationToken);

        // Asignar guía y transportista a la orden en el OMS
        orden.GuiaEnvio = paquete.NumeroGuia;
        orden.Transportista = paquete.Transportista;
        await _financeDb.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            mensaje = "Paquete empacado y rotulado con guía de última milla.",
            paquete.NumeroGuia,
            paquete.NumeroOrden,
            Cubicaje = new
            {
                paquete.PesoRealKg,
                Dimensiones = $"{paquete.LargoCm}x{paquete.AnchoCm}x{paquete.AltoCm} cm",
                VolumenM3 = paquete.VolumenM3
            },
            Destino = $"{paquete.MunicipioDestino}, {paquete.DepartamentoDestino}",
            paquete.Transportista
        });
    }

    /// <summary>
    /// Despacha el paquete con el transportista y avanza el OMS a Enviada.
    /// </summary>
    [HttpPost("paquetes/{paqueteId:guid}/despachar")]
    public async Task<IActionResult> DespacharPaquete(Guid paqueteId, CancellationToken cancellationToken)
    {
        var paquete = await _fulfillmentService.DespacharPaqueteAsync(paqueteId, cancellationToken);
        if (paquete is null)
            return NotFound(new { mensaje = "Paquete no encontrado." });

        var orden = await _financeDb.Ordenes.FirstOrDefaultAsync(o => o.Id == paquete.OrdenId, cancellationToken);
        if (orden != null)
        {
            orden.Estado = EstadoOrden.Enviada;
            orden.FechaEnvio = DateTime.UtcNow;
            await _financeDb.SaveChangesAsync(cancellationToken);

            // Confirmar despacho y descontar inventario físico de las reservas asociadas
            var reservasDespacho = await _catalogDb.ReservasStock
                .Where(r => r.OrdenId == orden.Id && r.Estado == EstadoReserva.Comprometida)
                .ToListAsync(cancellationToken);

            foreach (var res in reservasDespacho)
            {
                await _stockService.ConfirmarDespachoAsync(res.Id);
            }
        }

        return Ok(new
        {
            mensaje = "Paquete despachado con éxito.",
            paquete.NumeroGuia,
            paquete.Transportista,
            paquete.FechaDespacho,
            EstadoOrden = orden?.Estado.ToString()
        });
    }

    /// <summary>
    /// Consulta las Pick Lists activas en bodega.
    /// </summary>
    [HttpGet("picklists")]
    public async Task<IActionResult> ListarPickLists([FromQuery] EstadoPicking? estado, CancellationToken cancellationToken)
    {
        var pickLists = await _fulfillmentService.ListarPickListsAsync(estado, cancellationToken);
        return Ok(pickLists);
    }

    /// <summary>
    /// Consulta los paquetes empacados o en tránsito.
    /// </summary>
    [HttpGet("paquetes")]
    public async Task<IActionResult> ListarPaquetes([FromQuery] EstadoPacking? estado, CancellationToken cancellationToken)
    {
        var paquetes = await _fulfillmentService.ListarPaquetesAsync(estado, cancellationToken);
        return Ok(paquetes);
    }
}
