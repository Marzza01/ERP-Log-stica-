/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Claims;
using MarketplaceERP.API.Contracts;
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
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublicados([FromQuery] int pagina = 1, [FromQuery] int tamano = 50)
    {
        var query = _dbContext.Productos
            .Include(p => p.Stocks)
            .Where(p => p.EstaPublicado);

        // El catálogo de gestión de un Vendedor solo incluye productos propios.
        // Comprador sigue viendo el catálogo publicado mediante el DTO público seguro.
        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue) return Forbid();
            query = query.Where(p => p.VendedorId == vendedorId.Value);
        }
        else if (!User.IsInRole("Comprador") && !User.IsInRole("Admin"))
            return Forbid();

        var total = await query.CountAsync();
        var productos = await query
            .OrderBy(p => p.Sku)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();

        if (User.IsInRole("Comprador"))
        {
            var compradorItems = productos.Select(ToCompradorDto).ToList();
            return Ok(new { Total = total, Pagina = pagina, Tamano = tamano, Items = compradorItems });
        }

        return Ok(new { Total = total, Pagina = pagina, Tamano = tamano, Items = productos });
    }

    /// <summary>
    /// Búsqueda y filtrado del catálogo por texto, categoría, subcategoría, rango de precio y stock.
    /// </summary>
    [HttpGet("buscar")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
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
            .Include(p => p.Stocks)
            .Where(p => p.EstaPublicado);

        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue) return Forbid();
            query = query.Where(p => p.VendedorId == vendedorId.Value);
        }
        else if (!User.IsInRole("Comprador") && !User.IsInRole("Admin"))
            return Forbid();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var qLower = q.ToLower();
            query = query.Where(p => p.Nombre.ToLower().Contains(qLower) || 
                                     p.Sku.ToLower().Contains(qLower) || 
                                     p.Descripcion.ToLower().Contains(qLower) ||
                                     (p.CodigoEAN != null && p.CodigoEAN.Contains(qLower)));
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
            query = query.Where(p => p.Stocks.Any(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada > 0));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.Sku)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();

        if (User.IsInRole("Comprador"))
        {
            var compradorItems = items.Select(ToCompradorDto).ToList();
            return Ok(new { Total = total, Pagina = pagina, Tamano = tamano, Items = compradorItems });
        }

        return Ok(new { Total = total, Pagina = pagina, Tamano = tamano, Items = items });
    }

    /// <summary>
    /// Catálogo especializado para clientes B2B con precios por volumen (PDF 2 - Sección 2 y 4).
    /// </summary>
    [HttpGet("b2b")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalogoB2B([FromQuery] string? categoria)
    {
        var query = _dbContext.Productos
            .Include(p => p.Stocks)
            .Where(p => p.EstaPublicado && p.PrecioB2B > 0);

        // Precios mayoristas, proveedores y unidades físicas son datos privados.
        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue) return Forbid();
            query = query.Where(p => p.VendedorId == vendedorId.Value);
        }
        else if (!User.IsInRole("Admin"))
            return Forbid();

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
                StockDisponible = p.Stocks.Sum(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada)
            })
            .ToListAsync();

        return Ok(productos);
    }

    /// <summary>
    /// Alertas logísticas de inventario: Productos en o bajo el punto de reorden / stock mínimo.
    /// </summary>
    [HttpGet("alertas-stock")]
    [HttpGet("alertas-reorden")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlertasStock()
    {
        var query = _dbContext.Productos.AsQueryable();

        if (!User.IsInRole("Admin"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue)
                return Forbid();

            query = query.Where(p => p.VendedorId == vendedorId.Value);
        }

        var productos = await query
            .Where(p => p.Stocks.Any() &&
                        p.Stocks.Sum(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada) <= p.StockMinimo)
            .OrderBy(p => p.Stocks.Sum(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada))
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Nombre,
                p.CategoriaNombre,
                p.ProveedorNombre,
                p.StockMinimo,
                PuntoReorden = p.StockMinimo,
                CantidadDisponible = p.Stocks.Sum(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada),
                NivelCriticidad = p.Stocks.Sum(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada) <= 0
                    ? "CRÍTICO - AGOTADO"
                    : "ATENCIÓN - BAJO MÍNIMO",
                UbicacionWMS = p.Stocks
                    .OrderByDescending(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada)
                    .Select(s => s.UbicacionCodigo)
                    .FirstOrDefault() ?? "SIN UBICACIÓN"
            })
            .ToListAsync();

        return Ok(new { TotalAlertas = productos.Count, Alertas = productos });
    }

    /// <summary>
    /// Carga masiva de los 500 productos mediante archivo CSV multipart (PDF 2 - Sección 3).
    /// </summary>
    [HttpPost("importar-csv")]
    [Authorize(Roles = "Admin")]
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
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var producto = await _dbContext.Productos
            .Include(p => p.Stocks)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (producto is null)
            return NotFound(new { mensaje = $"Producto {id} no encontrado." });

        if (User.IsInRole("Comprador"))
        {
            if (!producto.EstaPublicado)
                return NotFound(new { mensaje = $"Producto {id} no encontrado." });

            return Ok(ToCompradorDto(producto));
        }

        if (User.IsInRole("Admin"))
            return Ok(producto);

        if (User.IsInRole("Vendedor"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue) return Forbid();

            // No revelar detalles privados ni borradores de vendedores ajenos.
            if (producto.VendedorId != vendedorId.Value)
                return NotFound(new { mensaje = $"Producto {id} no encontrado." });

            return Ok(producto);
        }

        return Forbid();
    }

    /// <summary>
    /// Publica un nuevo producto. Requiere rol Vendedor o Admin.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(Producto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Producto producto)
    {
        if (producto is null)
            return BadRequest(new { mensaje = "El producto no puede ser nulo." });

        var vendedorId = producto.VendedorId;
        if (!User.IsInRole("Admin"))
        {
            var vendedorAutenticado = ObtenerVendedorIdActual();
            if (!vendedorAutenticado.HasValue)
                return Forbid();

            // La propiedad de la mercancía se deriva de la identidad autenticada.
            if (vendedorId != Guid.Empty && vendedorId != vendedorAutenticado.Value)
                return Forbid();

            vendedorId = vendedorAutenticado.Value;
        }

        // Lista blanca de campos maestros. Producto es una entidad EF con
        // navegaciones públicas (Stock, Reservas, Movimientos, etc.) que pueden
        // llegar por JSON; nunca insertar el grafo recibido con DbContext.Add.
        // El inventario físico se modifica exclusivamente por los flujos WMS.
        var ahora = DateTime.UtcNow;
        var nuevoProducto = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = producto.Sku,
            Nombre = producto.Nombre,
            Descripcion = producto.Descripcion,
            CategoriaNombre = producto.CategoriaNombre,
            Subcategoria = producto.Subcategoria,
            PrecioB2C = producto.PrecioB2C,
            PrecioB2B = producto.PrecioB2B,
            Moneda = producto.Moneda,
            StockInicial = producto.StockInicial, // dato maestro; NO crea StockFisico
            StockMinimo = producto.StockMinimo,
            ProveedorNombre = producto.ProveedorNombre,
            PaisOrigen = producto.PaisOrigen,
            PesoKg = producto.PesoKg,
            DimensionesCm = producto.DimensionesCm,
            UnidadMedida = producto.UnidadMedida,
            EstadoProducto = producto.EstadoProducto,
            CodigoEAN = producto.CodigoEAN,
            VendedorId = vendedorId,
            CategoriaId = producto.CategoriaId,
            EstaPublicado = producto.EstaPublicado,
            FechaCreacion = ahora,
            FechaPublicacion = producto.EstaPublicado ? ahora : null
        };

        _dbContext.Productos.Add(nuevoProducto);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Producto creado. Id={Id}, SKU={Sku}, Nombre={Nombre}, VendedorId={VendedorId}",
            nuevoProducto.Id, nuevoProducto.Sku, nuevoProducto.Nombre, nuevoProducto.VendedorId);

        return CreatedAtAction(nameof(GetById), new { id = nuevoProducto.Id }, nuevoProducto);
    }

    /// <summary>
    /// Actualiza los datos maestros operativos de un producto.
    /// El Código EAN se conserva sin cambios para permitir que la funcionalidad de códigos de barras
    /// sea implementada posteriormente por el integrante responsable.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(Producto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ActualizarProductoRequest request)
    {
        var producto = await _dbContext.Productos
            .Include(p => p.Stocks)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (producto is null)
            return NotFound(new { mensaje = $"Producto {id} no encontrado." });

        if (!User.IsInRole("Admin"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue || producto.VendedorId != vendedorId.Value)
                return Forbid();
        }

        producto.Sku = request.Sku;
        producto.Nombre = request.Nombre;
        producto.Descripcion = request.Descripcion;
        producto.CategoriaNombre = request.CategoriaNombre;
        producto.Subcategoria = request.Subcategoria;
        producto.PrecioB2C = request.PrecioB2C;
        producto.PrecioB2B = request.PrecioB2B;
        producto.StockMinimo = request.StockMinimo;
        producto.ProveedorNombre = request.ProveedorNombre;
        producto.PaisOrigen = request.PaisOrigen;
        producto.PesoKg = request.PesoKg;
        producto.DimensionesCm = request.DimensionesCm;
        producto.UnidadMedida = request.UnidadMedida;
        producto.EstadoProducto = request.EstadoProducto;
        producto.EstaPublicado = request.EstaPublicado;
        producto.FechaPublicacion = request.EstaPublicado
            ? producto.FechaPublicacion ?? DateTime.UtcNow
            : null;

        await _dbContext.SaveChangesAsync();
        return Ok(producto);
    }

    /// <summary>
    /// Da de baja lógica un producto sin eliminar su trazabilidad histórica.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var producto = await _dbContext.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = $"Producto {id} no encontrado." });

        if (!User.IsInRole("Admin"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue || producto.VendedorId != vendedorId.Value)
                return Forbid();
        }

        producto.EstaPublicado = false;
        producto.EstadoProducto = "Inactivo";
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Publica un producto (cambia EstaPublicado a true). Requiere rol Vendedor o Admin.
    /// </summary>
    [HttpPatch("{id:guid}/publicar")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publicar(Guid id)
    {
        var producto = await _dbContext.Productos.FindAsync(id);
        if (producto is null)
            return NotFound(new { mensaje = $"Producto {id} no encontrado." });

        if (!User.IsInRole("Admin"))
        {
            var vendedorId = ObtenerVendedorIdActual();
            if (!vendedorId.HasValue || producto.VendedorId != vendedorId.Value)
                return Forbid();
        }

        producto.EstaPublicado = true;
        producto.FechaPublicacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    private Guid? ObtenerVendedorIdActual()
    {
        var claims = User.FindAll("VendedorId").Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        if (claims.Length == 0) return null;
        if (claims.Any(c => !Guid.TryParse(c, out var id) || id == Guid.Empty)) return null;
        var distinct = claims.Select(Guid.Parse).Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static ProductoCompradorResponse ToCompradorDto(Producto p)
    {
        bool disponible = p.Stocks.Any(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada > 0);
        return new ProductoCompradorResponse(
            p.Id,
            p.Sku,
            p.Nombre,
            p.Descripcion,
            p.CategoriaNombre,
            p.Subcategoria,
            p.PrecioB2C,
            p.Moneda,
            p.UnidadMedida,
            p.EstadoProducto,
            p.EstaPublicado,
            null,
            disponible
        );
    }
}


public record ActualizarProductoRequest(
    string Sku,
    string Nombre,
    string Descripcion,
    string CategoriaNombre,
    string Subcategoria,
    decimal PrecioB2C,
    decimal PrecioB2B,
    int StockMinimo,
    string ProveedorNombre,
    string PaisOrigen,
    decimal PesoKg,
    string DimensionesCm,
    string UnidadMedida,
    string EstadoProducto,
    bool EstaPublicado);
