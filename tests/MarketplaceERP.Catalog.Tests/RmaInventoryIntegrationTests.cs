/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// Fase 5A: integración RMA -> Inventario.
/// </summary>
public class RmaInventoryIntegrationTests
{
    [Fact]
    public async Task ReStock_DebeAumentarUbicacionSeleccionadaYCrearMovimiento()
    {
        await using var db = CrearDb();
        var producto = CrearProducto("RMA-001");
        var ubicacion = CrearUbicacion("PAS-RMA-01", 100);
        var stock = CrearStock(producto.Id, ubicacion, 10);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearRmaService(db);
        var rma = await sut.RegistrarSolicitudRmaAsync(
            new RegistrarRmaRequest(Guid.NewGuid(), producto.Id, 3, "Caja abierta"));

        var resultado = await sut.InspeccionarDevolucionAsync(
            rma.Id,
            new DictaminarRmaRequest(
                ClasificacionRMA.ReStock,
                "Inspector QA",
                "Producto operativo y apto para venta.",
                UbicacionDestinoId: ubicacion.Id,
                OperacionId: Guid.NewGuid()));

        resultado.Should().NotBeNull();
        resultado!.Estado.Should().Be(EstadoRMA.Completado);
        stock.CantidadTotal.Should().Be(13);

        var movimiento = await db.MovimientosInventario.SingleAsync();
        movimiento.TipoMovimiento.Should().Be(TipoMovimientoInventario.RmaRestock);
        movimiento.RmaId.Should().Be(rma.Id);
        movimiento.ProductoId.Should().Be(producto.Id);
        movimiento.Cantidad.Should().Be(3);
        movimiento.UbicacionDestinoId.Should().Be(ubicacion.Id);
    }

    [Fact]
    public async Task ReStock_ReintentoNoDebeDuplicarStockNiMovimiento()
    {
        await using var db = CrearDb();
        var producto = CrearProducto("RMA-002");
        var ubicacion = CrearUbicacion("PAS-RMA-02", 100);
        var stock = CrearStock(producto.Id, ubicacion, 20);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearRmaService(db);
        var rma = await sut.RegistrarSolicitudRmaAsync(
            new RegistrarRmaRequest(Guid.NewGuid(), producto.Id, 2, "Cambio por garantía"));

        var primerDictamen = new DictaminarRmaRequest(
            ClasificacionRMA.ReStock,
            "Inspector QA",
            "Producto apto.",
            UbicacionDestinoId: ubicacion.Id,
            OperacionId: Guid.NewGuid());

        await sut.InspeccionarDevolucionAsync(rma.Id, primerDictamen);

        // Simula un reintento desde la UI con un nuevo OperacionId.
        var segundoDictamen = primerDictamen with { OperacionId = Guid.NewGuid() };
        var reintento = await sut.InspeccionarDevolucionAsync(rma.Id, segundoDictamen);

        reintento!.Estado.Should().Be(EstadoRMA.Completado);
        stock.CantidadTotal.Should().Be(22);
        (await db.MovimientosInventario.CountAsync(m =>
            m.TipoMovimiento == TipoMovimientoInventario.RmaRestock &&
            m.RmaId == rma.Id)).Should().Be(1);
    }

    [Fact]
    public async Task ReStock_SinUbicacionDebeRechazarseSinCambiarInventario()
    {
        await using var db = CrearDb();
        var producto = CrearProducto("RMA-003");
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var sut = CrearRmaService(db);
        var rma = await sut.RegistrarSolicitudRmaAsync(
            new RegistrarRmaRequest(Guid.NewGuid(), producto.Id, 1, "Producto sin uso"));

        Func<Task> act = async () => await sut.InspeccionarDevolucionAsync(
            rma.Id,
            new DictaminarRmaRequest(
                ClasificacionRMA.ReStock,
                "Inspector QA",
                "Producto apto.",
                OperacionId: Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ubicación WMS*");

        (await db.MovimientosInventario.CountAsync()).Should().Be(0);
        rma.Estado.Should().Be(EstadoRMA.Solicitado);
        rma.Clasificacion.Should().Be(ClasificacionRMA.Pendiente);
    }

    [Fact]
    public async Task ReStock_SiExcedeCapacidadDebeRechazarseYNoCompletarRma()
    {
        await using var db = CrearDb();
        var productoA = CrearProducto("RMA-004-A");
        var productoB = CrearProducto("RMA-004-B");
        var ubicacion = CrearUbicacion("PAS-RMA-04", 10);
        var stockOcupante = CrearStock(productoA.Id, ubicacion, 9);
        db.AddRange(productoA, productoB, ubicacion, stockOcupante);
        await db.SaveChangesAsync();

        var sut = CrearRmaService(db);
        var rma = await sut.RegistrarSolicitudRmaAsync(
            new RegistrarRmaRequest(Guid.NewGuid(), productoB.Id, 2, "Retorno por garantía"));

        Func<Task> act = async () => await sut.InspeccionarDevolucionAsync(
            rma.Id,
            new DictaminarRmaRequest(
                ClasificacionRMA.ReStock,
                "Inspector QA",
                "Producto apto.",
                UbicacionDestinoId: ubicacion.Id,
                OperacionId: Guid.NewGuid()));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*capacidad*");

        (await db.StockFisico.Where(s => s.ProductoId == productoB.Id).CountAsync()).Should().Be(0);
        (await db.MovimientosInventario.CountAsync()).Should().Be(0);
        rma.Estado.Should().Be(EstadoRMA.Solicitado);
    }

    private static CatalogDbContext CrearDb()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"RmaInventoryDb_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    private static RmaService CrearRmaService(CatalogDbContext db)
    {
        var inventory = new InventoryService(db, NullLogger<InventoryService>.Instance);
        return new RmaService(db, inventory, NullLogger<RmaService>.Instance);
    }

    private static Producto CrearProducto(string sku) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = "Producto RMA",
        CategoriaNombre = "PRUEBAS",
        Subcategoria = "PRUEBAS",
        PrecioB2C = 100m,
        PrecioB2B = 80m,
        Moneda = "USD",
        StockInicial = 0,
        StockMinimo = 2,
        ProveedorNombre = "Proveedor prueba",
        PaisOrigen = "El Salvador",
        PesoKg = 1m,
        DimensionesCm = "1x1x1",
        UnidadMedida = "Unidad",
        EstadoProducto = "Activo",
        CodigoEAN = string.Empty,
        Descripcion = "Producto de prueba RMA",
        VendedorId = Guid.NewGuid(),
        CategoriaId = Guid.NewGuid(),
        EstaPublicado = true,
        FechaCreacion = DateTime.UtcNow
    };

    private static UbicacionAlmacen CrearUbicacion(string codigo, int capacidad) => new()
    {
        Id = Guid.NewGuid(),
        Codigo = codigo,
        Pasillo = "RMA",
        Rack = "01",
        Nivel = 1,
        Posicion = "01",
        CapacidadMaxima = capacidad,
        EstaOcupada = false,
        Zona = "RMA",
        FechaCreacion = DateTime.UtcNow
    };

    private static StockFisico CrearStock(Guid productoId, UbicacionAlmacen ubicacion, int total) => new()
    {
        Id = Guid.NewGuid(),
        ProductoId = productoId,
        CantidadTotal = total,
        CantidadReservada = 0,
        CantidadBloqueada = 0,
        PuntoReorden = 2,
        UbicacionAlmacenId = ubicacion.Id,
        UbicacionCodigo = ubicacion.Codigo,
        UltimaActualizacion = DateTime.UtcNow
    };
}
