using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class ComprasSkuDuplicadoTests
{
    [Theory]
    [InlineData("ALM-0001", "ALM-0001")]
    [InlineData("ALM-0001", "alm-0001")]
    [InlineData("ALM-0001", " ALM-0001 ")]
    public async Task CrearPO_RechazaSkuDuplicado_SinGuardarLaOrden(
        string primerSku, string segundoSku)
    {
        var id = Guid.NewGuid().ToString("N");
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("finance-po-duplicada-" + id).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("core-po-duplicada-" + id).Options;
        await using var finance = new FinanceDbContext(financeOptions);
        await using var core = new CoreDbContext(coreOptions);
        var controller = new ComprasController(
            finance,
            Mock.Of<ITresWayMatchingService>(),
            Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(),
            core,
            NullLogger<ComprasController>.Instance);

        var req = new CrearPORequest(
            Guid.NewGuid(), "Proveedor Demo", null, "Prueba de SKU duplicado",
            null, false,
            new List<ItemPORequest>
            {
                new(primerSku, "Producto de prueba", 2, 10.00m),
                new(segundoSku, "Producto de prueba", 3, 10.00m)
            });

        var result = await controller.CrearPO(req, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await finance.OrdenesCompra.ToListAsync());
    }
    [Fact]
    public async Task CrearPO_AceptaDosSkusDistintos_YConservaTotales()
    {
        var id = Guid.NewGuid().ToString("N");
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("finance-po-valida-" + id).Options;
        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("core-po-valida-" + id).Options;
        await using var finance = new FinanceDbContext(financeOptions);
        await using var core = new CoreDbContext(coreOptions);
        var catalogOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("catalog-po-valida-" + id).Options;
        await using var catalog = new CatalogDbContext(catalogOptions);
        var vendedorId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var arrozProducto = new Producto
        {
            Id = Guid.NewGuid(), Sku = "ALM-0001", Nombre = "Arroz",
            CategoriaNombre = "Alimentos", Subcategoria = "Granos",
            PrecioB2C = 5m, PrecioB2B = 3.55m, Moneda = "USD",
            EstadoProducto = "Activo", VendedorId = vendedorId, CategoriaId = categoriaId
        };
        var cafeProducto = new Producto
        {
            Id = Guid.NewGuid(), Sku = "ALM-0005", Nombre = "Café",
            CategoriaNombre = "Alimentos", Subcategoria = "Bebidas",
            PrecioB2C = 14m, PrecioB2B = 10.06m, Moneda = "USD",
            EstadoProducto = "Activo", VendedorId = vendedorId, CategoriaId = categoriaId
        };
        catalog.Productos.AddRange(arrozProducto, cafeProducto);
        await catalog.SaveChangesAsync();
        var proveedor = new MarketplaceERP.Core.Entities.Proveedor
        {
            Id = Guid.NewGuid(),
            NombreComercial = "Proveedor de evaluación",
            RazonSocial = "Proveedor de evaluación",
            Activo = true
        };
        core.Proveedores.Add(proveedor);
        await core.SaveChangesAsync();

        var controller = new ComprasController(
            finance,
            Mock.Of<ITresWayMatchingService>(),
            Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(),
            core,
            NullLogger<ComprasController>.Instance,
            catalog);

        var req = new CrearPORequest(proveedor.Id, proveedor.NombreComercial,
            null, "Demo de orden", null, false,
            new List<ItemPORequest>
            {
                new("ALM-0001", "Arroz", 24, 3.55m),
                new("ALM-0005", "Café", 12, 10.06m)
            });

        var result = await controller.CrearPO(req, CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result);
        var orden = await finance.OrdenesCompra.Include(x => x.Items).SingleAsync();
        Assert.Equal(2, orden.Items.Count);
        Assert.Equal(arrozProducto.Id, orden.Items.Single(x => x.Sku == "ALM-0001").ProductoId);
        Assert.Equal(cafeProducto.Id, orden.Items.Single(x => x.Sku == "ALM-0005").ProductoId);
        Assert.Equal(205.92m, orden.SubTotal);
        Assert.Equal(26.77m, orden.ImpuestoIva);
        Assert.Equal(232.69m, orden.MontoTotal);
    }

    [Fact]
    public async Task CrearPO_RechazaSkuInexistente_SinPersistirLaOrden()
    {
        var id = Guid.NewGuid().ToString("N");
        await using var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("finance-po-sku-inexistente-" + id).Options);
        await using var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("core-po-sku-inexistente-" + id).Options);
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("catalog-po-sku-inexistente-" + id).Options);
        var proveedor = new MarketplaceERP.Core.Entities.Proveedor
        {
            Id = Guid.NewGuid(), NombreComercial = "Proveedor QA",
            RazonSocial = "Proveedor QA", Activo = true
        };
        core.Proveedores.Add(proveedor);
        await core.SaveChangesAsync();
        var controller = new ComprasController(finance,
            Mock.Of<ITresWayMatchingService>(), Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(), core, NullLogger<ComprasController>.Instance, catalog);
        var request = new CrearPORequest(proveedor.Id, proveedor.NombreComercial,
            null, "SKU inexistente", null, false,
            new List<ItemPORequest> { new("SKU-NO-EXISTE", "Producto desconocido", 1, 1m) });

        var result = await controller.CrearPO(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await finance.OrdenesCompra.ToListAsync());
        Assert.Empty(await finance.ItemsOrdenCompra.ToListAsync());
    }

}
