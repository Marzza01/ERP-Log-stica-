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
[Route("api/inventario/conteos-ciclicos")]
[Authorize]
public class ConteosCiclicosController : ControllerBase
{
    private readonly IConteoCiclicoService _service;

    public ConteosCiclicosController(IConteoCiclicoService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] EstadoConteoCiclico? estado, CancellationToken cancellationToken) =>
        Ok(await _service.ListarAsync(estado, cancellationToken));

    [HttpPost]
    [Authorize(Roles = "Admin,Vendedor")]
    public async Task<IActionResult> Planificar([FromBody] PlanificarConteoCiclicoRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.PlanificarAsync(request.StockFisicoId, request.Observaciones, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/iniciar")]
    [Authorize(Roles = "Admin,Vendedor")]
    public async Task<IActionResult> Iniciar(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.IniciarAsync(id, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/primer-conteo")]
    [Authorize(Roles = "Admin,Vendedor")]
    public async Task<IActionResult> PrimerConteo(Guid id, [FromBody] RegistrarCantidadConteoRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.RegistrarPrimerConteoAsync(id, request.Cantidad, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/reconteo")]
    [Authorize(Roles = "Admin,Vendedor")]
    public async Task<IActionResult> Reconteo(Guid id, [FromBody] RegistrarCantidadConteoRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.RegistrarReconteoAsync(id, request.Cantidad, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id:guid}/cancelar")]
    [Authorize(Roles = "Admin,Vendedor")]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.CancelarAsync(id, UsuarioActual(), cancellationToken);
        return result.Exitoso ? Ok(result) : BadRequest(result);
    }

    private string? UsuarioActual() => User.Identity?.Name;
}

public sealed record PlanificarConteoCiclicoRequest(Guid StockFisicoId, string? Observaciones = null);
public sealed record RegistrarCantidadConteoRequest(int Cantidad);
