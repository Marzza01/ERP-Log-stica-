using System.Globalization;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

public class CatalogImportService : ICatalogImportService
{
    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<CatalogImportService> _logger;

    public CatalogImportService(CatalogDbContext dbContext, ILogger<CatalogImportService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ImportResult> ImportarCatalogoDesdeArchivoAsync(
        string rutaArchivo, Guid defaultVendedorId, Guid defaultCategoriaId, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(rutaArchivo))
        {
            return new ImportResult(0, 0, 1, new List<string> { $"El archivo '{rutaArchivo}' no existe." });
        }

        using var fileStream = File.OpenRead(rutaArchivo);
        return await ImportarCatalogoCsvAsync(fileStream, defaultVendedorId, defaultCategoriaId, cancellationToken);
    }

    public async Task<ImportResult> ImportarCatalogoCsvAsync(
        Stream csvStream, Guid defaultVendedorId, Guid defaultCategoriaId, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csvStream, System.Text.Encoding.UTF8);
        var errores = new List<string>();
        int exitosos = 0;
        int total = 0;

        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
        {
            return new ImportResult(0, 0, 1, new List<string> { "El archivo CSV está vacío." });
        }

        int fila = 1;
        while (!reader.EndOfStream)
        {
            fila++;
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            total++;
            try
            {
                var fields = ParseCsvLine(line);
                if (fields.Count < 16)
                {
                    errores.Add($"Fila {fila}: Se esperaban 16 columnas pero se obtuvieron {fields.Count}.");
                    continue;
                }

                // 16 columnas oficiales:
                // 0: SKU, 1: Nombre, 2: Categoria, 3: Subcategoria, 4: Precio B2C, 5: Precio B2B,
                // 6: Stock Inicial, 7: Stock Minimo, 8: Proveedor, 9: Pais, 10: Peso,
                // 11: Dimensiones, 12: Unidad, 13: Estado, 14: EAN, 15: Descripcion
                string sku = fields[0].Trim();
                string nombre = fields[1].Trim();
                string categoria = fields[2].Trim();
                string subcategoria = fields[3].Trim();
                decimal precioB2C = ParseDecimal(fields[4], 10.0m);
                decimal precioB2B = ParseDecimal(fields[5], precioB2C * 0.8m);
                int stockInicial = ParseInt(fields[6], 50);
                int stockMinimo = ParseInt(fields[7], 15);
                string proveedor = fields[8].Trim();
                string paisOrigen = fields[9].Trim();
                decimal pesoKg = ParseDecimal(fields[10], 1.0m);
                string dimensiones = fields[11].Trim();
                string unidad = fields[12].Trim();
                string estado = fields[13].Trim();
                string ean = fields[14].Trim();
                string descripcion = fields[15].Trim();

                // Ubicación WMS según categoría y SKU
                string pasilloLetra = ObtenerPasilloPorCategoria(categoria);
                int skuNum = total;
                int rackNum = ((skuNum - 1) % 10) + 1;
                int nivelNum = (((skuNum - 1) / 10) % 5) + 1;
                int posNum = (((skuNum - 1) / 50) % 10) + 1;
                string wmsCodigo = $"PAS-{pasilloLetra}-RCK-{rackNum:D2}-NIV-{nivelNum:D2}-POS-{posNum:D2}";

                // Buscar o crear la ubicación de almacén WMS
                var ubicacion = await _dbContext.UbicacionesAlmacen
                    .FirstOrDefaultAsync(u => u.Codigo == wmsCodigo, cancellationToken);

                if (ubicacion is null)
                {
                    ubicacion = new UbicacionAlmacen
                    {
                        Id = Guid.NewGuid(),
                        Codigo = wmsCodigo,
                        Pasillo = $"Pasillo {pasilloLetra}",
                        Rack = $"Rack {rackNum:D2}",
                        Nivel = nivelNum,
                        Posicion = $"Posición {posNum:D2}",
                        CapacidadMaxima = 1500,
                        EstaOcupada = true,
                        Zona = "AlmacenamientoGeneral",
                        FechaCreacion = DateTime.UtcNow
                    };
                    _dbContext.UbicacionesAlmacen.Add(ubicacion);
                }
                else
                {
                    ubicacion.EstaOcupada = true;
                }

                // Verificar si el producto ya existe por SKU
                var producto = await _dbContext.Productos
                    .Include(p => p.Stock)
                    .FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);

                if (producto is null)
                {
                    producto = new Producto
                    {
                        Id = Guid.NewGuid(),
                        Sku = sku,
                        Nombre = nombre,
                        CategoriaNombre = categoria,
                        Subcategoria = subcategoria,
                        PrecioB2C = precioB2C,
                        PrecioB2B = precioB2B,
                        StockInicial = stockInicial,
                        StockMinimo = stockMinimo,
                        ProveedorNombre = proveedor,
                        PaisOrigen = paisOrigen,
                        PesoKg = pesoKg,
                        DimensionesCm = dimensiones,
                        UnidadMedida = unidad,
                        EstadoProducto = estado,
                        CodigoEAN = ean,
                        Descripcion = descripcion,
                        VendedorId = defaultVendedorId,
                        CategoriaId = defaultCategoriaId,
                        EstaPublicado = (estado == "Activo"),
                        FechaCreacion = DateTime.UtcNow,
                        FechaPublicacion = (estado == "Activo") ? DateTime.UtcNow : null
                    };

                    producto.Stock = new StockFisico
                    {
                        Id = Guid.NewGuid(),
                        ProductoId = producto.Id,
                        CantidadTotal = stockInicial,
                        CantidadReservada = 0,
                        PuntoReorden = stockMinimo,
                        UbicacionAlmacenId = ubicacion.Id,
                        UbicacionCodigo = wmsCodigo,
                        UltimaActualizacion = DateTime.UtcNow
                    };

                    _dbContext.Productos.Add(producto);
                }
                else
                {
                    // Actualizar campos
                    producto.Nombre = nombre;
                    producto.CategoriaNombre = categoria;
                    producto.Subcategoria = subcategoria;
                    producto.PrecioB2C = precioB2C;
                    producto.PrecioB2B = precioB2B;
                    producto.StockMinimo = stockMinimo;
                    producto.ProveedorNombre = proveedor;
                    producto.PaisOrigen = paisOrigen;
                    producto.PesoKg = pesoKg;
                    producto.DimensionesCm = dimensiones;
                    producto.UnidadMedida = unidad;
                    producto.EstadoProducto = estado;
                    producto.CodigoEAN = ean;
                    producto.Descripcion = descripcion;
                    producto.EstaPublicado = (estado == "Activo");

                    if (producto.Stock is not null)
                    {
                        producto.Stock.PuntoReorden = stockMinimo;
                        producto.Stock.UbicacionAlmacenId = ubicacion.Id;
                        producto.Stock.UbicacionCodigo = wmsCodigo;
                    }
                }

                exitosos++;
            }
            catch (Exception ex)
            {
                errores.Add($"Fila {fila}: Error procesando - {ex.Message}");
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Importación de catálogo finalizada. Total={Total}, Exitosos={Exitosos}, Errores={Errores}",
            total, exitosos, errores.Count);

        return new ImportResult(total, exitosos, errores.Count, errores);
    }

    private static string ObtenerPasilloPorCategoria(string categoria) => categoria switch
    {
        "Electrónica y Tecnología" => "A",
        "Ropa y Accesorios" => "B",
        "Alimentos y Bebidas" => "C",
        "Ferretería e Industria" => "D",
        "Hogar y Decoración" => "E",
        "Salud y Cuidado Personal" => "F",
        _ => "Z"
    };

    private static decimal ParseDecimal(string value, decimal fallback)
    {
        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;
        if (decimal.TryParse(value, NumberStyles.Any, new CultureInfo("es-SV"), out result))
            return result;
        return fallback;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out int result) ? result : fallback;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var currentField = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++; // Saltar comilla escapada
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        result.Add(currentField.ToString());
        return result;
    }
}
