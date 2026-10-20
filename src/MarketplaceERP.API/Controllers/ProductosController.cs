/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador para gestión de productos del catálogo (Célula A — Catalog).
/// Incluye carga masiva de los 500 SKUs, filtros multi-criterio, vistas B2B/B2C y alertas de reorden.
/// </summary>
[ApiController]
[Route("api/productos")]
[Authorize]
public class ProductosController : ControllerBase
{
    private readonly CatalogDbContext _dbContext;
    private readonly ICatalogImportService _importService;
    private readonly ILogger<ProductosController> _logger;

    public ProductosController(
        CatalogDbContext dbContext,
        ICatalogImportService importService,
        ILogger<ProductosController> logger)
    {
        _dbContext = dbContext;
        _importService = importService;
        _logger = logger;
    }

    /// <summary>
    /// Lista todos los productos publicados (con soporte de paginación básica).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Producto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublicados([FromQuery] int pagina = 1, [FromQuery] int tamano = 50)
    {
        var query = _dbContext.Productos
            .Include(p => p.Stock)
            .Where(p => p.EstaPublicado)
            .AsQueryable();

        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("VendedorId")?.Value;

            if (Guid.TryParse(User.FindFirst("VendedorId")?.Value, out var vendedorId))
            {
                query = query.Where(p => p.VendedorId == vendedorId);
            }
            else
            {
                return Ok(new { Total = 0, Pagina = pagina, Tamano = tamano, Items = Array.Empty<Producto>() });
            }
        }

        query = query.OrderBy(p => p.Sku);

        var total = await query.CountAsync();
        var productos = await query
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();

