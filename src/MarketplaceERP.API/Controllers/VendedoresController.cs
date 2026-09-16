using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador para gestión de vendedores (Equipo 1 — Core).
/// </summary>
[ApiController]
[Route("api/vendedores")]
[Authorize]
public class VendedoresController : ControllerBase
{
    private readonly CoreDbContext _dbContext;
    private readonly ILogger<VendedoresController> _logger;

    public VendedoresController(CoreDbContext dbContext, ILogger<VendedoresController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Lista todos los vendedores activos.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(IEnumerable<Vendedor>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var vendedores = await _dbContext.Vendedores
            .Where(v => v.EstaActivo)
            .OrderBy(v => v.NombreEmpresa)
            .ToListAsync();

        return Ok(vendedores);
    }

    /// <summary>
    /// Obtiene un vendedor por su ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(Vendedor), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var vendedor = await _dbContext.Vendedores.FindAsync(id);
        if (vendedor is null)
            return NotFound(new { mensaje = $"Vendedor {id} no encontrado." });

        return Ok(vendedor);
    }

    /// <summary>
    /// Registra un nuevo vendedor.
    /// Requiere rol Admin.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(Vendedor), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Vendedor vendedor)
    {
        vendedor.Id = Guid.NewGuid();
        vendedor.FechaRegistro = DateTime.UtcNow;
        vendedor.EstaActivo = true;

        _dbContext.Vendedores.Add(vendedor);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Vendedor creado. Id={Id}, Empresa={Empresa}", vendedor.Id, vendedor.NombreEmpresa);
        return CreatedAtAction(nameof(GetById), new { id = vendedor.Id }, vendedor);
    }
}
