/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class ReabastecimientoControllerTests
{
    [Fact]
    public async Task ObtenerNecesidades_ExcluyeBorradoresYCuentaSoloComprasComprometidas()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var catalogOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"ReabastecimientoCatalog_{suffix}")
            .Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"ReabastecimientoFinance_{suffix}")
            .Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"ReabastecimientoCore_{suffix}")
            .Options;

        await using var catalogDb = new CatalogDbContext(catalogOptions);
        await using var financeDb = new FinanceDbContext(financeOptions);
        await using var coreDb = new CoreDbContext(coreOptions);

        var productId = Guid.NewGuid();
        const string sku = "QA-REAB-001";
        catalogDb.Productos.Add(new Producto
        {
            Id = productId,
            Sku = sku,
            Nombre = "Producto QA",
            CategoriaNombre = "Pruebas",
            ProveedorNombre = "Proveedor QA",
            EstadoProducto = "Activo",
            EstaPublicado = true,
            StockMinimo = 5,
            PrecioB2C = 10m,
            PrecioB2B = 8m,
            VendedorId = Guid.NewGuid(),
            CategoriaId = Guid.NewGuid()
        });
        await catalogDb.SaveChangesAsync();

        financeDb.OrdenesCompra.AddRange(
            CrearOrden("PO-BORRADOR", EstadoOrdenCompra.Borrador, productId, sku, 100, 0),
            CrearOrden("PO-ENVIADA", EstadoOrdenCompra.Enviada, productId, sku, 3, 0),
            CrearOrden("PO-PARCIAL", EstadoOrdenCompra.RecibidaParcial, productId, sku, 4, 2),
            CrearOrden("PO-CANCELADA", EstadoOrdenCompra.Cancelada, productId, sku, 100, 0));
        await financeDb.SaveChangesAsync();

        var controller = new ReabastecimientoController(
            new ReabastecimientoService(catalogDb),
            financeDb,
            coreDb);

        var result = await controller.ObtenerNecesidades(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var item = json.RootElement.GetProperty("Items")[0];

        Assert.Equal(5, item.GetProperty("CantidadEnCompra").GetInt32());
        Assert.Equal(5, item.GetProperty("DisponibleProyectado").GetInt32());
        Assert.Equal(1, item.GetProperty("FaltanteProyectado").GetInt32());
        Assert.Equal("COBERTURA_PARCIAL", item.GetProperty("EstadoCompra").GetString());

        var ordenes = item.GetProperty("OrdenesCompra")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();
        Assert.Equal(new[] { "PO-ENVIADA", "PO-PARCIAL" }, ordenes);
    }

    private static OrdenCompra CrearOrden(
        string numero,
        EstadoOrdenCompra estado,
        Guid productoId,
        string sku,
        int solicitada,
        int recibida)
    {
        var ordenId = Guid.NewGuid();
        return new OrdenCompra
        {
            Id = ordenId,
            NumeroPO = numero,
            ProveedorId = Guid.NewGuid(),
            ProveedorNombre = "Proveedor QA",
            Estado = estado,
            SubTotal = solicitada,
            ImpuestoIva = 0m,
            MontoTotal = solicitada,
            Moneda = "USD",
            Items =
            [
                new ItemOrdenCompra
                {
                    Id = Guid.NewGuid(),
                    OrdenCompraId = ordenId,
                    ProductoId = productoId,
                    Sku = sku,
                    NombreProducto = "Producto QA",
                    CantidadSolicitada = solicitada,
                    CantidadRecibida = recibida,
                    PrecioUnitarioAcordado = 1m
                }
            ]
        };
    }
}
