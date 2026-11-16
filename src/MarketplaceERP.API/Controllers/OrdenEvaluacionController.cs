using System.Security.Claims;
using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/ordenes/evaluacion")]
public sealed class OrdenEvaluacionController(
    OrdenEvaluacionService orders, ILogger<OrdenEvaluacionController> logger) : ControllerBase
{
    [HttpGet("opciones")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await orders.GetOptionsAsync(ct)); }
        catch (EvaluacionOrdenException ex) { return StatusCode(ex.StatusCode, new { mensaje = ex.Message }); }
    }

    /// <summary>
    /// Read-only reconciliation of a previously submitted request. The caller
    /// must send the original payload and the same Idempotency-Key; this endpoint
    /// never creates an order or reserves inventory.
    /// </summary>
    [HttpPost("resultado")]
    public async Task<IActionResult> Result([FromBody] CrearOrdenRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var identities = User.FindAll(ClaimTypes.NameIdentifier).Select(c => c.Value).ToArray();
        if (identities.Length == 0 || identities.Any(value =>
                !Guid.TryParse(value, out var actorId) || actorId == Guid.Empty) ||
            identities.Select(Guid.Parse).Distinct().Count() != 1)
            return Unauthorized();

        var keys = Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]))
            return BadRequest(new { mensaje = "Idempotency-Key es obligatoria para recuperar la solicitud." });

        try
        {
            var result = await orders.GetStatusAsync(request, Guid.Parse(identities[0]), keys[0]!, ct);
            if (result is null)
                return NotFound(new
                {
                    codigo = "SOLICITUD_NO_REGISTRADA",
                    mensaje = "Todavía no existe una orden confirmada para esta solicitud."
                });
            return Ok(result);
        }
        catch (EvaluacionOrdenException ex)
        {
            return StatusCode(ex.StatusCode, new { mensaje = ex.Message });
        }
    }

    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var order = await orders.CancelAsync(id, ct);
            logger.LogInformation("Compensación OMS evaluación: Orden={OrdenId}, Actor={Actor}",
                id, User.FindFirstValue(ClaimTypes.NameIdentifier));
            return Ok(new { order.Id, estado = order.Estado.ToString() });
        }
        catch (EvaluacionOrdenException ex) { return StatusCode(ex.StatusCode, new { mensaje = ex.Message }); }
    }
}
