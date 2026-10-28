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

    [Fact]
    public async Task GenerarPickList_OrdenaItemsPorRutaOptimaSShape()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var service = new FulfillmentService(db, new FakeTestNumeracionService(), NullLogger<FulfillmentService>.Instance);

        var prod1 = Guid.NewGuid();
        var prod2 = Guid.NewGuid();

        // Ubicaciones: prod2 en Pasillo A, prod1 en Pasillo B
        var ubiB = new UbicacionAlmacen { Id = Guid.NewGuid(), Codigo = "PAS-B-RCK-01-NIV-01-POS-01", Pasillo = "B", Rack = "01", Nivel = 1, Posicion = "01" };
        var ubiA = new UbicacionAlmacen { Id = Guid.NewGuid(), Codigo = "PAS-A-RCK-01-NIV-01-POS-01", Pasillo = "A", Rack = "01", Nivel = 1, Posicion = "01" };

        db.UbicacionesAlmacen.AddRange(ubiB, ubiA);
        db.StockFisico.AddRange(
            new StockFisico { Id = Guid.NewGuid(), ProductoId = prod1, CantidadTotal = 50, UbicacionAlmacen = ubiB, UbicacionCodigo = ubiB.Codigo },
            new StockFisico { Id = Guid.NewGuid(), ProductoId = prod2, CantidadTotal = 50, UbicacionAlmacen = ubiA, UbicacionCodigo = ubiA.Codigo }
        );
        await db.SaveChangesAsync();

        var request = new GenerarPickListRequest(
            OrdenId: Guid.NewGuid(),
            NumeroOrden: "ORD-TEST-001",
            OperadorAsignado: "Carlos Perez",
            Items: new List<ItemPickingSolicitud>
            {
                new(prod1, "SKU-PROD-B", "Producto B", 2),
                new(prod2, "SKU-PROD-A", "Producto A", 3)
            }
        );

        // Act
        var pickList = await service.GenerarPickListAsync(request);

        // Assert
        pickList.Should().NotBeNull();
        pickList.Items.Should().HaveCount(2);
        // Primero Pasillo A luego Pasillo B
        pickList.Items.First().Pasillo.Should().Be("A");
        pickList.Items.Last().Pasillo.Should().Be("B");
        pickList.Estado.Should().Be(EstadoPicking.Pendiente);
    }

    [Fact]
    public async Task ConfirmarPicking_ActualizaEstadoACompletado_CuandoTodosSonRecogidos()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var service = new FulfillmentService(db, new FakeTestNumeracionService(), NullLogger<FulfillmentService>.Instance);

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

        // Act
        var confirmaciones = new List<ConfirmarItemPickingRequest>
        {
            new(item.Id, CantidadRecogida: 2)
        };

        var resultado = await service.ConfirmarPickingAsync(pickList.Id, confirmaciones);

        // Assert
        resultado.Should().NotBeNull();
        resultado!.Estado.Should().Be(EstadoPicking.Completado);
        resultado.FechaFin.Should().NotBeNull();
        resultado.Items.First().Recogido.Should().BeTrue();
    }

    [Fact]
    public async Task EmpacarYRotular_GeneraGuiaEnvio_YCalculaVolumenM3()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var service = new FulfillmentService(db, new FakeTestNumeracionService(), NullLogger<FulfillmentService>.Instance);

        var request = new EmpacarDespachoRequest(
            OrdenId: Guid.NewGuid(),
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

        // Act
        var paquete = await service.EmpacarYRotularAsync(request);

        // Assert
        paquete.Should().NotBeNull();
        paquete.NumeroGuia.Should().StartWith("SV-LOGI-");
        paquete.Estado.Should().Be(EstadoPacking.ListoParaDespacho);
        // Volumen en m3 = (30 * 20 * 10) / 1,000,000 = 6000 / 1000000 = 0.006 m3
        paquete.VolumenM3.Should().Be(0.006m);
    }
}
