/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

[ApiController]
[Route("api/inventario/ajustes")]
[Authorize]
public class AjustesInventarioController : ControllerBase
{
    private readonly IAjusteInventarioService _service;

    public AjustesInventarioController(IAjusteInventarioService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AjusteInventarioDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] EstadoAjusteInventario? estado, CancellationToken cancellationToken)
    {
        return Ok(await _service.ListarAsync(estado, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Vendedor")]
    public async Task<IActionResult> Crear([FromBody] CrearAjusteInventarioRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CrearAsync(
            request.StockFisicoId,
            request.Tipo,
            request.Cantidad,
            request.Motivo,
            request.Detalle,
            request.Referencia,
            UsuarioActual(),
            cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/aprobar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Aprobar(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.AprobarAsync(id, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/rechazar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Rechazar(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RechazarAsync(id, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/cancelar")]
    [Authorize(Roles = "Admin,Vendedor")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.CancelarAsync(id, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/aplicar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Aplicar(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.AplicarAsync(id, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    private string? UsuarioActual() => User.Identity?.Name;
}

public sealed record CrearAjusteInventarioRequest(
    Guid StockFisicoId,
    TipoAjusteInventario Tipo,
    int Cantidad,
    string Motivo,
    string? Detalle = null,
    string? Referencia = null);
