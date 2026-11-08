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
}
