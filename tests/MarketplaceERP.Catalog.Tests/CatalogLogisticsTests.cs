using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Catalog.SeedData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public class CatalogLogisticsTests
{
    private CatalogDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(databaseName: $"CatalogTestDb_{Guid.NewGuid()}")
            .Options;

        return new CatalogDbContext(options);
    }

    [Fact]
    public async Task ImportarCatalogoCsv_Con16Campos_ImportaCorrectamenteYGeneraWMS()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = new CatalogImportService(dbContext, NullLogger<CatalogImportService>.Instance);

        string csvContent =
            "\"SKU\",\"Nombre del Producto\",\"Categoría\",\"Subcategoría\",\"Precio B2C ($)\",\"Precio B2B ($)\",\"Stock Inicial\",\"Stock Mínimo\",\"Proveedor\",\"País Origen\",\"Peso (kg)\",\"Dimensiones (cm)\",\"Unidad de Medida\",\"Estado\",\"Código EAN\",\"Descripción Corta\"\n" +
            "\"ELC-0001\",\"Smartphone Pro 128GB\",\"Electrónica y Tecnología\",\"Smartphones\",\"350.00\",\"280.00\",\"100\",\"20\",\"Telecom SV\",\"China\",\"0.35\",\"15x7x1 cm\",\"Unidad\",\"Activo\",\"7410000000001\",\"Smartphone alta gama\"\n" +
            "\"ROP-0001\",\"Camisa Formal Slim\",\"Ropa y Accesorios\",\"Moda Hombre\",\"25.50\",\"18.00\",\"50\",\"15\",\"Confecciones SV\",\"El Salvador\",\"0.25\",\"30x20x2 cm\",\"Unidad\",\"Activo\",\"7410000000002\",\"Camisa de vestir\"\n";

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));
        var defaultVendedorId = Guid.NewGuid();
        var defaultCategoriaId = Guid.NewGuid();

        // Act
        var result = await service.ImportarCatalogoCsvAsync(stream, defaultVendedorId, defaultCategoriaId);

        // Assert
        result.TotalProcesados.Should().Be(2);
        result.Exitosos.Should().Be(2);
        result.Errores.Should().Be(0);

        var producto = await dbContext.Productos.Include(p => p.Stock).FirstOrDefaultAsync(p => p.Sku == "ELC-0001");
        producto.Should().NotBeNull();
        producto!.Nombre.Should().Be("Smartphone Pro 128GB");
        producto.CategoriaNombre.Should().Be("Electrónica y Tecnología");
        producto.PrecioB2C.Should().Be(350.00m);
        producto.PrecioB2B.Should().Be(280.00m);
        producto.StockMinimo.Should().Be(20);
        producto.Stock.Should().NotBeNull();
        producto.Stock!.CantidadTotal.Should().Be(100);
        producto.Stock.UbicacionCodigo.Should().StartWith("PAS-A-RCK-");
    }

    [Fact]
    public async Task WmsService_ObtenerMapaBodega_CalculaCapacidadYOcupacion()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var u1 = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(),
            Codigo = "PAS-A-RCK-01-NIV-01-POS-01",
            Pasillo = "Pasillo A",
            Rack = "Rack 01",
            Nivel = 1,
            Posicion = "Posición 01",
            EstaOcupada = true
        };
        var u2 = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(),
            Codigo = "PAS-A-RCK-01-NIV-01-POS-02",
            Pasillo = "Pasillo A",
            Rack = "Rack 01",
            Nivel = 1,
            Posicion = "Posición 02",
            EstaOcupada = false
        };
        dbContext.UbicacionesAlmacen.AddRange(u1, u2);
        await dbContext.SaveChangesAsync();

        var wmsService = new WmsService(dbContext, NullLogger<WmsService>.Instance);

        // Act
        var resumen = await wmsService.ObtenerMapaBodegaAsync();

        // Assert
        resumen.TotalUbicaciones.Should().Be(2);
        resumen.Ocupadas.Should().Be(1);
        resumen.Disponibles.Should().Be(1);
        resumen.PorcentajeOcupacion.Should().Be(50m);
        resumen.Pasillos.Should().HaveCount(1);
        resumen.Pasillos[0].Pasillo.Should().Be("Pasillo A");
    }

    [Fact]
    public async Task WmsService_ReubicarStock_ActualizaUbicacionCorrectamente()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var uOrigen = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(),
            Codigo = "PAS-A-RCK-01-NIV-01-POS-01",
            Pasillo = "Pasillo A",
            Rack = "Rack 01",
            Nivel = 1,
            Posicion = "Posición 01",
            EstaOcupada = true
        };
        var uDestino = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(),
            Codigo = "PAS-B-RCK-02-NIV-03-POS-05",
            Pasillo = "Pasillo B",
            Rack = "Rack 02",
            Nivel = 3,
            Posicion = "Posición 05",
            EstaOcupada = false
        };
        var stock = new StockFisico
        {
            Id = Guid.NewGuid(),
            ProductoId = Guid.NewGuid(),
            CantidadTotal = 30,
            UbicacionAlmacenId = uOrigen.Id,
            UbicacionCodigo = uOrigen.Codigo
        };
        dbContext.UbicacionesAlmacen.AddRange(uOrigen, uDestino);
        dbContext.StockFisico.Add(stock);
        await dbContext.SaveChangesAsync();

        var wmsService = new WmsService(dbContext, NullLogger<WmsService>.Instance);

        // Act
        var result = await wmsService.ReubicarStockAsync(stock.Id, uDestino.Id);

        // Assert
        result.Should().BeTrue();
        var stockActualizado = await dbContext.StockFisico.FindAsync(stock.Id);
        stockActualizado!.UbicacionAlmacenId.Should().Be(uDestino.Id);
        stockActualizado.UbicacionCodigo.Should().Be(uDestino.Codigo);

        var destinoActualizado = await dbContext.UbicacionesAlmacen.FindAsync(uDestino.Id);
        destinoActualizado!.EstaOcupada.Should().BeTrue();
    }

    [Fact]
    public async Task RmaService_InspeccionReStock_AumentaInventarioDisponible()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var producto = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = "ELC-0005",
            Nombre = "Audífonos Inalámbricos",
            Stock = new StockFisico
            {
                Id = Guid.NewGuid(),
                CantidadTotal = 50,
                CantidadReservada = 0
            }
        };
        dbContext.Productos.Add(producto);
        await dbContext.SaveChangesAsync();

        var rmaService = new RmaService(dbContext, NullLogger<RmaService>.Instance);

        var rma = await rmaService.RegistrarSolicitudRmaAsync(new RegistrarRmaRequest(
            OrdenId: Guid.NewGuid(),
            ProductoId: producto.Id,
            Cantidad: 3,
            MotivoCliente: "Empaque abierto por error"
        ));

        // Act
        var dictamen = new DictaminarRmaRequest(
            Clasificacion: ClasificacionRMA.ReStock,
            Inspector: "Técnico Juan Pérez",
            DictamenTecnico: "Sellado intacto, producto operativo al 100%."
        );
        var rmaDictaminado = await rmaService.InspeccionarDevolucionAsync(rma.Id, dictamen);

        // Assert
        rmaDictaminado.Should().NotBeNull();
        rmaDictaminado!.Clasificacion.Should().Be(ClasificacionRMA.ReStock);
        rmaDictaminado.Estado.Should().Be(EstadoRMA.Completado);

        var prodActualizado = await dbContext.Productos.Include(p => p.Stock).FirstOrDefaultAsync(p => p.Id == producto.Id);
        prodActualizado!.Stock!.CantidadTotal.Should().Be(53); // 50 + 3 reingresadas
    }

    [Fact]
    public async Task RmaService_InspeccionMermaScrap_NoAumentaInventario()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var producto = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = "ELC-0006",
            Nombre = "Monitor LED",
            Stock = new StockFisico
            {
                Id = Guid.NewGuid(),
                CantidadTotal = 20,
                CantidadReservada = 0
            }
        };
        dbContext.Productos.Add(producto);
        await dbContext.SaveChangesAsync();

        var rmaService = new RmaService(dbContext, NullLogger<RmaService>.Instance);

        var rma = await rmaService.RegistrarSolicitudRmaAsync(new RegistrarRmaRequest(
            OrdenId: Guid.NewGuid(),
            ProductoId: producto.Id,
            Cantidad: 2,
            MotivoCliente: "Pantalla quebrada en transporte"
        ));

        // Act
        var dictamen = new DictaminarRmaRequest(
            Clasificacion: ClasificacionRMA.MermaScrap,
            Inspector: "Técnico Logística Inversa",
            DictamenTecnico: "Panel LCD fracturado, pérdida irreversible."
        );
        var rmaDictaminado = await rmaService.InspeccionarDevolucionAsync(rma.Id, dictamen);

        // Assert
        rmaDictaminado.Should().NotBeNull();
        rmaDictaminado!.Clasificacion.Should().Be(ClasificacionRMA.MermaScrap);

        var prodActualizado = await dbContext.Productos.Include(p => p.Stock).FirstOrDefaultAsync(p => p.Id == producto.Id);
        prodActualizado!.Stock!.CantidadTotal.Should().Be(20); // No se incrementa
    }

    [Fact]
    public void StockFisico_PuntoReorden_DetectaBajoMinimo()
    {
        // Arrange & Act
        var stockNormal = new StockFisico { CantidadTotal = 50, CantidadReservada = 10, PuntoReorden = 20 };
        var stockCritico = new StockFisico { CantidadTotal = 25, CantidadReservada = 10, PuntoReorden = 20 };

        // Assert
        stockNormal.CantidadDisponible.Should().Be(40);
        stockNormal.EsBajoMinimo.Should().BeFalse();

        stockCritico.CantidadDisponible.Should().Be(15);
        stockCritico.EsBajoMinimo.Should().BeTrue();
    }

    [Fact]
    public async Task ImportarCatalogoCompleto_500SKUs_CargaTodosLosProductosYVerificaCategorias()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var service = new CatalogImportService(dbContext, NullLogger<CatalogImportService>.Instance);

        var posiblesRutas = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "data", "Catalogo_500_Productos.csv"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MarketplaceERP.Catalog", "SeedData", "Catalogo_500_Productos.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "docs", "data", "Catalogo_500_Productos.csv")
        };

        string? rutaCsv = posiblesRutas.FirstOrDefault(File.Exists) 
            ?? Path.Combine(AppContext.BaseDirectory, "Catalogo_500_Productos.csv");

        // Asegurar que el archivo tenga codificación UTF-8 pura
        CatalogDataGenerator.GenerarCsv(rutaCsv);

        var defaultVendedorId = Guid.NewGuid();
        var defaultCategoriaId = Guid.NewGuid();

        // Act
        var result = await service.ImportarCatalogoDesdeArchivoAsync(rutaCsv!, defaultVendedorId, defaultCategoriaId);

        // Assert
        result.TotalProcesados.Should().Be(500);
        result.Exitosos.Should().Be(500);
        result.Errores.Should().Be(0);

        var totalEnBd = await dbContext.Productos.CountAsync();
        totalEnBd.Should().Be(500);

        var categorias = await dbContext.Productos.Select(p => p.CategoriaNombre).Distinct().ToListAsync();
        categorias.Should().HaveCount(6);
        categorias.Should().Contain(new[]
        {
            "Electrónica y Tecnología",
            "Ropa y Accesorios",
            "Alimentos y Bebidas",
            "Ferretería e Industria",
            "Hogar y Decoración",
            "Salud y Cuidado Personal"
        });

        // Verificar que todos tienen stock físico y código de ubicación WMS
        var sinStock = await dbContext.Productos.Where(p => p.Stock == null).CountAsync();
        sinStock.Should().Be(0);

        var ubicacionesGeneradas = await dbContext.UbicacionesAlmacen.CountAsync();
        ubicacionesGeneradas.Should().BeGreaterThan(0);
    }
}
