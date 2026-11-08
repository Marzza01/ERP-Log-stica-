using System.Globalization;
using System.Text;
using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// Un CSV de datos maestros no debe ejecutar traslados físicos en el WMS.
/// Un traslado de stock debe pasar por el servicio de inventario auditado.
/// </summary>
public class CatalogImportPreservesWmsTests
{
    private const string Header =
        "\"SKU\",\"Nombre del Producto\",\"Categoría\",\"Subcategoría\",\"Precio B2C ($)\",\"Precio B2B ($)\",\"Stock Inicial\",\"Stock Mínimo\",\"Proveedor\",\"País Origen\",\"Peso (kg)\",\"Dimensiones (cm)\",\"Unidad de Medida\",\"Estado\",\"Código EAN\",\"Descripción Corta\"";

    private static string Row(string sku, string category, int initialStock, int minimumStock, decimal price) =>
        $"\"{sku}\",\"Producto {sku}\",\"{category}\",\"General\",\"{price.ToString("0.00", CultureInfo.InvariantCulture)}\",\"{(price * .8m).ToString("0.00", CultureInfo.InvariantCulture)}\",\"{initialStock}\",\"{minimumStock}\",\"Proveedor\",\"El Salvador\",\"1.0\",\"20x10x5 cm\",\"Unidad\",\"Activo\",\"\",\"Ficha técnica\"";

    [Fact]
    public async Task ReimportarEnOrdenInverso_NoMueveStockNiCreaUbicacionesFantasma()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogWmsReimport_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
        var vendor = Guid.NewGuid();
        var category = Guid.NewGuid();

