/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Globalization;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

public class CatalogImportService : ICatalogImportService
{
    // El contrato CSV es posicional: rechazar encabezados reordenados evita
    // interpretar precios, cantidades y proveedores como otras columnas.
    private static readonly string[] ColumnasEsperadas =
    {
        "SKU", "Nombre del Producto", "Categoría", "Subcategoría",
        "Precio B2C ($)", "Precio B2B ($)", "Stock Inicial", "Stock Mínimo",
        "Proveedor", "País Origen", "Peso (kg)", "Dimensiones (cm)",
        "Unidad de Medida", "Estado", "Código EAN", "Descripción Corta"
    };

    // Acotar la memoria al validar duplicados antes de tocar el DbContext.
    private const int MaxFilasImportacion = 10000;
    private const int MaxCaracteresImportacion = 8000000;

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
        // La primera pasada es SOLO validación y no rastrea ni modifica entidades.
        // Soportar streams no-seekable: copiar con límite a un buffer temporal en memoria.
        const int maxBytesImportacion = 8_000_000;
        using var copia = new MemoryStream();
        var buffer = new byte[81920];
        int leidos;
        while ((leidos = await csvStream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (copia.Length > maxBytesImportacion - leidos)
                return new ImportResult(0, 0, 1, new List<string> { "CSV excede 8 MB para importación segura." });
            await copia.WriteAsync(buffer.AsMemory(0, leidos), cancellationToken);
        }

        copia.Position = 0;
        var validacion = await ProcesarCatalogoCsvAsync(
            copia, Guid.Empty, Guid.Empty, soloValidar: true, cancellationToken);
        if (validacion.Errores > 0)
        {
            // No guardar filas válidas de un archivo que contiene filas inválidas.
            return new ImportResult(
                validacion.TotalProcesados, 0, validacion.Errores, validacion.MensajesError);
        }

        copia.Position = 0;
        return await ProcesarCatalogoCsvAsync(
            copia, defaultVendedorId, defaultCategoriaId, soloValidar: false, cancellationToken);
    }

    public Task<ImportResult> ValidarCatalogoCsvAsync(
        Stream csvStream, CancellationToken cancellationToken = default) =>
        ProcesarCatalogoCsvAsync(csvStream, Guid.Empty, Guid.Empty, soloValidar: true, cancellationToken);

