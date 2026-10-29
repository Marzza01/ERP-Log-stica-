using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// I4: regresiones de reservas WMS. Pruebas en memoria: NO sustituyen
/// pruebas de locks y transacciones en PostgreSQL QA.
/// </summary>
public sealed class RegresionReservasI4Tests
{
    private static CatalogDbContext CrearDb() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"RegresionI4_{Guid.NewGuid():N}").Options);

    private static async Task<(StockService sut, StockFisico saldo, Producto producto)> PrepararAsync(
        CatalogDbContext db, string? ttl = null)
    {
        var producto = new Producto
        {
            Id = Guid.NewGuid(), Sku = $"I4-{Guid.NewGuid():N}",
            Nombre = "Producto de regresion I4", CategoriaNombre = "QA",
            PrecioB2C = 10m, PrecioB2B = 8m, VendedorId = Guid.NewGuid(),
            CategoriaId = Guid.NewGuid(), EstaPublicado = true
        };
        var ubicacion = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(), Codigo = "PAS-I4-RCK-01-NIV-01-POS-01",
            Pasillo = "I4", Rack = "01", Nivel = 1, Posicion = "01",
            Zona = "AlmacenamientoGeneral"
        };
        var saldo = new StockFisico
        {
            Id = Guid.NewGuid(), ProductoId = producto.Id,
            CantidadTotal = 12, CantidadBloqueada = 2,
            UbicacionAlmacenId = ubicacion.Id,
            UbicacionCodigo = ubicacion.Codigo
        };
        db.AddRange(producto, ubicacion, saldo);
        await db.SaveChangesAsync();
        IConfiguration? config = null;
        if (ttl is not null)
        {
            var mock = new Mock<IConfiguration>();
            mock.Setup(x => x["Reservas:TTLMinutos"]).Returns(ttl);
            config = mock.Object;
        }
        return (new StockService(db, NullLogger<StockService>.Instance, config), saldo, producto);
    }

    [Fact]
    public async Task Reserva_Nueva_UsaVigencia30MinutosPorDefecto()
    {
        await using var db = CrearDb();
        var (sut, saldo, producto) = await PrepararAsync(db);
        var inicio = DateTime.UtcNow;
        var resultado = await sut.ReservarStockAsync(producto.Id, 4, Guid.NewGuid());
        Assert.True(resultado.Exitoso);
        var reserva = await db.ReservasStock.SingleAsync();
        Assert.NotNull(reserva.FechaExpiracion);
        Assert.InRange((reserva.FechaExpiracion!.Value - inicio).TotalMinutes, 29.9, 30.1);
        Assert.Equal(12, saldo.CantidadTotal);
        Assert.Equal(4, saldo.CantidadReservada);
        Assert.Equal(2, saldo.CantidadBloqueada);
        Assert.Equal(6, saldo.CantidadDisponible);
    }

    [Fact]
    public async Task Reserva_Nueva_RespetaVigenciaConfigurable()
    {
        await using var db = CrearDb();
        var (sut, _, producto) = await PrepararAsync(db, "5");
        var inicio = DateTime.UtcNow;
        var resultado = await sut.ReservarStockAsync(producto.Id, 1, Guid.NewGuid());
        Assert.True(resultado.Exitoso);
        var reserva = await db.ReservasStock.SingleAsync();
        Assert.NotNull(reserva.FechaExpiracion);
        Assert.InRange((reserva.FechaExpiracion!.Value - inicio).TotalMinutes, 4.9, 5.1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("1441")]
    [InlineData("no-numero")]
    public async Task Configuracion_Invalida_BloqueaServicio(string ttl)
    {
        await using var db = CrearDb();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PrepararAsync(db, ttl));
        Assert.Empty(await db.ReservasStock.ToListAsync());
    }

    [Fact]
    public async Task Confirmacion_NoDescuentaFisicoNiDuplicaReserva()
    {
        await using var db = CrearDb();
        var (sut, saldo, producto) = await PrepararAsync(db);
        var orden = Guid.NewGuid();
        var r1 = await sut.ReservarStockAsync(producto.Id, 3, orden);
        var r2 = await sut.ReservarStockAsync(producto.Id, 3, orden);
        Assert.True(r1.Exitoso);
        Assert.True(r2.Exitoso);
        Assert.Equal(r1.ReservaId, r2.ReservaId);
        await sut.ConfirmarReservaAsync(r1.ReservaId);
        Assert.Equal(EstadoReserva.Confirmada, (await db.ReservasStock.SingleAsync()).Estado);
        Assert.Equal(12, saldo.CantidadTotal);
        Assert.Equal(3, saldo.CantidadReservada);
        Assert.Single(await db.ReservasStock.ToListAsync());
    }

    [Fact]
    public async Task Liberacion_DobleEsIdempotente_YNoDescuentaFisico()
    {
        await using var db = CrearDb();
        var (sut, saldo, producto) = await PrepararAsync(db);
        var r = await sut.ReservarStockAsync(producto.Id, 3, Guid.NewGuid());
        Assert.True(r.Exitoso);
        await sut.LiberarStockAsync(r.ReservaId);
        await sut.LiberarStockAsync(r.ReservaId);
        Assert.Equal(12, saldo.CantidadTotal);
        Assert.Equal(0, saldo.CantidadReservada);
        var reserva = await db.ReservasStock.SingleAsync();
        Assert.Equal(3, reserva.CantidadLiberada);
        Assert.Equal(0, reserva.CantidadPendiente);
        Assert.Equal(EstadoReserva.Liberada, reserva.Estado);
        Assert.Single(await db.MovimientosInventario
            .Where(m => m.TipoMovimiento == TipoMovimientoInventario.LiberacionReserva)
            .ToListAsync());
    }
}
