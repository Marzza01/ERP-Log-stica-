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

public class ConteoCiclicoServiceTests
{
    [Fact]
    public async Task PlanificarEIniciar_NoDebeModificarStock()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(12, 2, 1);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var plan = await sut.PlanificarAsync(stock.Id, "Conteo semanal", "operador");
        var inicio = await sut.IniciarAsync(plan.Conteo!.Id, "operador");

        plan.Exitoso.Should().BeTrue();
        inicio.Exitoso.Should().BeTrue();
        inicio.Conteo!.CantidadSistema.Should().Be(12);
        inicio.Conteo.Estado.Should().Be(EstadoConteoCiclico.EnConteo);
        stock.CantidadTotal.Should().Be(12);
        (await db.AjustesInventario.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PrimerConteo_CoincideConSistema_DebeCompletarSinAjuste()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(7, 0, 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var plan = await sut.PlanificarAsync(stock.Id, null, "operador");
        await sut.IniciarAsync(plan.Conteo!.Id, "operador");
        var result = await sut.RegistrarPrimerConteoAsync(plan.Conteo.Id, 7, "operador");

        result.Exitoso.Should().BeTrue();
        result.Conteo!.Estado.Should().Be(EstadoConteoCiclico.Completado);
        result.Conteo.PrimerConteo.Should().Be(7);
        result.Conteo.AjusteInventarioId.Should().BeNull();
        stock.CantidadTotal.Should().Be(7);
        (await db.AjustesInventario.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DiferenciaConfirmadaEnReconteo_DebeGenerarAjustePendienteSinCambiarStock()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(10, 0, 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var plan = await sut.PlanificarAsync(stock.Id, null, "operador");
        await sut.IniciarAsync(plan.Conteo!.Id, "operador");
        var primero = await sut.RegistrarPrimerConteoAsync(plan.Conteo.Id, 8, "operador");
        var reconteo = await sut.RegistrarReconteoAsync(plan.Conteo.Id, 8, "operador");

        primero.Conteo!.Estado.Should().Be(EstadoConteoCiclico.RequiereReconteo);
        reconteo.Exitoso.Should().BeTrue();
        reconteo.Conteo!.Estado.Should().Be(EstadoConteoCiclico.CompletadoConAjuste);
        reconteo.Conteo.AjusteInventarioId.Should().NotBeNull();
        stock.CantidadTotal.Should().Be(10);

        var ajuste = await db.AjustesInventario.SingleAsync();
        ajuste.Tipo.Should().Be(TipoAjusteInventario.Negativo);
        ajuste.Cantidad.Should().Be(2);
        ajuste.Estado.Should().Be(EstadoAjusteInventario.PendienteAprobacion);
        ajuste.Referencia.Should().StartWith("CONTEO-");
    }

    [Fact]
    public async Task Reconteo_Reintento_NoDebeCrearAjusteDuplicado()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(5, 0, 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var plan = await sut.PlanificarAsync(stock.Id, null, "operador");
        await sut.IniciarAsync(plan.Conteo!.Id, "operador");
        await sut.RegistrarPrimerConteoAsync(plan.Conteo.Id, 6, "operador");

        var primera = await sut.RegistrarReconteoAsync(plan.Conteo.Id, 6, "operador");
        var segunda = await sut.RegistrarReconteoAsync(plan.Conteo.Id, 6, "operador");

        primera.Exitoso.Should().BeTrue();
        segunda.Exitoso.Should().BeTrue();
        (await db.AjustesInventario.CountAsync()).Should().Be(1);
        stock.CantidadTotal.Should().Be(5);
    }

    [Fact]
    public async Task RegistrarConteo_SiStockCambioDesdeSnapshot_DebeRechazarDatosObsoletos()
    {
        await using var db = Db();
        var (producto, ubicacion, stock) = Seed(9, 0, 0);
        db.AddRange(producto, ubicacion, stock);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        var plan = await sut.PlanificarAsync(stock.Id, null, "operador");
        await sut.IniciarAsync(plan.Conteo!.Id, "operador");

        stock.CantidadTotal = 10;
        await db.SaveChangesAsync();

        var result = await sut.RegistrarPrimerConteoAsync(plan.Conteo.Id, 9, "operador");

        result.Exitoso.Should().BeFalse();
        result.Mensaje.Should().Contain("stock cambió");
        (await db.AjustesInventario.CountAsync()).Should().Be(0);
    }

    private static CatalogDbContext Db()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"Conteos_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    private static ConteoCiclicoService Sut(CatalogDbContext db)
    {
        var ajustes = new AjusteInventarioService(db, NullLogger<AjusteInventarioService>.Instance);
        return new ConteoCiclicoService(db, ajustes, NullLogger<ConteoCiclicoService>.Instance);
    }

    private static (Producto producto, UbicacionAlmacen ubicacion, StockFisico stock) Seed(int total, int reservado, int bloqueado)
    {
        var producto = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"CC-{Guid.NewGuid():N}"[..12],
            Nombre = "Producto conteo",
            CategoriaNombre = "Pruebas",
            PrecioB2C = 10,
            PrecioB2B = 8,
            StockMinimo = 2,
            VendedorId = Guid.NewGuid(),
            CategoriaId = Guid.NewGuid(),
            EstadoProducto = "Activo"
        };
        var ubicacion = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(),
            Codigo = $"UB-{Guid.NewGuid():N}"[..10],
            Pasillo = "A",
            Rack = "01",
            Nivel = 1,
            Posicion = "01",
            CapacidadMaxima = 100,
            EstaOcupada = total > 0,
            Zona = "General"
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
            UbicacionCodigo = ubicacion.Codigo
        };
        return (producto, ubicacion, stock);
    }
}