    private async Task<ImportResult> ProcesarCatalogoCsvAsync(
        Stream csvStream, Guid defaultVendedorId, Guid defaultCategoriaId,
        bool soloValidar, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csvStream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var errores = new List<string>();
        int exitosos = 0;
        int total = 0;

        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
        {
            return new ImportResult(0, 0, 1, new List<string> { "El archivo CSV está vacío." });
        }

        var columnas = ParseCsvLine(headerLine);
        if (columnas.Count != ColumnasEsperadas.Length ||
            columnas.Where((valor, indice) =>
                !string.Equals(valor.Trim().TrimStart('\uFEFF'), ColumnasEsperadas[indice],
                    StringComparison.OrdinalIgnoreCase)).Any())
        {
            return new ImportResult(0, 0, 1, new List<string>
            {
                "Encabezado CSV inválido: se requieren las 16 columnas oficiales en su orden original."
            });
        }

        // Preflight sin mutaciones: si hay un SKU repetido, rechazar todo el
        // archivo antes de cambiar datos maestros, stock o ubicaciones WMS.
        var filas = new List<(int Numero, string Contenido)>();
        var skusVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int fila = 1;
        long caracteres = headerLine.Length;
        while (!reader.EndOfStream)
        {
            fila++;
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            caracteres += line.Length;
            if (filas.Count >= MaxFilasImportacion || caracteres > MaxCaracteresImportacion)
            {
                return new ImportResult(filas.Count + 1, 0, 1, new List<string>
                {
                    "CSV excede el límite de filas o caracteres para importación segura."
                });
            }

            filas.Add((fila, line));
            var campos = ParseCsvLine(line);
            if (campos.Count != ColumnasEsperadas.Length) continue; // validación por fila
            string sku = campos[0].Trim();
            if (sku.Length > 0 && !skusVistos.Add(sku))
            {
                errores.Add($"Fila {fila}: SKU duplicado dentro del CSV: {sku}.");
            }
        }

        if (errores.Count > 0)
            return new ImportResult(filas.Count, 0, errores.Count, errores);

        foreach (var (numeroFila, line) in filas)
        {
            total++;
            try
            {
                var fields = ParseCsvLine(line);
                if (fields.Count != 16)
                {
                    errores.Add($"Fila {numeroFila}: Se esperaban exactamente 16 columnas pero se obtuvieron {fields.Count}.");
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
                if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(nombre) ||
                    string.IsNullOrWhiteSpace(categoria))
                {
                    errores.Add($"Fila {numeroFila}: SKU, nombre y categoría son obligatorios.");
                    continue;
                }

                // No sustituir silenciosamente importes y cantidades defectuosos por
                // valores ficticios. Un CSV malformado no puede alterar el catálogo.
                if (!TryParseImportDecimal(fields[4], out decimal precioB2C) ||
                    !TryParseImportDecimal(fields[5], out decimal precioB2B) ||
                    precioB2C <= 0m || precioB2B <= 0m || precioB2B > precioB2C)
                {
                    errores.Add($"Fila {numeroFila}: Precio B2C/B2B inválido o inconsistente.");
                    continue;
                }

                if (!int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stockInicial) ||
                    !int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stockMinimo) ||
                    stockInicial < 0 || stockMinimo < 0)
                {
                    errores.Add($"Fila {numeroFila}: Stock inicial/mínimo inválido o negativo.");
                    continue;
                }

                if (!TryParseImportDecimal(fields[10], out decimal pesoKg) || pesoKg < 0m)
                {
                    errores.Add($"Fila {numeroFila}: Peso en kg inválido o negativo.");
                    continue;
                }

                string proveedor = fields[8].Trim();
                string paisOrigen = fields[9].Trim();
                string dimensiones = fields[11].Trim();
                string unidad = fields[12].Trim();
                string estado = fields[13].Trim();
                string ean = fields[14].Trim();
                string descripcion = fields[15].Trim();

                // Simulación: SOLO validación estructural. Se detiene antes de
                // cualquier acceso o mutación del DbContext y nunca persiste.
                if (soloValidar)
                {
                    exitosos++;
                    continue;
                }

                // Localizar primero el producto. Importar datos maestros NUNCA debe
                // mover stock físico existente ni marcar ubicaciones nuevas ocupadas.
                var producto = await _dbContext.Productos
                    .Include(p => p.Stocks)
                    .FirstOrDefaultAsync(p => p.Sku == sku, cancellationToken);

                // Solo se genera ubicación inicial cuando aún no existe saldo WMS.
                // Un traslado posterior debe pasar por InventoryService y quedar auditado.
                UbicacionAlmacen? ubicacion = null;
                string? wmsCodigo = null;
                if (producto is null || !producto.Stocks.Any())
                {
                    string pasilloLetra = ObtenerPasilloPorCategoria(categoria);
                    int skuNum = total;
                    int rackNum = ((skuNum - 1) % 10) + 1;
                    int nivelNum = (((skuNum - 1) / 10) % 5) + 1;
                    int posNum = (((skuNum - 1) / 50) % 10) + 1;
                    wmsCodigo = $"PAS-{pasilloLetra}-RCK-{rackNum:D2}-NIV-{nivelNum:D2}-POS-{posNum:D2}";

                    ubicacion = await _dbContext.UbicacionesAlmacen
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
                }

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

                    producto.Stocks.Add(new StockFisico
                    {
                        Id = Guid.NewGuid(),
                        ProductoId = producto.Id,
                        CantidadTotal = stockInicial,
                        CantidadReservada = 0,
                        CantidadBloqueada = 0,
                        PuntoReorden = stockMinimo,
                        UbicacionAlmacenId = ubicacion!.Id,
                        UbicacionCodigo = wmsCodigo,
                        UltimaActualizacion = DateTime.UtcNow
                    });

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

                    var stockPrincipal = producto.Stocks.FirstOrDefault();
                    if (stockPrincipal is not null)
                    {
                        // Solo cambia el umbral de reposición. No mutar ubicación ni saldo:
                        // el orden de filas CSV no debe desencadenar un traslado WMS.
                        stockPrincipal.PuntoReorden = stockMinimo;
                    }
                    else
                    {
                        producto.Stocks.Add(new StockFisico
                        {
                            Id = Guid.NewGuid(),
                            ProductoId = producto.Id,
                            CantidadTotal = stockInicial,
                            CantidadReservada = 0,
                            CantidadBloqueada = 0,
                            PuntoReorden = stockMinimo,
                            UbicacionAlmacenId = ubicacion!.Id,
                            UbicacionCodigo = wmsCodigo,
                            UltimaActualizacion = DateTime.UtcNow
                        });
                    }
                }

                exitosos++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // No continuar ni guardar filas anteriores si falla el acceso a
                // PostgreSQL o la preparación de una fila. SaveChanges sigue fuera
                // del bucle, por lo que no se ha confirmado ninguna mutación.
                throw new InvalidOperationException(
                    $"Importación CSV abortada en fila {numeroFila}; no se guardaron cambios del lote.", ex);
            }
        }

        if (!soloValidar)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Catálogo CSV procesado. SoloValidar={SoloValidar}, Total={Total}, Exitosos={Exitosos}, Errores={Errores}",
            soloValidar, total, exitosos, errores.Count);

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

    private static bool TryParseImportDecimal(string value, out decimal result)
    {
        result = default;
        string normalized = value.Trim();
        // El CSV oficial utiliza números decimales sin separadores de miles.
        // NumberStyles.Any interpretaba "1,25" como 125 en InvariantCulture.
        // Se admite coma O punto decimal, nunca ambos, con máximo 2 decimales
        // para ajustarse a las columnas numeric(18,2) de PostgreSQL.
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                normalized, @"\A\d{1,16}(?:[.,]\d{1,2})?\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            return false;

        return decimal.TryParse(normalized.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
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
