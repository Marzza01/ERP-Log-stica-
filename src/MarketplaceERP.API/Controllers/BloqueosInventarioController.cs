/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

[ApiController]
[Route("api/inventario/bloqueos")]
[Authorize]
public class BloqueosInventarioController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public BloqueosInventarioController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpPost("bloquear")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(BloqueoInventarioResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BloqueoInventarioResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Bloquear(
        [FromBody] CambiarBloqueoInventarioRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.BloquearStockAsync(
            request.StockFisicoId,
            request.Cantidad,
            request.OperacionId,
            request.Motivo,
            cancellationToken);

        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("desbloquear")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(BloqueoInventarioResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BloqueoInventarioResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Desbloquear(
        [FromBody] CambiarBloqueoInventarioRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryService.DesbloquearStockAsync(
            request.StockFisicoId,
            request.Cantidad,
            request.OperacionId,
            request.Motivo,
            cancellationToken);

        return result.Exitoso ? Ok(result) : BadRequest(result);
    }
}

public sealed record CambiarBloqueoInventarioRequest(
    Guid StockFisicoId,
    int Cantidad,
    Guid OperacionId,
    string Motivo);
