using System.Security.Claims;
using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Marketplace contract consumed by LOGIMKT. Uses the ERP's existing JWT
/// authentication; catalog, pricing, reservations and order state remain ERP-owned.
/// </summary>
[ApiController]
[Route("api/integraciones/marketplace")]
public sealed class MarketplaceIntegrationController : ControllerBase
{
    private readonly MarketplaceIntegrationService _service;

    public MarketplaceIntegrationController(MarketplaceIntegrationService service) =>
        _service = service;

    [HttpPost("pedidos")]
    [Authorize(Roles = "Comprador")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] MarketplaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var buyerId) ||
            buyerId == Guid.Empty)
            return Unauthorized(new { tipo = "IdentidadInvalida" });

        try
        {
            var result = await _service.CreateOrderAsync(request, buyerId, cancellationToken);
            return CreatedAtAction(nameof(GetOrder),
                new { externalOrderCode = result.ExternalOrderCode }, result);
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }

    [HttpGet("pedidos/{externalOrderCode}")]
    [Authorize(Roles = "Admin,Comprador")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrder(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        var isAdmin = User.IsInRole("Admin");
        var buyerId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedBuyer)
            ? parsedBuyer : Guid.Empty;
        if (!isAdmin && buyerId == Guid.Empty)
            return Unauthorized(new { tipo = "IdentidadInvalida" });

        try
        {
            return Ok(await _service.GetOrderAsync(externalOrderCode, buyerId, isAdmin, cancellationToken));
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }

    [HttpPost("pedidos/{externalOrderCode}/cancelar")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelOrder(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.CancelOrderAsync(externalOrderCode, cancellationToken));
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }

    [HttpPost("pedidos/{externalOrderCode}/devolucion")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestReturn(
        string externalOrderCode, [FromBody] MarketplaceReturnRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.RequestReturnAsync(externalOrderCode, request, cancellationToken));
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }
}
