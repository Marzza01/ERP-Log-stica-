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

public class FulfillmentServiceTests
{
    private CatalogDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new CatalogDbContext(options);
    }

    private static StockService CreateStockService(CatalogDbContext db) =>
        new(db, NullLogger<StockService>.Instance);

    private static FulfillmentService CreateFulfillmentService(CatalogDbContext db, StockService stockService) =>
        new(db, stockService, NullLogger<FulfillmentService>.Instance);

    [Fact]
    public async Task GenerarPickList_UsaUbicacionesReservadas_YOrdenaRutaSShape()
    {
        await using var db = CreateInMemoryDbContext();
        var stockService = CreateStockService(db);
        var service = CreateFulfillmentService(db, stockService);

        var prod1 = CrearProducto("SKU-PROD-B");
        var prod2 = CrearProducto("SKU-PROD-A");

        var ubiB = CrearUbicacion("PAS-B-RCK-01-NIV-01-POS-01", "B");
        var ubiA = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01", "A");

        db.Productos.AddRange(prod1, prod2);
        db.UbicacionesAlmacen.AddRange(ubiB, ubiA);
        db.StockFisico.AddRange(
            CrearStock(prod1.Id, ubiB, 50),
            CrearStock(prod2.Id, ubiA, 50)
        );
        await db.SaveChangesAsync();

        var ordenId = Guid.NewGuid();
        var reserva1 = await stockService.ReservarStockAsync(prod1.Id, 2, ordenId);
        var reserva2 = await stockService.ReservarStockAsync(prod2.Id, 3, ordenId);
        await stockService.ConfirmarReservaAsync(reserva1.ReservaId);
        await stockService.ConfirmarReservaAsync(reserva2.ReservaId);

        var request = new GenerarPickListRequest(
            OrdenId: ordenId,
            NumeroOrden: "ORD-TEST-001",
            OperadorAsignado: "Carlos Perez",
            Items: new List<ItemPickingSolicitud>
            {
                new(prod1.Id, prod1.Sku, "Producto B", 2),
                new(prod2.Id, prod2.Sku, "Producto A", 3)
            }
        );

        var pickList = await service.GenerarPickListAsync(request);

        pickList.Items.Should().HaveCount(2);
        pickList.Items.First().Pasillo.Should().Be("A");
        pickList.Items.Last().Pasillo.Should().Be("B");
        pickList.Estado.Should().Be(EstadoPicking.Pendiente);
    }

    [Fact]
    public async Task ConfirmarPicking_ActualizaEstadoACompletado_CuandoTodosSonRecogidos()
    {
        await using var db = CreateInMemoryDbContext();
        var stockService = CreateStockService(db);
        var service = CreateFulfillmentService(db, stockService);

        var item = new ItemPickList
        {
            Id = Guid.NewGuid(),
            Sku = "SKU-001",
            NombreProducto = "Taladro Percutor",
            CantidadSolicitada = 2,
            CantidadRecogida = 0,
            Pasillo = "A",
            Rack = "01",
            Nivel = 1,
            Posicion = "01"
        };

        var pickList = new PickList
        {
            Id = Guid.NewGuid(),
            NumeroPickList = "PKL-20260918-001",
            NumeroOrden = "ORD-TEST-002",
            Estado = EstadoPicking.Pendiente,
            Items = new List<ItemPickList> { item }
        };

        db.PickLists.Add(pickList);
        await db.SaveChangesAsync();

        var confirmaciones = new List<ConfirmarItemPickingRequest>
        {
            new(item.Id, CantidadRecogida: 2)
        };

        var resultado = await service.ConfirmarPickingAsync(pickList.Id, confirmaciones);

        resultado.Should().NotBeNull();
        resultado!.Estado.Should().Be(EstadoPicking.Completado);
        resultado.FechaFin.Should().NotBeNull();
        resultado.Items.First().Recogido.Should().BeTrue();
    }

    [Fact]
    public async Task EmpacarYRotular_GeneraGuiaEnvio_YCalculaVolumenM3()
    {
        await using var db = CreateInMemoryDbContext();
        var stockService = CreateStockService(db);
        var service = CreateFulfillmentService(db, stockService);

        var ordenId = Guid.NewGuid();
        db.PickLists.Add(new PickList
        {
            Id = Guid.NewGuid(),
            OrdenId = ordenId,
            NumeroOrden = "ORD-TEST-003",
            NumeroPickList = "PKL-TEST-003",
            Estado = EstadoPicking.Completado,
            FechaFin = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var request = new EmpacarDespachoRequest(
            OrdenId: ordenId,
            NumeroOrden: "ORD-TEST-003",
            PesoRealKg: 2.5m,
            LargoCm: 30,
            AnchoCm: 20,
            AltoCm: 10,
            Transportista: "LOGIMKT Express",
            DepartamentoDestino: "San Salvador",
            MunicipioDestino: "Escalón",
            DireccionEntrega: "Paseo General Escalón #1234",
            ContactoReceptor: "Juan Morales",
            TelefonoReceptor: "7000-1234"
        );

        var paquete = await service.EmpacarYRotularAsync(request);

        paquete.NumeroGuia.Should().StartWith("SV-LOGI-");
        paquete.Estado.Should().Be(EstadoPacking.ListoParaDespacho);
        paquete.VolumenM3.Should().Be(0.006m);
    }


    [Fact]
    public async Task ConfirmarPicking_Completada_NoPermiteReducirCantidad()
    {
        await using var db = CreateInMemoryDbContext();
        var service = CreateFulfillmentService(db, CreateStockService(db));
        var item = new ItemPickList
        {
            Id = Guid.NewGuid(), Sku = "SKU-GUARD", NombreProducto = "Producto",
            CantidadSolicitada = 3, CantidadRecogida = 3,
            Pasillo = "A", Rack = "01", Nivel = 1, Posicion = "01"
        };
        var list = new PickList
        {
            Id = Guid.NewGuid(), NumeroPickList = "PKL-GUARD", NumeroOrden = "ORD-GUARD",
            Estado = EstadoPicking.Completado, FechaFin = DateTime.UtcNow,
            Items = new List<ItemPickList> { item }
        };
        db.PickLists.Add(list);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmarPickingAsync(list.Id,
                new List<ConfirmarItemPickingRequest> { new(item.Id, 1) }));

        Assert.Equal(3, (await db.PickLists.Include(x => x.Items).SingleAsync()).Items.Single().CantidadRecogida);
    }

    [Fact]
    public async Task EmpacarYRotular_NoCreaNuevoPaqueteDespuesDeDespacho()
    {
        await using var db = CreateInMemoryDbContext();
        var service = CreateFulfillmentService(db, CreateStockService(db));
        var orderId = Guid.NewGuid();
        db.PickLists.Add(new PickList
        {
            Id = Guid.NewGuid(), OrdenId = orderId, NumeroOrden = "ORD-GUARD-2",
            NumeroPickList = "PKL-GUARD-2", Estado = EstadoPicking.Completado
        });
        db.PaquetesDespacho.Add(new PaqueteDespacho
        {
            Id = Guid.NewGuid(), OrdenId = orderId, NumeroOrden = "ORD-GUARD-2",
            NumeroGuia = "GUIA-GUARD-2", Estado = EstadoPacking.Despachado,
            FechaDespacho = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var request = new EmpacarDespachoRequest(
            orderId, "ORD-GUARD-2", 1m, 10m, 10m, 10m, "Transportista",
            "San Salvador", "Centro", "Direccion", "Cliente", "7000-0000");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmpacarYRotularAsync(request));

        Assert.Equal(1, await db.PaquetesDespacho.CountAsync());
    }

    [Fact]
    public async Task DespacharPaquete_ConsumeReservaYStock_YSiguienteIntentoEsIdempotente()
    {
        await using var db = CreateInMemoryDbContext();
        var stockService = CreateStockService(db);
        var service = CreateFulfillmentService(db, stockService);

        var producto = CrearProducto("SKU-DESP-001");
        var ubicacion = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01", "A");
        var stock = CrearStock(producto.Id, ubicacion, 10);

        db.Productos.Add(producto);
        db.UbicacionesAlmacen.Add(ubicacion);
        db.StockFisico.Add(stock);
        await db.SaveChangesAsync();

        var ordenId = Guid.NewGuid();
        var reserva = await stockService.ReservarStockAsync(producto.Id, 3, ordenId);
        await stockService.ConfirmarReservaAsync(reserva.ReservaId);

        var pick = await service.GenerarPickListAsync(new GenerarPickListRequest(
            ordenId,
            "ORD-DESP-001",
            "Operador",
            new List<ItemPickingSolicitud>
            {
                new(producto.Id, producto.Sku, producto.Nombre, 3)
            }));

        await service.ConfirmarPickingAsync(
            pick.Id,
            pick.Items.Select(i => new ConfirmarItemPickingRequest(i.Id, i.CantidadSolicitada)).ToList());

        var paquete = await service.EmpacarYRotularAsync(new EmpacarDespachoRequest(
            ordenId,
            "ORD-DESP-001",
            2m, 20m, 20m, 20m,
            "LOGIMKT Express",
            "San Salvador",
            "San Salvador Centro",
            "Dirección prueba",
            "Cliente prueba",
            "7000-0000"));

        var primerDespacho = await service.DespacharPaqueteAsync(paquete.Id);
        var segundoDespacho = await service.DespacharPaqueteAsync(paquete.Id);

        primerDespacho!.Estado.Should().Be(EstadoPacking.Despachado);
        primerDespacho.FechaDespacho.Should().NotBeNull();
        segundoDespacho!.Estado.Should().Be(EstadoPacking.Despachado);

        stock.CantidadTotal.Should().Be(7);
        stock.CantidadReservada.Should().Be(0);
        stock.CantidadDisponible.Should().Be(7);

        var reservaDb = await db.ReservasStock.SingleAsync(r => r.Id == reserva.ReservaId);
        reservaDb.CantidadConsumida.Should().Be(3);
        reservaDb.CantidadPendiente.Should().Be(0);
        reservaDb.Estado.Should().Be(EstadoReserva.Consumida);

        var outbound = await db.MovimientosInventario
            .Where(m => m.OrdenId == ordenId && m.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound)
            .ToListAsync();

        outbound.Sum(m => m.Cantidad).Should().Be(3);
        outbound.Should().HaveCount(1, "el segundo despacho no debe descontar otra vez");
    }

    [Fact]
    public async Task GenerarPickList_ConDuplicadosHistoricos_RechazaSinBorrarListas()
    {
        await using var db = CreateInMemoryDbContext();
        var servicio = CreateFulfillmentService(db, CreateStockService(db));
        var ordenId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        const string sku = "SKU-LEGACY-DUP";
        const string numero = "ORD-LEGACY-DUP";

        for (var i = 0; i < 2; i++)
        {
            db.PickLists.Add(new PickList
            {
                Id = Guid.NewGuid(), OrdenId = ordenId, NumeroOrden = numero,
                NumeroPickList = "PKL-LEGACY-" + i,
                Estado = EstadoPicking.Pendiente,
                Items = new List<ItemPickList>
                {
                    new()
                    {
                        Id = Guid.NewGuid(), ProductoId = productoId,
                        Sku = sku, NombreProducto = "QA", CantidadSolicitada = 2
                    }
                }
            });
        }
        await db.SaveChangesAsync();

        var request = new GenerarPickListRequest(
            ordenId, numero, "Operador QA", new List<ItemPickingSolicitud>
            {
                new(productoId, sku, "QA", 2)
            });
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.GenerarPickListAsync(request));

        Assert.Contains("múltiples PickLists", exception.Message);
        Assert.Equal(2, await db.PickLists.CountAsync(p => p.OrdenId == ordenId));
    }

    [Fact]
    public async Task GenerarPickList_RetryConCantidadDistinta_NoReutilizaUnaListaIncompatible()
    {
        await using var db = CreateInMemoryDbContext();
        var servicio = CreateFulfillmentService(db, CreateStockService(db));
        var ordenId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        const string sku = "SKU-CONFLICT-QA";
        const string numero = "ORD-CONFLICT-QA";

        var existente = new PickList
        {
            Id = Guid.NewGuid(), OrdenId = ordenId, NumeroOrden = numero,
            NumeroPickList = "PKL-CONFLICT-QA", Estado = EstadoPicking.Pendiente,
            Items = new List<ItemPickList>
            {
                new()
                {
                    Id = Guid.NewGuid(), ProductoId = productoId,
                    Sku = sku, NombreProducto = "QA", CantidadSolicitada = 2
                }
            }
        };
        db.PickLists.Add(existente);
        await db.SaveChangesAsync();

        var request = new GenerarPickListRequest(
            ordenId, numero, "Operador QA", new List<ItemPickingSolicitud>
            {
                new(productoId, sku, "QA", 3)
            });
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.GenerarPickListAsync(request));

        Assert.Contains("no coincide", exception.Message);
        Assert.Single(await db.PickLists.ToListAsync());
        Assert.Equal(2, (await db.PickLists.Include(p => p.Items).SingleAsync())
            .Items.Single().CantidadSolicitada);
    }

    private static Producto CrearProducto(string sku) => new()
    {
        Id = Guid.NewGuid(),
        Sku = sku,
        Nombre = $"Producto {sku}",
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

    private static UbicacionAlmacen CrearUbicacion(string codigo, string pasillo) => new()
    {
        Id = Guid.NewGuid(),
        Codigo = codigo,
        Pasillo = pasillo,
        Rack = "01",
        Nivel = 1,
        Posicion = "01",
        CapacidadMaxima = 100,
        EstaOcupada = true,
        Zona = "AlmacenamientoGeneral",
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
