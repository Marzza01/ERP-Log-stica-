/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador para gestión de reglas de comisión (Equipo 1 — Core).
/// Solo Admin puede gestionar comisiones.
/// </summary>
[ApiController]
[Route("api/comisiones")]
[Authorize(Roles = "Admin")]
public class ComisionesController : ControllerBase
{
    private readonly CoreDbContext _dbContext;
    private readonly ILogger<ComisionesController> _logger;

    public ComisionesController(CoreDbContext dbContext, ILogger<ComisionesController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Lista todas las reglas de comisión activas.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ReglaComision>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var reglas = await _dbContext.ReglasComision
            .Include(r => r.Vendedor)
            .Where(r => r.EsActiva)
            .ToListAsync();

        return Ok(reglas);
    }

    /// <summary>
    /// Crea una nueva regla de comisión para un vendedor.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ReglaComision), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] ReglaComision regla)
    {
        regla.Id = Guid.NewGuid();
        regla.VigenteDesde = DateTime.UtcNow;
        regla.EsActiva = true;

        _dbContext.ReglasComision.Add(regla);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "ReglaComision creada. Id={Id}, VendedorId={VendedorId}, Pct={Pct}%",
            regla.Id, regla.VendedorId, regla.PorcentajeComision);

        return Created($"/api/comisiones/{regla.Id}", regla);
    }
}
