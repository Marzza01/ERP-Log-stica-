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

public class BloqueoInventarioServiceTests
{
    [Fact]
    public async Task BloquearStockAsync_DebeReducirDisponibleSinCambiarFisico()
    {
        await using var db = CrearDb();
        var (producto, ubicacion, stock) = CrearEscenario(total: 10, reservado: 2, bloqueado: 1);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var result = await sut.BloquearStockAsync(stock.Id, 3, Guid.NewGuid(), "Empaque dañado");

        result.Exitoso.Should().BeTrue();
        result.YaProcesada.Should().BeFalse();
        stock.CantidadTotal.Should().Be(10);
        stock.CantidadReservada.Should().Be(2);
        stock.CantidadBloqueada.Should().Be(4);
        stock.CantidadDisponible.Should().Be(4);

        var mov = await db.MovimientosInventario.SingleAsync();
        mov.TipoMovimiento.Should().Be(TipoMovimientoInventario.BloqueoDano);
        mov.Cantidad.Should().Be(3);
        mov.UbicacionOrigenId.Should().Be(ubicacion.Id);
    }

    [Fact]
    public async Task BloquearStockAsync_DebeRespetarReservadoYBloqueado()
    {
        await using var db = CrearDb();
        var (producto, ubicacion, stock) = CrearEscenario(total: 10, reservado: 6, bloqueado: 2);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var result = await sut.BloquearStockAsync(stock.Id, 3, Guid.NewGuid(), "Cuarentena");

        result.Exitoso.Should().BeFalse();
        result.Mensaje.Should().Contain("Disponible: 2");
        stock.CantidadTotal.Should().Be(10);
        stock.CantidadBloqueada.Should().Be(2);
        (await db.MovimientosInventario.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task BloquearStockAsync_DebeSerIdempotentePorOperacionId()
    {
        await using var db = CrearDb();
        var (producto, ubicacion, stock) = CrearEscenario(total: 12, reservado: 0, bloqueado: 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var operacionId = Guid.NewGuid();

        var primera = await sut.BloquearStockAsync(stock.Id, 4, operacionId, "Daño detectado");
        var segunda = await sut.BloquearStockAsync(stock.Id, 4, operacionId, "Daño detectado");

        primera.Exitoso.Should().BeTrue();
        segunda.Exitoso.Should().BeTrue();
        segunda.YaProcesada.Should().BeTrue();
        stock.CantidadBloqueada.Should().Be(4);
        stock.CantidadTotal.Should().Be(12);
        (await db.MovimientosInventario.CountAsync(m => m.TipoMovimiento == TipoMovimientoInventario.BloqueoDano)).Should().Be(1);
    }

    [Fact]
    public async Task DesbloquearStockAsync_DebeRestaurarDisponibleSinCambiarFisico()
    {
        await using var db = CrearDb();
        var (producto, ubicacion, stock) = CrearEscenario(total: 10, reservado: 2, bloqueado: 5);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var result = await sut.DesbloquearStockAsync(stock.Id, 3, Guid.NewGuid(), "Inspección aprobada");

        result.Exitoso.Should().BeTrue();
        stock.CantidadTotal.Should().Be(10);
        stock.CantidadReservada.Should().Be(2);
        stock.CantidadBloqueada.Should().Be(2);
        stock.CantidadDisponible.Should().Be(6);

        var mov = await db.MovimientosInventario.SingleAsync();
        mov.TipoMovimiento.Should().Be(TipoMovimientoInventario.Desbloqueo);
        mov.Cantidad.Should().Be(3);
    }

    [Fact]
    public async Task DesbloquearStockAsync_DebeRechazarCantidadMayorQueBloqueada()
    {
        await using var db = CrearDb();
        var (producto, ubicacion, stock) = CrearEscenario(total: 10, reservado: 0, bloqueado: 2);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var result = await sut.DesbloquearStockAsync(stock.Id, 3, Guid.NewGuid(), "Liberación");

        result.Exitoso.Should().BeFalse();
        result.Mensaje.Should().Contain("Bloqueadas: 2");
        stock.CantidadBloqueada.Should().Be(2);
        stock.CantidadTotal.Should().Be(10);
        (await db.MovimientosInventario.CountAsync()).Should().Be(0);
    }

    private static CatalogDbContext CrearDb()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"BloqueoInventarioDb_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    private static InventoryService CrearSut(CatalogDbContext db) =>
        new(db, NullLogger<InventoryService>.Instance);

    private static (Producto Producto, UbicacionAlmacen Ubicacion, StockFisico Stock) CrearEscenario(
        int total,
        int reservado,
        int bloqueado)
    {
        var producto = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-BLQ-{Guid.NewGuid():N}"[..20],
            Nombre = "Producto prueba bloqueo",
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
            Descripcion = "Producto de prueba",
            VendedorId = Guid.NewGuid(),
            CategoriaId = Guid.NewGuid(),
            EstaPublicado = true,
            FechaCreacion = DateTime.UtcNow
        };

        var ubicacion = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(),
            Codigo = $"PAS-A-RCK-01-NIV-01-POS-{Random.Shared.Next(10, 99)}",
            Pasillo = "A",
            Rack = "01",
            Nivel = 1,
            Posicion = "01",
            CapacidadMaxima = 100,
            EstaOcupada = true,
            Zona = "Almacenamiento",
            FechaCreacion = DateTime.UtcNow
        };

        var stock = new StockFisico
        {
            Id = Guid.NewGuid(),
            ProductoId = producto.Id,
            CantidadTotal = total,
            CantidadReservada = reservado,
            CantidadBloqueada = bloqueado,
            PuntoReorden = 2,
            UbicacionAlmacenId = ubicacion.Id,
            UbicacionCodigo = ubicacion.Codigo,
            UltimaActualizacion = DateTime.UtcNow
        };

        return (producto, ubicacion, stock);
    }
}