        return Ok(new { Total = total, Pagina = pagina, Tamano = tamano, Items = productos });
    }

    /// <summary>
    /// Búsqueda y filtrado del catálogo por texto, categoría, subcategoría, rango de precio y stock.
    /// </summary>
    [HttpGet("buscar")]
    [ProducesResponseType(typeof(IEnumerable<Producto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Buscar(
        [FromQuery] string? q,
        [FromQuery] string? categoria,
        [FromQuery] string? subcategoria,
        [FromQuery] decimal? precioMin,
        [FromQuery] decimal? precioMax,
        [FromQuery] bool? soloConStock,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50)
    {
        var query = _dbContext.Productos
            .Include(p => p.Stock)
            .Where(p => p.EstaPublicado);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var qLower = q.ToLower();
            query = query.Where(p => p.Nombre.ToLower().Contains(qLower) || 
                                     p.Sku.ToLower().Contains(qLower) || 
                                     p.Descripcion.ToLower().Contains(qLower) ||
                                     p.CodigoEAN.Contains(qLower));
        }

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            query = query.Where(p => p.CategoriaNombre == categoria);
        }

        if (!string.IsNullOrWhiteSpace(subcategoria))
        {
            query = query.Where(p => p.Subcategoria == subcategoria);
        }

        if (precioMin.HasValue)
        {
            query = query.Where(p => p.PrecioB2C >= precioMin.Value);
        }

        if (precioMax.HasValue)
        {
            query = query.Where(p => p.PrecioB2C <= precioMax.Value);
        }

        if (soloConStock == true)
        {
            query = query.Where(p => p.Stock != null && p.Stock.CantidadTotal - p.Stock.CantidadReservada > 0);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Sku)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();

        return Ok(new { Total = total, Pagina = pagina, Tamano = tamano, Items = items });
    }

    /// <summary>
    /// Catálogo especializado para clientes B2B con precios por volumen (PDF 2 - Sección 2 y 4).
    /// </summary>
    [HttpGet("b2b")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalogoB2B([FromQuery] string? categoria)
    {
        var query = _dbContext.Productos
            .Include(p => p.Stock)
            .Where(p => p.EstaPublicado && p.PrecioB2B > 0);

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            query = query.Where(p => p.CategoriaNombre == categoria);
        }

        var productos = await query
            .OrderBy(p => p.Sku)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Nombre,
                p.CategoriaNombre,
                p.Subcategoria,
                p.PrecioB2C,
                p.PrecioB2B,
                AhorroB2B = p.PrecioB2C - p.PrecioB2B,
                PorcentajeDescuento = p.PrecioB2C > 0 ? Math.Round(((p.PrecioB2C - p.PrecioB2B) / p.PrecioB2C) * 100m, 1) : 0m,
                p.UnidadMedida,
                p.ProveedorNombre,
                p.CodigoEAN,
                StockDisponible = p.Stock != null ? p.Stock.CantidadTotal - p.Stock.CantidadReservada : 0
            })
            .ToListAsync();

        return Ok(productos);
    }

    /// <summary>
    /// Alertas logísticas de inventario: Productos en o bajo el punto de reorden / stock mínimo.
    /// </summary>
    [HttpGet("alertas-stock")]
    [HttpGet("alertas-reorden")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlertasStock()
    {
        var productos = await _dbContext.Productos
            .Include(p => p.Stock)
            .Where(p => p.Stock != null && (p.Stock.CantidadTotal - p.Stock.CantidadReservada) <= p.StockMinimo)
            .OrderBy(p => p.Stock!.CantidadTotal - p.Stock!.CantidadReservada)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Nombre,
                p.CategoriaNombre,
                p.ProveedorNombre,
                p.StockMinimo,
                PuntoReorden = p.Stock != null ? p.Stock.PuntoReorden : p.StockMinimo,
                CantidadDisponible = p.Stock != null ? p.Stock.CantidadTotal - p.Stock.CantidadReservada : 0,
                NivelCriticidad = (p.Stock != null && p.Stock.CantidadTotal - p.Stock.CantidadReservada <= 0) ? "CRÍTICO - AGOTADO" : "ATENCIÓN - BAJO MÍNIMO",
                UbicacionWMS = p.Stock != null ? p.Stock.UbicacionCodigo : "SIN UBICACIÓN"
            })
            .ToListAsync();

        return Ok(new { TotalAlertas = productos.Count, Alertas = productos });
    }

    /// <summary>
    /// Carga masiva de los 500 productos mediante archivo CSV multipart (PDF 2 - Sección 3).
    /// </summary>
    [HttpPost("importar-csv")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(ImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportarCsv(IFormFile archivo)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Debe proporcionar un archivo CSV válido." });

        // Vendedor y Categoría por defecto para la carga masiva de catálogo
        var defaultVendedorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var defaultCategoriaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        using var stream = archivo.OpenReadStream();
        var resultado = await _importService.ImportarCatalogoCsvAsync(stream, defaultVendedorId, defaultCategoriaId);

        return Ok(resultado);
    }

    /// <summary>
    /// Inicializa la base de datos con los 500 SKUs oficiales desde el archivo preinstalado en el servidor.
    /// </summary>
    [HttpPost("seed-500")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ImportResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Seed500()
    {
        var defaultVendedorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var defaultCategoriaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var posiblesRutas = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "SeedData", "Catalogo_500_Productos.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "SeedData", "Catalogo_500_Productos.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "MarketplaceERP.Catalog", "SeedData", "Catalogo_500_Productos.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "docs", "data", "Catalogo_500_Productos.csv")
        };

        string? rutaEncontrada = posiblesRutas.FirstOrDefault(System.IO.File.Exists);
        if (rutaEncontrada is null)
        {
            return NotFound(new { mensaje = "No se encontró el archivo Catalogo_500_Productos.csv en el servidor." });
        }

        var resultado = await _importService.ImportarCatalogoDesdeArchivoAsync(rutaEncontrada, defaultVendedorId, defaultCategoriaId);
        return Ok(resultado);
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
        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorIdClaim = User.FindFirst("VendedorId")?.Value;
            if (Guid.TryParse(vendedorIdClaim, out var claimVendedorId))
            {
                producto.VendedorId = claimVendedorId;
            }
            else
            {
                return Forbid();
            }
        }

        producto.Id = Guid.NewGuid();
        producto.FechaCreacion = DateTime.UtcNow;

        if (producto.EstaPublicado)
            producto.FechaPublicacion = DateTime.UtcNow;

        _dbContext.Productos.Add(producto);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Producto creado. Id={Id}, SKU={Sku}, Nombre={Nombre}, VendedorId={VendedorId}",
            producto.Id, producto.Sku, producto.Nombre, producto.VendedorId);

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

        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorIdClaim = User.FindFirst("VendedorId")?.Value;
            if (Guid.TryParse(vendedorIdClaim, out var claimVendedorId))
            {
                if (producto.VendedorId != claimVendedorId)
                {
                    return NotFound(new { mensaje = $"Producto {id} no encontrado." });
                }
            }
            else
            {
                return NotFound(new { mensaje = $"Producto {id} no encontrado." });
            }
        }

        producto.EstaPublicado = true;
        producto.FechaPublicacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }
}
