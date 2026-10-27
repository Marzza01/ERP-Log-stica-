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
/// Pruebas del StockService real de Fase 4B.
/// Usa EF InMemory para validar reglas de negocio; el bloqueo FOR UPDATE se
/// ejerce únicamente con PostgreSQL en ejecución real.
/// </summary>
public class StockServiceTests
{
    [Fact]
    public async Task ReservarStockAsync_DebeDistribuirReservaEntreUbicaciones_SinReducirFisico()
    {
        await using var db = CrearDb();
        var producto = CrearProducto();
        var ubicacionA = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01");
        var ubicacionB = CrearUbicacion("PAS-B-RCK-01-NIV-01-POS-01");

        var stockA = CrearStock(producto.Id, ubicacionA, total: 4);
        var stockB = CrearStock(producto.Id, ubicacionB, total: 6);

        db.AddRange(producto, ubicacionA, ubicacionB, stockA, stockB);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var ordenId = Guid.NewGuid();

        var result = await sut.ReservarStockAsync(producto.Id, 7, ordenId);

        result.Exitoso.Should().BeTrue();
        result.ReservaId.Should().NotBeEmpty();

        var stocks = await db.StockFisico.OrderBy(s => s.UbicacionCodigo).ToListAsync();
        stocks.Sum(s => s.CantidadTotal).Should().Be(10, "reservar nunca reduce el físico");
        stocks.Sum(s => s.CantidadReservada).Should().Be(7);
        stocks.Sum(s => s.CantidadDisponible).Should().Be(3);

        var reserva = await db.ReservasStock.SingleAsync();
        reserva.CantidadOriginal.Should().Be(7);
        reserva.Estado.Should().Be(EstadoReserva.Pendiente);

        var detalles = await db.ReservasStockDetalle.Where(d => d.ReservaStockId == reserva.Id).ToListAsync();
        detalles.Should().HaveCount(2);
        detalles.Sum(d => d.CantidadAsignada).Should().Be(7);

        var movimientos = await db.MovimientosInventario
            .Where(m => m.ReservaStockId == reserva.Id && m.TipoMovimiento == TipoMovimientoInventario.Reserva)
            .ToListAsync();
        movimientos.Sum(m => m.Cantidad).Should().Be(7);
    }

