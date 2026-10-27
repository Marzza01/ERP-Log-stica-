/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

public class AlertaInventarioServiceTests
{
    [Fact]
    public async Task ObtenerActivas_SinStock_DebeGenerarOutOfStockCritica()
    {
        await using var db = Db();
        db.Productos.Add(Producto("ALT-001", 5));
        await db.SaveChangesAsync();

        var alerta = (await Sut(db).ObtenerActivasAsync()).Single();

        alerta.Tipo.Should().Be(TipoAlertaInventario.OutOfStock);
        alerta.Prioridad.Should().Be(PrioridadAlertaInventario.Critical);
        alerta.CantidadDisponible.Should().Be(0);
        alerta.Estado.Should().Be(EstadoAlertaInventario.Open);
    }

    [Fact]
    public async Task ObtenerActivas_DisponibleEnMinimo_DebeGenerarCriticalStock()
    {
        await using var db = Db();
        var producto = Producto("ALT-002", 5);
        producto.Stocks.Add(Stock(producto, total: 8, reservado: 2, bloqueado: 1, reorden: 12)); // disponible 5
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var alerta = (await Sut(db).ObtenerActivasAsync()).Single();

        alerta.Tipo.Should().Be(TipoAlertaInventario.CriticalStock);
        alerta.Prioridad.Should().Be(PrioridadAlertaInventario.High);
        alerta.CantidadDisponible.Should().Be(5);
        alerta.PuntoReorden.Should().Be(12);
    }

    [Fact]
    public async Task ObtenerActivas_EntreMinimoYReorden_DebeGenerarLowStock()
    {
        await using var db = Db();
        var producto = Producto("ALT-003", 5);
        producto.Stocks.Add(Stock(producto, total: 9, reservado: 0, bloqueado: 0, reorden: 12));
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var alerta = (await Sut(db).ObtenerActivasAsync()).Single();

        alerta.Tipo.Should().Be(TipoAlertaInventario.LowStock);
        alerta.Prioridad.Should().Be(PrioridadAlertaInventario.Medium);
        alerta.FaltanteAReorden.Should().Be(3);
    }

    [Fact]
    public async Task Reconocer_NoDebeResolverNiModificarStock()
    {
        await using var db = Db();
        var producto = Producto("ALT-004", 5);
        var stock = Stock(producto, total: 4, reservado: 0, bloqueado: 0, reorden: 10);
        producto.Stocks.Add(stock);
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var ok = await sut.ReconocerAsync(producto.Id, TipoAlertaInventario.CriticalStock, "admin");
        var alerta = (await sut.ObtenerActivasAsync()).Single();

        ok.Should().BeTrue();
        alerta.Estado.Should().Be(EstadoAlertaInventario.Acknowledged);
        alerta.ReconocidaPor.Should().Be("admin");
        stock.CantidadTotal.Should().Be(4);
        (await db.AlertasInventarioRevision.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ResolverYReactivar_DebeEliminarReconocimientoAnterior()
    {
        await using var db = Db();
        var producto = Producto("ALT-005", 5);
        var stock = Stock(producto, total: 4, reservado: 0, bloqueado: 0, reorden: 10);
        producto.Stocks.Add(stock);
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        await sut.ReconocerAsync(producto.Id, TipoAlertaInventario.CriticalStock, "admin");

        stock.CantidadTotal = 20; // resuelve
        await db.SaveChangesAsync();
        (await sut.ObtenerActivasAsync()).Should().BeEmpty();
        (await db.AlertasInventarioRevision.CountAsync()).Should().Be(0);

        stock.CantidadTotal = 4; // misma condición vuelve a aparecer
        await db.SaveChangesAsync();
        var reactivada = (await sut.ObtenerActivasAsync()).Single();
        reactivada.Estado.Should().Be(EstadoAlertaInventario.Open);
    }

    private static AlertaInventarioService Sut(CatalogDbContext db) => new(db);

    private static CatalogDbContext Db()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"Alertas_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    private static Producto Producto(string sku, int stockMinimo) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = $"Producto {sku}",
        CategoriaNombre = "Pruebas",
        ProveedorNombre = "Proveedor",
        EstadoProducto = "Activo",
        EstaPublicado = true,
        StockMinimo = stockMinimo,
        PrecioB2C = 10m,
        PrecioB2B = 8m,
        VendedorId = Guid.NewGuid(),
        CategoriaId = Guid.NewGuid()
    };

    private static StockFisico Stock(Producto producto, int total, int reservado, int bloqueado, int reorden) => new()
    {
        Id = Guid.NewGuid(),
        ProductoId = producto.Id,
        CantidadTotal = total,
        CantidadReservada = reservado,
        CantidadBloqueada = bloqueado,
        PuntoReorden = reorden,
        UbicacionCodigo = "A-01"
    };
}
