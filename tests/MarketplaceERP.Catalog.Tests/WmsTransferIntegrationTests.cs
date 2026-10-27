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

public class WmsTransferIntegrationTests
{
    private static CatalogDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"WmsTransferDb_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    [Fact]
    public async Task TransferirStock_Parcial_ConservaTotalGlobalYRegistraLedger()
    {
        using var db = CreateInMemoryDbContext();
        var producto = ProductoBase("WMS-0001");
        var origen = Ubicacion("ORIGEN", 100, true);
        var destino = Ubicacion("DESTINO", 100, false);
        var stockOrigen = Stock(producto.Id, origen, total: 10);

        db.AddRange(producto, origen, destino, stockOrigen);
        await db.SaveChangesAsync();

        var service = new InventoryService(db, NullLogger<InventoryService>.Instance);
        var result = await service.TransferirStockAsync(
            stockOrigen.Id, destino.Id, 4, Guid.NewGuid(), "Movimiento interno de prueba");

        result.Exitoso.Should().BeTrue();
        result.YaProcesada.Should().BeFalse();
        result.CantidadTransferida.Should().Be(4);

        var origenDb = await db.StockFisico.SingleAsync(s => s.Id == stockOrigen.Id);
        var destinoDb = await db.StockFisico.SingleAsync(s => s.ProductoId == producto.Id && s.UbicacionAlmacenId == destino.Id);

        origenDb.CantidadTotal.Should().Be(6);
        destinoDb.CantidadTotal.Should().Be(4);
        (origenDb.CantidadTotal + destinoDb.CantidadTotal).Should().Be(10);

        var movimientos = await db.MovimientosInventario
            .Where(m => m.ProductoId == producto.Id)
            .OrderBy(m => m.TipoMovimiento)
            .ToListAsync();

        movimientos.Should().HaveCount(2);
        movimientos.Should().ContainSingle(m => m.TipoMovimiento == TipoMovimientoInventario.TransferenciaSalida && m.Cantidad == 4);
        movimientos.Should().ContainSingle(m => m.TipoMovimiento == TipoMovimientoInventario.TransferenciaEntrada && m.Cantidad == 4);
    }

    [Fact]
    public async Task TransferirStock_NoMueveUnidadesReservadasNiBloqueadas()
    {
        using var db = CreateInMemoryDbContext();
        var producto = ProductoBase("WMS-0002");
        var origen = Ubicacion("ORIGEN", 100, true);
        var destino = Ubicacion("DESTINO", 100, false);
        var stockOrigen = Stock(producto.Id, origen, total: 10, reservado: 3, bloqueado: 2);

        db.AddRange(producto, origen, destino, stockOrigen);
        await db.SaveChangesAsync();

        var service = new InventoryService(db, NullLogger<InventoryService>.Instance);
        var result = await service.TransferirStockAsync(
            stockOrigen.Id, destino.Id, 6, Guid.NewGuid());

        result.Exitoso.Should().BeFalse();
        result.Mensaje.Should().Contain("Disponible: 5");

        var origenDb = await db.StockFisico.SingleAsync(s => s.Id == stockOrigen.Id);
        origenDb.CantidadTotal.Should().Be(10);
        (await db.StockFisico.CountAsync()).Should().Be(1);
        (await db.MovimientosInventario.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TransferirStock_ReintentoMismaOperacion_NoDuplicaInventario()
    {
        using var db = CreateInMemoryDbContext();
        var producto = ProductoBase("WMS-0003");
        var origen = Ubicacion("ORIGEN", 100, true);
        var destino = Ubicacion("DESTINO", 100, false);
        var stockOrigen = Stock(producto.Id, origen, total: 10);
        var operacionId = Guid.NewGuid();

        db.AddRange(producto, origen, destino, stockOrigen);
        await db.SaveChangesAsync();

        var service = new InventoryService(db, NullLogger<InventoryService>.Instance);
        var primera = await service.TransferirStockAsync(stockOrigen.Id, destino.Id, 3, operacionId);
        var segunda = await service.TransferirStockAsync(stockOrigen.Id, destino.Id, 3, operacionId);

        primera.Exitoso.Should().BeTrue();
        primera.YaProcesada.Should().BeFalse();
        segunda.Exitoso.Should().BeTrue();
        segunda.YaProcesada.Should().BeTrue();

        var origenDb = await db.StockFisico.SingleAsync(s => s.Id == stockOrigen.Id);
        var destinoDb = await db.StockFisico.SingleAsync(s => s.ProductoId == producto.Id && s.UbicacionAlmacenId == destino.Id);

        origenDb.CantidadTotal.Should().Be(7);
        destinoDb.CantidadTotal.Should().Be(3);
        (await db.MovimientosInventario.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task TransferirStock_RespetaCapacidadDeUbicacionDestino()
    {
        using var db = CreateInMemoryDbContext();
        var producto = ProductoBase("WMS-0004");
        var otroProducto = ProductoBase("WMS-OTRO");
        var origen = Ubicacion("ORIGEN", 100, true);
        var destino = Ubicacion("DESTINO", 5, true);
        var stockOrigen = Stock(producto.Id, origen, total: 10);
        var stockOcupante = Stock(otroProducto.Id, destino, total: 4);

        db.AddRange(producto, otroProducto, origen, destino, stockOrigen, stockOcupante);
        await db.SaveChangesAsync();

        var service = new InventoryService(db, NullLogger<InventoryService>.Instance);
        var result = await service.TransferirStockAsync(
            stockOrigen.Id, destino.Id, 2, Guid.NewGuid());

        result.Exitoso.Should().BeFalse();
        result.Mensaje.Should().Contain("excede la capacidad");

        var origenDb = await db.StockFisico.SingleAsync(s => s.Id == stockOrigen.Id);
        origenDb.CantidadTotal.Should().Be(10);
        (await db.StockFisico.Where(s => s.UbicacionAlmacenId == destino.Id).SumAsync(s => s.CantidadTotal)).Should().Be(4);
    }

    private static Producto ProductoBase(string sku) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = $"Producto {sku}",
        PrecioB2C = 10m,
        PrecioB2B = 8m,
        StockMinimo = 2,
        VendedorId = Guid.NewGuid(),
        CategoriaId = Guid.NewGuid()
    };

    private static UbicacionAlmacen Ubicacion(string sufijo, int capacidad, bool ocupada) => new()
    {
        Id = Guid.NewGuid(),
        Codigo = $"PAS-A-RCK-01-NIV-01-POS-{sufijo}",
        Pasillo = "Pasillo A",
        Rack = "Rack 01",
        Nivel = 1,
        Posicion = sufijo,
        CapacidadMaxima = capacidad,
        EstaOcupada = ocupada,
        Zona = "AlmacenamientoGeneral"
    };

    private static StockFisico Stock(
        Guid productoId,
        UbicacionAlmacen ubicacion,
        int total,
        int reservado = 0,
        int bloqueado = 0) => new()
    {
        Id = Guid.NewGuid(),
        ProductoId = productoId,
        CantidadTotal = total,
        CantidadReservada = reservado,
        CantidadBloqueada = bloqueado,
        PuntoReorden = 2,
        UbicacionAlmacenId = ubicacion.Id,
        UbicacionCodigo = ubicacion.Codigo
    };
}
