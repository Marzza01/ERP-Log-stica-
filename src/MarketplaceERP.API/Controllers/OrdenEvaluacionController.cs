using System.Security.Claims;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/ordenes/evaluacion")]
public sealed class OrdenEvaluacionController(
    OrdenEvaluacionService orders, CoreDbContext core, CatalogDbContext catalog,
    ILogger<OrdenEvaluacionController> logger) : ControllerBase
{
    [HttpGet("opciones")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { orders.ValidateTarget(); }
        catch (EvaluacionOrdenException ex) { return StatusCode(ex.StatusCode, new { mensaje = ex.Message }); }
        return Ok(new
        {
            compradores = await core.Compradores.AsNoTracking().Where(b => b.EstaActivo)
                .OrderBy(b => b.NombreCompleto).Take(100)
                .Select(b => new { b.Id, nombre = b.NombreCompleto }).ToListAsync(ct),
            vendedores = await core.Vendedores.AsNoTracking().Where(s => s.EstaActivo)
                .OrderBy(s => s.NombreEmpresa).Take(100)
                .Select(s => new { s.Id, nombre = s.NombreEmpresa }).ToListAsync(ct),
            productos = await catalog.Productos.AsNoTracking()
                .Where(p => p.EstaPublicado && p.EstadoProducto == "Activo" && p.Moneda == "USD")
                .OrderBy(p => p.Sku).Take(500)
                .Select(p => new { p.Id, p.VendedorId, p.Sku, p.Nombre, p.PrecioB2C, p.PrecioB2B })
                .ToListAsync(ct)
        });
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
