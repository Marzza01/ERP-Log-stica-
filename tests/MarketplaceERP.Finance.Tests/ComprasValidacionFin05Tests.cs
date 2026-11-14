using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class ComprasValidacionFin05Tests
{
    private static (CoreDbContext Core, FinanceDbContext Finance, CatalogDbContext Catalog,
        ComprasController Controller, Guid ProveedorId) Crear()
    {
        var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("fin05-core-" + Guid.NewGuid()).Options);
        var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("fin05-finance-" + Guid.NewGuid()).Options);
        var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("fin05-catalog-" + Guid.NewGuid()).Options);
        catalog.Productos.Add(new Producto
        {
            Id = Guid.NewGuid(), Sku = "SKU-TEST", Nombre = "Producto",
            CategoriaNombre = "Pruebas", Subcategoria = "QA",
            PrecioB2C = 12m, PrecioB2B = 10m, Moneda = "USD",
            EstadoProducto = "Activo", VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid()
        });
        catalog.SaveChanges();

        var proveedorId = Guid.NewGuid();
        core.Proveedores.Add(new Proveedor
        {
            Id = proveedorId,
            NombreComercial = "Proveedor de prueba",
            RazonSocial = "Proveedor de prueba S.A.",
            Activo = true
        });
        core.SaveChanges();

        var controller = new ComprasController(
            finance, Mock.Of<ITresWayMatchingService>(), Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(), core, NullLogger<ComprasController>.Instance, catalog);
        return (core, finance, catalog, controller, proveedorId);
    }

    private static CrearPORequest NuevaPO(Guid proveedorId,
        int cantidad = 2, decimal precio = 10m, string sku = "SKU-TEST",
        string nombre = "Producto") => new(
            proveedorId, "Proveedor", null, null, null, false,
            new List<ItemPORequest> { new(sku, nombre, cantidad, precio) });

    private static async Task<Guid> RegistrarPO(FinanceDbContext finance, Guid proveedorId)
    {
        var id = Guid.NewGuid();
        finance.OrdenesCompra.Add(new OrdenCompra
        {
            Id = id,
            NumeroPO = "PO-FIN05-" + Guid.NewGuid().ToString("N"),
            ProveedorId = proveedorId,
            ProveedorNombre = "Proveedor",
            SubTotal = 100m,
            ImpuestoIva = 13m,
            MontoTotal = 113m,
            Estado = EstadoOrdenCompra.RecibidaCompleta
        });
        await finance.SaveChangesAsync();
        return id;
    }

    [Theory]
    [InlineData("cantidad-cero")]
    [InlineData("cantidad-negativa")]
    [InlineData("precio-negativo")]
    [InlineData("precio-cero")]
    [InlineData("precio-tres-decimales")]
    [InlineData("sku-vacio")]
    [InlineData("nombre-vacio")]
    [InlineData("sku-largo")]
    [InlineData("precio-fuera-de-limite")]
    [InlineData("total-fuera-de-limite")]
    public async Task POConDatosInvalidos_Devuelve400SinPersistir(string caso)
    {
        var setup = Crear();
        await using var catalog = setup.Catalog;
        await using var core = setup.Core;
        await using var finance = setup.Finance;
        var (cantidad, precio, sku, nombre) = caso switch
        {
            "cantidad-cero" => (0, 10m, "SKU", "Producto"),
            "cantidad-negativa" => (-5, 10m, "SKU", "Producto"),
            "precio-negativo" => (1, -10m, "SKU", "Producto"),
            "precio-cero" => (1, 0m, "SKU", "Producto"),
            "precio-tres-decimales" => (1, 10.001m, "SKU", "Producto"),
            "sku-vacio" => (1, 10m, " ", "Producto"),
            "nombre-vacio" => (1, 10m, "SKU", " "),
            "sku-largo" => (1, 10m, new string('S', 51), "Producto"),
            "precio-fuera-de-limite" => (1, 9999999999999999.99m, "SKU", "Producto"),
            "total-fuera-de-limite" => (int.MaxValue, 9999999999999999.99m, "SKU", "Producto"),
            _ => throw new InvalidOperationException(caso)
        };

        var response = await setup.Controller.CrearPO(
            NuevaPO(setup.ProveedorId, cantidad, precio, sku, nombre),
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Empty(await finance.OrdenesCompra.ToListAsync());
        Assert.Empty(await finance.ItemsOrdenCompra.ToListAsync());
    }

    [Fact]
    public async Task POValida_CalculaSubtotalIvaYTotalExactos()
    {
        var setup = Crear();
        await using var catalog = setup.Catalog;
        await using var core = setup.Core;
        await using var finance = setup.Finance;
        Assert.IsType<CreatedAtActionResult>(await setup.Controller.CrearPO(
            NuevaPO(setup.ProveedorId, cantidad: 2, precio: 10.25m),
            CancellationToken.None));
        var po = await finance.OrdenesCompra.Include(x => x.Items).SingleAsync();
        Assert.Equal(20.50m, po.SubTotal);
        Assert.Equal(2.66m, po.ImpuestoIva); // Math.Round(20.50 * 0.13, 2)
        Assert.Equal(23.16m, po.MontoTotal);
        Assert.Equal(2, Assert.Single(po.Items).CantidadSolicitada);
    }

    [Theory]
    [InlineData("subtotal-negativo")]
    [InlineData("subtotal-cero")]
    [InlineData("subtotal-tres-decimales")]
    [InlineData("total-negativo")]
    [InlineData("total-incompleto")]
    [InlineData("numero-vacio")]
    [InlineData("numero-largo")]
    [InlineData("vencimiento-anterior")]
    public async Task FacturaInvalida_NoGeneraRegistro(string caso)
    {
        var setup = Crear();
        await using var catalog = setup.Catalog;
        await using var core = setup.Core;
        await using var finance = setup.Finance;
        var poId = await RegistrarPO(finance, setup.ProveedorId);
        var subt = 100m;
        var total = 113m;
        var numero = "FACT-1";
        DateTime? emision = null;
        DateTime? vencimiento = null;
        switch (caso)
        {
            case "subtotal-negativo": subt = -100m; break;
            case "subtotal-cero": subt = 0m; break;
            case "subtotal-tres-decimales": subt = 100.001m; break;
            case "total-negativo": total = -113m; break;
            case "total-incompleto": total = 100m; break;
            case "numero-vacio": numero = " "; break;
            case "numero-largo": numero = new string('F', 51); break;
            case "vencimiento-anterior":
                emision = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
                vencimiento = emision.Value.AddDays(-1);
                break;
        }

        var req = new RegistrarFacturaRequest(poId, numero, subt, total, emision, vencimiento);
        var result = await setup.Controller.RegistrarFactura(req, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await finance.FacturasProveedor.ToListAsync());
    }

    [Fact]
    public async Task FacturaValida_ReintentoMismoNumeroNoDuplicaDocumento()
    {
        var setup = Crear();
        await using var catalog = setup.Catalog;
        await using var core = setup.Core;
        await using var finance = setup.Finance;
        var poId = await RegistrarPO(finance, setup.ProveedorId);
        var request = new RegistrarFacturaRequest(poId, "  CCF-001  ", 100m, 113m, null, null);

        Assert.IsType<CreatedAtActionResult>(await setup.Controller.RegistrarFactura(
            request, CancellationToken.None));

        var registro = Assert.Single(await finance.FacturasProveedor.ToListAsync());
        Assert.Equal("CCF-001", registro.NumeroFactura);
        Assert.Equal(100m, registro.MontoSubTotal);
        Assert.Equal(13m, registro.MontoIva);
        Assert.Equal(113m, registro.MontoTotal);
        Assert.True(registro.FechaVencimiento >= registro.FechaEmision);

        var duplicado = await setup.Controller.RegistrarFactura(
            new RegistrarFacturaRequest(poId, "ccf-001", 100m, 113m, null, null),
            CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(duplicado);
        Assert.Single(await finance.FacturasProveedor.ToListAsync());
    }
}
