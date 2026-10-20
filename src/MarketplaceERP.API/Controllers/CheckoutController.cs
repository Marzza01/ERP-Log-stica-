/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Request para iniciar el proceso de checkout.
/// </summary>
public record CheckoutRequest(
    Guid VendedorId,
    Guid ProductoId,
    int Cantidad,
    decimal PrecioUnitario,
    string Moneda = "USD"
);

/// <summary>
/// Controlador del flujo transaccional de checkout y consulta de órdenes (Equipo 3 — Finance).
/// </summary>
[ApiController]
[Route("api/checkout")]
public class CheckoutController : ControllerBase
{
    private readonly CheckoutOrchestrator _orchestrator;
    private readonly FinanceDbContext _dbContext;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        CheckoutOrchestrator orchestrator,
        FinanceDbContext dbContext,
        ILogger<CheckoutController> logger)
    {
        _orchestrator = orchestrator;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Inicia el proceso de checkout (Saga).
    /// Solo los compradores pueden realizar compras.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Comprador")]
    [ProducesResponseType(typeof(CheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProcesarCheckout([FromBody] CheckoutRequest request)
    {
        // Extraer CompradorId del claim JWT NameIdentifier (Guid)
        var compradorIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(compradorIdClaim, out var compradorId))
        {
            return Unauthorized(new { mensaje = "Identificador de comprador inválido en el token." });
        }

        var ordenId = Guid.NewGuid();
        var montoTotal = request.Cantidad * request.PrecioUnitario;

        // Crear la orden en Finance primero
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"CHK-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(10000, 99999)}",
            CompradorId = compradorId,
            VendedorId = request.VendedorId,
            MontoTotal = montoTotal,
            Moneda = request.Moneda,
            Estado = EstadoOrden.Pendiente,
            FechaCreacion = DateTime.UtcNow,
            Items = new List<ItemOrden>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    OrdenId = ordenId,
                    ProductoId = request.ProductoId,
                    Cantidad = request.Cantidad,
                    PrecioUnitario = request.PrecioUnitario
                }
            }
        };

        _dbContext.Ordenes.Add(orden);
        await _dbContext.SaveChangesAsync();

        try
        {
            var command = new CheckoutCommand(
                OrdenId: ordenId,
                CompradorId: compradorId,
                VendedorId: request.VendedorId,
                ProductoId: request.ProductoId,
                Cantidad: request.Cantidad,
                MontoTotal: montoTotal,
                Moneda: request.Moneda);

            var resultado = await _orchestrator.ProcesarCheckoutAsync(command);

            // Actualizar estado de la orden
            orden.Estado = EstadoOrden.Entregada;
            orden.FechaEntregaReal = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return Ok(resultado);
        }
        catch (StockInsuficienteException ex)
        {
            orden.Estado = EstadoOrden.Cancelada;
            await _dbContext.SaveChangesAsync();

            _logger.LogWarning(ex, "Checkout fallido por stock insuficiente. OrdenId={OrdenId}", ordenId);
            return Conflict(new { mensaje = ex.Message, tipo = "StockInsuficiente" });
        }
        catch (PagoFallidoException ex)
        {
            orden.Estado = EstadoOrden.Cancelada;
            await _dbContext.SaveChangesAsync();

            _logger.LogWarning(ex, "Checkout fallido por pago rechazado. OrdenId={OrdenId}", ordenId);
            return Conflict(new { mensaje = ex.Message, tipo = "PagoFallido" });
        }
    }

    /// <summary>
    /// Lista las órdenes del sistema. Solo Admin puede ver todas.
    /// </summary>
    [HttpGet("ordenes")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<Orden>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrdenes()
    {
        var ordenes = await _dbContext.Ordenes
            .Include(o => o.Items)
            .Include(o => o.SplitPago)
            .OrderByDescending(o => o.FechaCreacion)
            .Take(100)
            .ToListAsync();

        return Ok(ordenes);
    }
}