    [Fact]
    public async Task ReservarStockAsync_DebeRespetarStockBloqueado()
    {
        await using var db = CrearDb();
        var producto = CrearProducto();
        var ubicacion = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01");
        var stock = CrearStock(producto.Id, ubicacion, total: 10, bloqueado: 4);

        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var result = await sut.ReservarStockAsync(producto.Id, 7, Guid.NewGuid());

        result.Exitoso.Should().BeFalse();
        result.ReservaId.Should().BeEmpty();
        stock.CantidadReservada.Should().Be(0);
        stock.CantidadTotal.Should().Be(10);
        (await db.ReservasStock.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReservarStockAsync_DebeSerIdempotenteParaMismaOrdenProductoYCantidad()
    {
        await using var db = CrearDb();
        var producto = CrearProducto();
        var ubicacion = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01");
        var stock = CrearStock(producto.Id, ubicacion, total: 10);

        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var ordenId = Guid.NewGuid();

        var primera = await sut.ReservarStockAsync(producto.Id, 3, ordenId);
        var segunda = await sut.ReservarStockAsync(producto.Id, 3, ordenId);

        segunda.Exitoso.Should().BeTrue();
        segunda.ReservaId.Should().Be(primera.ReservaId);
        stock.CantidadReservada.Should().Be(3);
        (await db.ReservasStock.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ConfirmarReservaAsync_DebeConfirmarReservaSinConsumirFisico()
    {
        await using var db = CrearDb();
        var producto = CrearProducto();
        var ubicacion = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01");
        var stock = CrearStock(producto.Id, ubicacion, total: 8);

        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var creada = await sut.ReservarStockAsync(producto.Id, 3, Guid.NewGuid());

        await sut.ConfirmarReservaAsync(creada.ReservaId);

        var reserva = await db.ReservasStock.SingleAsync(r => r.Id == creada.ReservaId);
        reserva.Estado.Should().Be(EstadoReserva.Confirmada);
        reserva.FechaConfirmacion.Should().NotBeNull();
        stock.CantidadTotal.Should().Be(8);
        stock.CantidadReservada.Should().Be(3);
        stock.CantidadDisponible.Should().Be(5);
    }

    [Fact]
    public async Task LiberarStockAsync_DebeLiberarPendienteYConservarFisico()
    {
        await using var db = CrearDb();
        var producto = CrearProducto();
        var ubicacion = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01");
        var stock = CrearStock(producto.Id, ubicacion, total: 8);

        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var creada = await sut.ReservarStockAsync(producto.Id, 5, Guid.NewGuid());

        await sut.LiberarStockAsync(creada.ReservaId);

        var reserva = await db.ReservasStock.SingleAsync(r => r.Id == creada.ReservaId);
        reserva.CantidadLiberada.Should().Be(5);
        reserva.CantidadPendiente.Should().Be(0);
        reserva.Estado.Should().Be(EstadoReserva.Liberada);
        reserva.FechaFinalizacion.Should().NotBeNull();

        stock.CantidadTotal.Should().Be(8);
        stock.CantidadReservada.Should().Be(0);
        stock.CantidadDisponible.Should().Be(8);

        var liberado = await db.MovimientosInventario
            .Where(m => m.ReservaStockId == reserva.Id && m.TipoMovimiento == TipoMovimientoInventario.LiberacionReserva)
            .SumAsync(m => m.Cantidad);
        liberado.Should().Be(5);
    }

    [Fact]
    public async Task LiberarStockAsync_DebeSerIdempotente()
    {
        await using var db = CrearDb();
        var producto = CrearProducto();
        var ubicacion = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01");
        var stock = CrearStock(producto.Id, ubicacion, total: 8);

        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = CrearSut(db);
        var creada = await sut.ReservarStockAsync(producto.Id, 4, Guid.NewGuid());

        await sut.LiberarStockAsync(creada.ReservaId);
        await sut.LiberarStockAsync(creada.ReservaId);

        stock.CantidadReservada.Should().Be(0);
        var reserva = await db.ReservasStock.SingleAsync(r => r.Id == creada.ReservaId);
        reserva.CantidadLiberada.Should().Be(4);
        (await db.MovimientosInventario.CountAsync(m =>
            m.ReservaStockId == reserva.Id &&
            m.TipoMovimiento == TipoMovimientoInventario.LiberacionReserva)).Should().Be(1);
    }

    private static CatalogDbContext CrearDb()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"StockServiceDb_{Guid.NewGuid()}")
            .Options;

        return new CatalogDbContext(options);
    }

    private static StockService CrearSut(CatalogDbContext db) =>
        new(db, NullLogger<StockService>.Instance);

    private static Producto CrearProducto() => new()
    {
        Id = Guid.NewGuid(),
        Sku = "SKU-" + Guid.NewGuid().ToString("N")[..16],
        Nombre = "Producto prueba StockService",
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

    private static UbicacionAlmacen CrearUbicacion(string codigo) => new()
    {
        Id = Guid.NewGuid(),
        Codigo = codigo,
        Pasillo = codigo.Contains("PAS-B") ? "B" : "A",
        Rack = "01",
        Nivel = 1,
        Posicion = "01",
        CapacidadMaxima = 100,
        EstaOcupada = true,
        Zona = "AlmacenamientoGeneral",
        FechaCreacion = DateTime.UtcNow
    };

    private static StockFisico CrearStock(
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
        UbicacionCodigo = ubicacion.Codigo,
        UltimaActualizacion = DateTime.UtcNow
    };
}