        async Task Importar(params string[] rows)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(
                Header + "\n" + string.Join("\n", rows) + "\n"));
            var result = await service.ImportarCatalogoCsvAsync(input, vendor, category);
            result.TotalProcesados.Should().Be(rows.Length);
            result.Exitosos.Should().Be(rows.Length);
            result.Errores.Should().Be(0);
        }

        const string tech = "Electrónica y Tecnología";
        const string clothing = "Ropa y Accesorios";

        await Importar(
            Row("ELC-0001", tech, 100, 20, 350m),
            Row("ROP-0001", clothing, 50, 15, 25m));

        var before = (await db.StockFisico.AsNoTracking().ToListAsync())
            .ToDictionary(s => s.ProductoId, s => new
            {
                s.Id,
                s.CantidadTotal,
                s.CantidadReservada,
                s.CantidadBloqueada,
                s.UbicacionAlmacenId,
                s.UbicacionCodigo
            });
        before.Should().HaveCount(2);
        var locationCount = await db.UbicacionesAlmacen.CountAsync();

        // El orden de las filas y el stock inicial cambian, pero el stock físico
        // ya contabilizado no debe cambiar por reimportar datos maestros.
        await Importar(
            Row("ROP-0001", clothing, 999, 19, 30m),
            Row("ELC-0001", tech, 999, 22, 355m));

        var after = await db.StockFisico.AsNoTracking().ToListAsync();
        after.Should().HaveCount(2);
        foreach (var balance in after)
        {
            var original = before[balance.ProductoId];
            balance.Id.Should().Be(original.Id);
            balance.CantidadTotal.Should().Be(original.CantidadTotal);
            balance.CantidadReservada.Should().Be(original.CantidadReservada);
            balance.CantidadBloqueada.Should().Be(original.CantidadBloqueada);
            balance.UbicacionAlmacenId.Should().Be(original.UbicacionAlmacenId);
            balance.UbicacionCodigo.Should().Be(original.UbicacionCodigo);
        }

        (await db.UbicacionesAlmacen.CountAsync()).Should().Be(locationCount);
        (await db.MovimientosInventario.CountAsync()).Should().Be(0);

        var products = await db.Productos.AsNoTracking().ToDictionaryAsync(p => p.Sku);
        products["ELC-0001"].PrecioB2C.Should().Be(355m);
        products["ROP-0001"].PrecioB2C.Should().Be(30m);
        products["ELC-0001"].StockMinimo.Should().Be(22);
        products["ROP-0001"].StockMinimo.Should().Be(19);
    }
    [Fact]
    public async Task Importacion_RechazaPreciosYStockInvalidosSinValoresPorDefecto()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogBadImport_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);

        static string CambiarCampo(string row, int index, string value)
        {
            var fields = row.Split(',');
            fields[index] = "\"" + value + "\"";
            return string.Join(",", fields);
        }

        const string cat = "Electrónica y Tecnología";
        var precioRoto = CambiarCampo(Row("ERR-PRECIO", cat, 20, 4, 25m), 4, "sin-precio");
        var stockRoto = CambiarCampo(Row("ERR-STOCK", cat, 20, 4, 25m), 6, "muchos");
        var valido = Row("SKU-VALIDO", cat, 20, 4, 25m);

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" + precioRoto + "\n" + stockRoto + "\n" + valido + "\n"));
        var result = await service.ImportarCatalogoCsvAsync(input, Guid.NewGuid(), Guid.NewGuid());

        result.TotalProcesados.Should().Be(3);
        result.Exitosos.Should().Be(0); // lote rechazado completamente
        result.Errores.Should().Be(2);
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.Productos.CountAsync()).Should().Be(0);
        (await db.StockFisico.CountAsync()).Should().Be(0);
        (await db.UbicacionesAlmacen.CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task Importacion_EncabezadoDesordenado_RechazaArchivoAntesDeEscribir()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogBadHeader_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);

        // Las columnas siguen siendo 16, pero el orden del contrato ya no es válido.
        var headerAlterado = Header.Replace(
            "\"SKU\",\"Nombre del Producto\"",
            "\"Nombre del Producto\",\"SKU\"");

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            headerAlterado + "\n" + Row("SKU-001", "Electrónica y Tecnología", 20, 5, 25m) + "\n"));

        var result = await service.ImportarCatalogoCsvAsync(input, Guid.NewGuid(), Guid.NewGuid());

        result.Exitosos.Should().Be(0);
        result.Errores.Should().Be(1);
        result.MensajesError.Should().ContainSingle(m => m.Contains("Encabezado"));
        (await db.Productos.CountAsync()).Should().Be(0);
        (await db.StockFisico.CountAsync()).Should().Be(0);
        (await db.UbicacionesAlmacen.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Importacion_SkuDuplicadoConDistintaCapitalizacion_NoEscribeNingunaFila()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogDuplicateSku_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);

        const string category = "Electrónica y Tecnología";
        var rows = new[]
        {
            Row("SKU-DUP", category, 40, 10, 30m),
            Row("SKU-OK", category, 40, 10, 30m),
            Row("sku-dup", category, 40, 10, 30m)
        };

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" + string.Join("\n", rows) + "\n"));

        var result = await service.ImportarCatalogoCsvAsync(input, Guid.NewGuid(), Guid.NewGuid());

        result.TotalProcesados.Should().Be(3);
        result.Exitosos.Should().Be(0);
        result.Errores.Should().Be(1);
        result.MensajesError.Should().ContainSingle(m => m.Contains("duplicado"));
        (await db.Productos.CountAsync()).Should().Be(0);
        (await db.StockFisico.CountAsync()).Should().Be(0);
        (await db.UbicacionesAlmacen.CountAsync()).Should().Be(0);
    }



    [Fact]
    public async Task ValidarCsv_DosFilasCorrectas_NoCreaDatosNiAdjuntaEntidades()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogDryRun_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
        const string category = "Electrónica y Tecnología";

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" +
            Row("DRY-001", category, 20, 4, 25m) + "\n" +
            Row("DRY-002", category, 30, 5, 40m) + "\n"));

        var result = await service.ValidarCatalogoCsvAsync(input);

        result.TotalProcesados.Should().Be(2);
        result.Exitosos.Should().Be(2);
        result.Errores.Should().Be(0);
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.Productos.CountAsync()).Should().Be(0);
        (await db.StockFisico.CountAsync()).Should().Be(0);
        (await db.UbicacionesAlmacen.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ValidarCsv_PrecioInvalido_ReportarErrorSinPersistirFilasValidas()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogDryRunInvalid_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);

        const string category = "Electrónica y Tecnología";
        var invalido = Row("DRY-ERR", category, 10, 3, 20m).Replace("\"16.00\"", "\"incorrecto\"");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" +
            Row("DRY-GOOD", category, 20, 4, 25m) + "\n" +
            invalido + "\n"));

        var result = await service.ValidarCatalogoCsvAsync(input);

        result.TotalProcesados.Should().Be(2);
        result.Exitosos.Should().Be(1);
        result.Errores.Should().Be(1);
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.Productos.CountAsync()).Should().Be(0);
        (await db.StockFisico.CountAsync()).Should().Be(0);
        (await db.UbicacionesAlmacen.CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task ReimportarConFilaValidaEInvalida_NoModificaProductoNiUbicacionExistentes()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogAtomicReimport_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
        var vendedor = Guid.NewGuid();
        var categoria = Guid.NewGuid();
        const string category = "Electrónica y Tecnología";

        using (var original = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" + Row("SKU-001", category, 100, 20, 30m) + "\n")))
        {
            var carga = await service.ImportarCatalogoCsvAsync(original, vendedor, categoria);
            carga.Exitosos.Should().Be(1);
            carga.Errores.Should().Be(0);
        }

        var antes = await db.Productos.AsNoTracking().SingleAsync();
        var stockAntes = await db.StockFisico.AsNoTracking().SingleAsync();
        var ubicacionesAntes = await db.UbicacionesAlmacen.CountAsync();

        // Si alguna fila tiene precio inválido, incluso después de una fila
        // válida que cambiaría el SKU existente, se rechaza el lote completo.
        var incorrecta = Row("SKU-002", category, 30, 5, 45m)
            .Replace("\"45.00\"", "\"precio-roto\"");
        using var segundo = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" +
            Row("SKU-001", category, 777, 9, 90m) + "\n" +
            incorrecta + "\n"));

        var resultado = await service.ImportarCatalogoCsvAsync(segundo, vendedor, categoria);
        resultado.TotalProcesados.Should().Be(2);
        resultado.Exitosos.Should().Be(0);
        resultado.Errores.Should().Be(1);

        (await db.Productos.CountAsync()).Should().Be(1);
        var despues = await db.Productos.AsNoTracking().SingleAsync();
        despues.Id.Should().Be(antes.Id);
        despues.PrecioB2C.Should().Be(antes.PrecioB2C);
        despues.StockMinimo.Should().Be(antes.StockMinimo);
        (await db.StockFisico.CountAsync()).Should().Be(1);
        var stockDespues = await db.StockFisico.AsNoTracking().SingleAsync();
        stockDespues.CantidadTotal.Should().Be(stockAntes.CantidadTotal);
        stockDespues.UbicacionAlmacenId.Should().Be(stockAntes.UbicacionAlmacenId);
        (await db.UbicacionesAlmacen.CountAsync()).Should().Be(ubicacionesAntes);
    }



    [Fact]
    public async Task ImportarDecimalConComa_NoInterpretaComoMilesYRechazaFormatoAmbiguo()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"CatalogDecimalStrict_{Guid.NewGuid():N}")
            .Options;
        await using var db = new CatalogDbContext(options);
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
        const string category = "Electrónica y Tecnología";
        var vendor = Guid.NewGuid();
        var catId = Guid.NewGuid();

        var filaComa = Row("DEC-001", category, 10, 3, 25m)
            .Replace("\"25.00\"", "\"1,25\"")
            .Replace("\"20.00\"", "\"1,00\"");
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" + filaComa + "\n")))
        {
            var result = await service.ImportarCatalogoCsvAsync(input, vendor, catId);
            result.Exitosos.Should().Be(1);
            result.Errores.Should().Be(0);
        }

        var guardado = await db.Productos.AsNoTracking().SingleAsync();
        guardado.PrecioB2C.Should().Be(1.25m);
        guardado.PrecioB2B.Should().Be(1.00m);

        var filaAmbigua = Row("DEC-002", category, 10, 3, 25m)
            .Replace("\"25.00\"", "\"1,234\"");
        using var bad = new MemoryStream(Encoding.UTF8.GetBytes(
            Header + "\n" + filaAmbigua + "\n"));
        var rejected = await service.ImportarCatalogoCsvAsync(bad, vendor, catId);
        rejected.Exitosos.Should().Be(0);
        rejected.Errores.Should().Be(1);
        (await db.Productos.CountAsync()).Should().Be(1);
    }


}
