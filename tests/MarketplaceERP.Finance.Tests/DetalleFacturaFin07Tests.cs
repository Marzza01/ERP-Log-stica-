using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class DetalleFacturaFin07Tests
{
    private static FinanceDbContext Db() => new(new DbContextOptionsBuilder<FinanceDbContext>()
        .UseInMemoryDatabase("fin07-" + Guid.NewGuid().ToString("N")).Options);

    private static (OrdenCompra Po, FacturaProveedor Factura) Crear(
        bool withLines = true)
    {
        var po = new OrdenCompra
        {
            Id = Guid.NewGuid(),
            NumeroPO = "PO-FIN07-" + Guid.NewGuid().ToString("N"),
            ProveedorId = Guid.NewGuid(), ProveedorNombre = "Proveedor CI",
            Estado = EstadoOrdenCompra.RecibidaCompleta,
            SubTotal = 40m, ImpuestoIva = 5.20m, MontoTotal = 45.20m,
            Items = new List<ItemOrdenCompra>
            {
                new()
                {
                    Id = Guid.NewGuid(), Sku = "A", NombreProducto = "Producto A",
                    CantidadSolicitada = 2, CantidadRecibida = 2,
                    PrecioUnitarioAcordado = 10m
                },
                new()
                {
                    Id = Guid.NewGuid(), Sku = "B", NombreProducto = "Producto B",
                    CantidadSolicitada = 2, CantidadRecibida = 2,
                    PrecioUnitarioAcordado = 10m
                }
            }
        };
        var factura = new FacturaProveedor
        {
            Id = Guid.NewGuid(), OrdenCompraId = po.Id,
            ProveedorId = po.ProveedorId,
            NumeroFactura = "CCF-FIN07-" + Guid.NewGuid().ToString("N"),
            MontoSubTotal = 40m, MontoIva = 5.20m, MontoTotal = 45.20m,
            FechaEmision = DateTime.UtcNow, FechaVencimiento = DateTime.UtcNow.AddDays(30)
        };
        if (withLines)
        {
            factura.Items = po.Items.Select(item => new ItemFacturaProveedor
            {
                Id = Guid.NewGuid(),
                ItemOrdenCompraId = item.Id,
                CantidadFacturada = 2,
                PrecioUnitarioFacturado = 10m
            }).ToList();
        }
        return (po, factura);
    }

    private static TresWayMatchingService Matching(FinanceDbContext db) =>
        new(db, NullLogger<TresWayMatchingService>.Instance);

    private static ComprasController Controller(FinanceDbContext db)
    {
        var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("fin07-core-" + Guid.NewGuid().ToString("N")).Options);
        return new ComprasController(db, Matching(db),
            Mock.Of<IContabilidadService>(), Mock.Of<IInventoryService>(),
            core, NullLogger<ComprasController>.Instance);
    }

    [Fact]
    public async Task LineasReales_Validas_ConformeYTotalFacturadoIndependiente()
    {
        await using var db = Db();
        var (po, factura) = Crear();
        db.OrdenesCompra.Add(po);
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();

        var match = await Matching(db).EjecutarMatchAsync(new(po.Id, factura.Id, 4));
        Assert.Equal(Estado3WayMatch.Conforme, match.Resultado);
        Assert.True(match.AutorizadoParaPago);
        Assert.Equal(4, match.CantidadFacturada);
    }

    [Fact]
    public async Task MismoTotalYUnidadesPeroDistribucionDeItemsIncorrecta_NoAutoriza()
    {
        await using var db = Db();
        var (po, factura) = Crear();
        var lineas = factura.Items.ToArray();
        lineas[0].CantidadFacturada = 3;
        lineas[1].CantidadFacturada = 1;
        db.OrdenesCompra.Add(po);
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();

        var match = await Matching(db).EjecutarMatchAsync(new(po.Id, factura.Id, 4));
        Assert.Equal(Estado3WayMatch.DiscrepanciaCantidad, match.Resultado);
        Assert.False(match.AutorizadoParaPago);
        Assert.Equal(4, match.CantidadFacturada);
        Assert.False(factura.AprobadaParaPago);
        Assert.Contains("SKU", match.DetalleDiscrepancias);
    }

    [Fact]
    public async Task MismoImporteGlobalPeroPreciosPorProductoDistintos_NoAutoriza()
    {
        await using var db = Db();
        var (po, factura) = Crear();
        var lineas = factura.Items.ToArray();
        lineas[0].PrecioUnitarioFacturado = 9m;
        lineas[1].PrecioUnitarioFacturado = 11m;
        db.OrdenesCompra.Add(po);
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();

        var match = await Matching(db).EjecutarMatchAsync(new(po.Id, factura.Id, 4));
        Assert.Equal(Estado3WayMatch.DiscrepanciaPrecio, match.Resultado);
        Assert.False(match.AutorizadoParaPago);
        Assert.Equal(45.20m, match.MontoTotalFactura);
    }

    [Fact]
    public async Task FacturaHistoricaSinLineas_SeConservaPeroNoAutoriza()
    {
        await using var db = Db();
        var (po, factura) = Crear(withLines: false);
        db.OrdenesCompra.Add(po);
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Matching(db).EjecutarMatchAsync(new(po.Id, factura.Id, 4)));
        Assert.False(factura.AprobadaParaPago);
        Assert.Empty(await db.MatchesTridireccionales.ToListAsync());
    }

    [Fact]
    public async Task FacturaHeredada_SeCompletaConDocumentoRealYSePuedeConciliar()
    {
        await using var db = Db();
        var (po, factura) = Crear(withLines: false);
        db.OrdenesCompra.Add(po);
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();

        var lineas = po.Items.Select(x =>
            new LineaFacturaProveedorRequest(x.Id, 2, 10m)).ToList();
        var result = await Controller(db).CompletarDetalleFactura(
            factura.Id, lineas, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(2, await db.ItemsFacturaProveedor.CountAsync());

        var match = await Matching(db).EjecutarMatchAsync(new(po.Id, factura.Id, 4));
        Assert.Equal(Estado3WayMatch.Conforme, match.Resultado);
    }

    [Fact]
    public async Task RegistroFacturaConDetalle_SeGuardaYConciliacionUsaCantidadesReales()
    {
        await using var db = Db();
        var (po, _) = Crear();
        db.OrdenesCompra.Add(po);
        await db.SaveChangesAsync();

        var lineas = po.Items.Select(x =>
            new LineaFacturaProveedorRequest(x.Id, 2, 10m)).ToList();

        var response = await Controller(db).RegistrarFactura(
            new RegistrarFacturaRequest(po.Id, "CCF-DETALLE-VERIFICADO",
                40m, 45.20m, null, null, lineas),
            CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(response);

        var factura = await db.FacturasProveedor.AsNoTracking()
            .Include(x => x.Items).SingleAsync();
        Assert.Equal(2, factura.Items.Count);
        Assert.Equal(4, factura.Items.Sum(x => x.CantidadFacturada));
        Assert.Equal(40m, factura.Items.Sum(x =>
            x.CantidadFacturada * x.PrecioUnitarioFacturado));

        var match = await Matching(db).EjecutarMatchAsync(new(po.Id, factura.Id, 4));
        Assert.True(match.AutorizadoParaPago);
        Assert.Equal(4, match.CantidadFacturada);
    }

    [Fact]
    public async Task RegistroFacturas_SinDetalleAceptadoPeroNoAutorizable()
    {
        await using var db = Db();
        var (po, _) = Crear();
        db.OrdenesCompra.Add(po);
        await db.SaveChangesAsync();

        var response = await Controller(db).RegistrarFactura(
            new RegistrarFacturaRequest(po.Id, "CCF-SIN-DETALLE", 40m, 45.20m, null, null),
            CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(response);
        var factura = Assert.Single(await db.FacturasProveedor.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Matching(db).EjecutarMatchAsync(new(po.Id, factura.Id, 4)));
    }

    [Theory]
    [InlineData("duplicada")]
    [InlineData("ajena")]
    [InlineData("subtotal")]
    [InlineData("precio-fraccionado")]
    [InlineData("cantidad-invalida")]
    public async Task RegistroDetalleInvalido_Devuelve400SinPersistir(string caso)
    {
        await using var db = Db();
        var (po, _) = Crear();
        db.OrdenesCompra.Add(po);
        await db.SaveChangesAsync();

        var lines = po.Items.Select(x =>
            new LineaFacturaProveedorRequest(x.Id, 2, 10m)).ToList();
        switch (caso)
        {
            case "duplicada": lines[1] = lines[0]; break;
            case "ajena": lines[1] = new(Guid.NewGuid(), 2, 10m); break;
            case "subtotal": lines[0] = new(lines[0].ItemOrdenCompraId, 3, 10m); break;
            case "precio-fraccionado": lines[0] = new(lines[0].ItemOrdenCompraId, 2, 10.001m); break;
            case "cantidad-invalida": lines[0] = new(lines[0].ItemOrdenCompraId, 0, 10m); break;
        }

        var result = await Controller(db).RegistrarFactura(
            new RegistrarFacturaRequest(po.Id, "CCF-ERR-" + caso,
                40m, 45.20m, null, null, lines),
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.FacturasProveedor.ToListAsync());
        Assert.Empty(await db.ItemsFacturaProveedor.ToListAsync());
    }

    [Fact]
    public async Task CompletarDetallePorSegundaVez_NoReemplazaEvidencia()
    {
        await using var db = Db();
        var (po, factura) = Crear();
        db.OrdenesCompra.Add(po);
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();

        var lineas = po.Items.Select(x =>
            new LineaFacturaProveedorRequest(x.Id, 2, 10m)).ToList();
        var response = await Controller(db).CompletarDetalleFactura(
            factura.Id, lineas, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(response);
        Assert.Equal(2, await db.ItemsFacturaProveedor.CountAsync());
    }
}
