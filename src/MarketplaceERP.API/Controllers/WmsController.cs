/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Warehouse Management System (WMS) para mapeo dinámico de bodega.
/// Gestiona ubicaciones y transferencias físicas de stock entre posiciones.
/// </summary>
[ApiController]
[Route("api/wms")]
[Authorize]
public class WmsController : ControllerBase
{
    private readonly IWmsService _wmsService;
    private readonly ILogger<WmsController> _logger;

    public WmsController(IWmsService wmsService, ILogger<WmsController> logger)
    {
        _wmsService = wmsService;
        _logger = logger;
    }

    [HttpGet("mapa")]
    [ProducesResponseType(typeof(ResumenBodegaDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMapaBodega()
    {
        var resumen = await _wmsService.ObtenerMapaBodegaAsync();
        return Ok(resumen);
    }

    [HttpGet("ubicaciones")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarUbicaciones([FromQuery] string? pasillo, [FromQuery] bool? soloDisponibles)
    {
        var ubicaciones = await _wmsService.ListarUbicacionesAsync(pasillo, soloDisponibles);
        return Ok(ubicaciones);
    }

    /// <summary>
    /// Lista saldos físicos WMS con producto y disponibilidad para transferencias operativas.
    /// </summary>
    [HttpGet("stocks")]
    [ProducesResponseType(typeof(IEnumerable<StockWmsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarStocks([FromQuery] Guid? ubicacionId, [FromQuery] string? sku)
    {
        var stocks = await _wmsService.ListarStocksAsync(ubicacionId, sku);
        return Ok(stocks);
    }

    /// <summary>
    /// Transfiere una cantidad de stock disponible entre dos ubicaciones WMS.
    /// Unidades reservadas o bloqueadas no pueden moverse.
    /// </summary>
    [HttpPost("transferir")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(TransferenciaInventarioResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TransferenciaInventarioResult), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Transferir([FromBody] TransferirStockRequest request)
    {
        var resultado = await _wmsService.TransferirStockAsync(
            request.StockFisicoId,
            request.NuevaUbicacionId,
            request.Cantidad,
            request.OperacionId,
            request.Observaciones);

        if (!resultado.Exitoso)
        {
            _logger.LogWarning(
                "Transferencia WMS rechazada. Stock={StockId}, Destino={Destino}, Cantidad={Cantidad}, Motivo={Motivo}",
                request.StockFisicoId,
                request.NuevaUbicacionId,
                request.Cantidad,
                resultado.Mensaje);
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    /// <summary>
    /// Endpoint legado para mover un saldo completo sin reservas ni bloqueos.
    /// La UI nueva utiliza /transferir.
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
            return BadRequest(new
            {
                mensaje = "No se pudo reubicar el saldo completo. Verifique IDs y confirme que no existan unidades reservadas o bloqueadas."
            });
        }

        return Ok(new { mensaje = "Stock reubicado exitosamente en el WMS." });
    }
}

public record ReubicarStockRequest(Guid StockFisicoId, Guid NuevaUbicacionId);
public record TransferirStockRequest(
    Guid StockFisicoId,
    Guid NuevaUbicacionId,
    int Cantidad,
    Guid OperacionId,
    string? Observaciones = null);
