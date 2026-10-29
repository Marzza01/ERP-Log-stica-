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

public class ReabastecimientoServiceTests
{
    private static CatalogDbContext Db()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"Reabastecimiento_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    [Fact]
    public async Task ObtenerNecesidades_IncluyeProductoSinStockFisico()
    {
        using var db = Db();
        db.Productos.Add(Producto("REB-001", stockMinimo: 5));
        await db.SaveChangesAsync();

        var servicio = new ReabastecimientoService(db);
        var necesidades = await servicio.ObtenerNecesidadesAsync();

        necesidades.Should().ContainSingle();
        necesidades[0].CantidadDisponible.Should().Be(0);
        necesidades[0].NivelCriticidad.Should().Be("AGOTADO");
        necesidades[0].CantidadParaSalirAlerta.Should().Be(6);
    }

    [Fact]
    public async Task ObtenerNecesidades_UsaDisponibleReal_MenosReservadoYBloqueado()
    {
        using var db = Db();
        var producto = Producto("REB-002", stockMinimo: 6);
        producto.Stocks.Add(new StockFisico
        {
            Id = Guid.NewGuid(),
            ProductoId = producto.Id,
            CantidadTotal = 10,
            CantidadReservada = 3,
            CantidadBloqueada = 2,
            PuntoReorden = 6,
            UbicacionCodigo = "A-01"
        });
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var servicio = new ReabastecimientoService(db);
        var necesidad = (await servicio.ObtenerNecesidadesAsync()).Single();

        necesidad.CantidadFisica.Should().Be(10);
        necesidad.CantidadDisponible.Should().Be(5);
        necesidad.FaltanteHastaMinimo.Should().Be(1);
        necesidad.CantidadParaSalirAlerta.Should().Be(2);
        necesidad.NivelCriticidad.Should().Be("BAJO_MINIMO");
    }

    [Fact]
    public async Task ObtenerNecesidades_NoIncluyeProductoPorEncimaDelMinimo()
    {
        using var db = Db();
        var producto = Producto("REB-003", stockMinimo: 5);
        producto.Stocks.Add(new StockFisico
        {
            Id = Guid.NewGuid(),
            ProductoId = producto.Id,
            CantidadTotal = 8,
            CantidadReservada = 0,
            CantidadBloqueada = 0,
            PuntoReorden = 5,
            UbicacionCodigo = "A-01"
        });
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var servicio = new ReabastecimientoService(db);
        var necesidades = await servicio.ObtenerNecesidadesAsync();

        necesidades.Should().BeEmpty();
    }

    private static Producto Producto(string sku, int stockMinimo) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = $"Producto {sku}",
        CategoriaNombre = "Prueba",
        ProveedorNombre = "Proveedor Prueba",
        EstadoProducto = "Activo",
        EstaPublicado = true,
        StockMinimo = stockMinimo,
        PrecioB2C = 10m,
        PrecioB2B = 8m,
        VendedorId = Guid.NewGuid(),
        CategoriaId = Guid.NewGuid()
    };
}
