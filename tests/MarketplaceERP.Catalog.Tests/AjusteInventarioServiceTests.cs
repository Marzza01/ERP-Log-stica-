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

public class AjusteInventarioServiceTests
{
    [Fact]
    public async Task CrearAsync_NoDebeModificarStockHastaAplicar()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(10, 0, 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var creado = await sut.CrearAsync(stock.Id, TipoAjusteInventario.Positivo, 3, "Existencia encontrada", null, "AJ-001", "admin");

        creado.Exitoso.Should().BeTrue();
        creado.Ajuste!.Estado.Should().Be(EstadoAjusteInventario.PendienteAprobacion);
        stock.CantidadTotal.Should().Be(10);
        (await db.MovimientosInventario.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AplicarAsync_AjustePositivoAprobado_DebeIncrementarFisicoYCrearMovimiento()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(10, 0, 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var creado = await sut.CrearAsync(stock.Id, TipoAjusteInventario.Positivo, 3, "Hallazgo conteo", "Prueba", null, "operador");
        await sut.AprobarAsync(creado.Ajuste!.Id, "admin");
        var aplicado = await sut.AplicarAsync(creado.Ajuste.Id, "admin");

        aplicado.Exitoso.Should().BeTrue();
        aplicado.Ajuste!.Estado.Should().Be(EstadoAjusteInventario.Aplicado);
        stock.CantidadTotal.Should().Be(13);
        var movimiento = await db.MovimientosInventario.SingleAsync();
        movimiento.TipoMovimiento.Should().Be(TipoMovimientoInventario.AjustePositivo);
        movimiento.Cantidad.Should().Be(3);
    }

    [Fact]
    public async Task AplicarAsync_AjusteNegativo_NoDebeConsumirReservadoNiBloqueado()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(10, 4, 2); // disponible = 4
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var creado = await sut.CrearAsync(stock.Id, TipoAjusteInventario.Negativo, 4, "Diferencia de conteo", null, null, "operador");
        await sut.AprobarAsync(creado.Ajuste!.Id, "admin");
        var aplicado = await sut.AplicarAsync(creado.Ajuste.Id, "admin");

        aplicado.Exitoso.Should().BeTrue();
        stock.CantidadTotal.Should().Be(6);
        stock.CantidadReservada.Should().Be(4);
        stock.CantidadBloqueada.Should().Be(2);
        stock.CantidadDisponible.Should().Be(0);
    }

    [Fact]
    public async Task AplicarAsync_Reintento_NoDebeDuplicarAjuste()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(8, 0, 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var creado = await sut.CrearAsync(stock.Id, TipoAjusteInventario.Positivo, 2, "Corrección", null, null, "operador");
        await sut.AprobarAsync(creado.Ajuste!.Id, "admin");
        var primera = await sut.AplicarAsync(creado.Ajuste.Id, "admin");
        var segunda = await sut.AplicarAsync(creado.Ajuste.Id, "admin");

        primera.Exitoso.Should().BeTrue();
        segunda.Exitoso.Should().BeTrue();
        stock.CantidadTotal.Should().Be(10);
        (await db.MovimientosInventario.CountAsync(m => m.TipoMovimiento == TipoMovimientoInventario.AjustePositivo)).Should().Be(1);
    }

    private static CatalogDbContext Db()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"Ajustes_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    private static AjusteInventarioService Sut(CatalogDbContext db) =>
        new(db, NullLogger<AjusteInventarioService>.Instance);

    private static (Producto producto, UbicacionAlmacen ubicacion, StockFisico stock) Seed(int total, int reservado, int bloqueado)
    {
        var producto = new Producto
        {
            Id = Guid.NewGuid(), Sku = $"AJ-{Guid.NewGuid():N}"[..12], Nombre = "Producto ajuste",
            CategoriaNombre = "Pruebas", PrecioB2C = 10, PrecioB2B = 8, StockMinimo = 2,
            VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid(), EstadoProducto = "Activo"
        };
        var ubicacion = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(), Codigo = $"UB-{Guid.NewGuid():N}"[..10], Pasillo = "A", Rack = "01",
            Nivel = 1, Posicion = "01", CapacidadMaxima = 100, EstaOcupada = total > 0, Zona = "General"
        };
        var stock = new StockFisico
        {
            Id = Guid.NewGuid(), ProductoId = producto.Id, CantidadTotal = total,
            CantidadReservada = reservado, CantidadBloqueada = bloqueado, PuntoReorden = 2,
            UbicacionAlmacenId = ubicacion.Id, UbicacionCodigo = ubicacion.Codigo
        };
        return (producto, ubicacion, stock);
    }
}
