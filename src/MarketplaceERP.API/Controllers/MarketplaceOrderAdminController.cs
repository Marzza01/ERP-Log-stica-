using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Admin-only operational controls for LOGIMKT proforma orders.
/// These transitions do not call payment gateways or create accounting entries.
/// </summary>
[ApiController]
[Route("api/ordenes/marketplace")]
[Authorize(Roles = "Admin")]
public sealed class MarketplaceOrderAdminController : ControllerBase
{
    private readonly MarketplaceIntegrationService _service;

    public MarketplaceOrderAdminController(MarketplaceIntegrationService service) =>
        _service = service;

    [HttpPut("{externalOrderCode}/estado")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Transition(
        string externalOrderCode, [FromBody] MarketplaceOrderTransitionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.TransitionOrderAsync(
                externalOrderCode, request, cancellationToken));
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }

    [HttpPost("{externalOrderCode}/verificar-transferencia")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyTransfer(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.VerifyTransferAsync(externalOrderCode, cancellationToken));
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }

    [HttpPost("{externalOrderCode}/autorizar-credito")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> VerifyCredit(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.VerifyCreditAsync(externalOrderCode, cancellationToken));
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }

    [HttpPost("{externalOrderCode}/confirmar-cobro-contra-entrega")]
    [ProducesResponseType(typeof(MarketplaceOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmCashOnDelivery(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _service.ConfirmCashOnDeliveryAsync(externalOrderCode, cancellationToken));
        }
        catch (MarketplaceIntegrationException ex)
        {
            return StatusCode(ex.StatusCode, new { tipo = ex.Code, mensaje = ex.Message });
        }
    }
}
