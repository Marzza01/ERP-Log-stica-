using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador para gestión de productos del catálogo (Equipo 2 — Catalog).
/// </summary>
[ApiController]
[Route("api/productos")]
[Authorize]
public class ProductosController : ControllerBase
{
    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<ProductosController> _logger;

    public ProductosController(CatalogDbContext dbContext, ILogger<ProductosController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Lista todos los productos publicados (accesible para compradores y vendedores).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Producto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublicados()
    {
        var productos = await _dbContext.Productos
            .Include(p => p.Stock)
            .Where(p => p.EstaPublicado)
            .OrderByDescending(p => p.FechaPublicacion)
            .ToListAsync();

        return Ok(productos);
    }

    /// <summary>
    /// Obtiene un producto por su ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Producto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var producto = await _dbContext.Productos
            .Include(p => p.Stock)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (producto is null)
            return NotFound(new { mensaje = $"Producto {id} no encontrado." });

        return Ok(producto);
    }

    /// <summary>
    /// Publica un nuevo producto. Requiere rol Vendedor o Admin.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Vendedor,Admin")]
    [ProducesResponseType(typeof(Producto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Producto producto)
    {
        producto.Id = Guid.NewGuid();
        producto.FechaCreacion = DateTime.UtcNow;

        if (producto.EstaPublicado)
            producto.FechaPublicacion = DateTime.UtcNow;

        _dbContext.Productos.Add(producto);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Producto creado. Id={Id}, Nombre={Nombre}, VendedorId={VendedorId}",
            producto.Id, producto.Nombre, producto.VendedorId);

        return CreatedAtAction(nameof(GetById), new { id = producto.Id }, producto);
    }

    /// <summary>
    /// Publica un producto (cambia EstaPublicado a true). Requiere rol Vendedor o Admin.
    /// </summary>
    [HttpPatch("{id:guid}/publicar")]
    [Authorize(Roles = "Vendedor,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publicar(Guid id)
    {
        var producto = await _dbContext.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = $"Producto {id} no encontrado." });

        producto.EstaPublicado = true;
        producto.FechaPublicacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }
}
