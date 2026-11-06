using System.Security.Claims;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class ProductosPropiedadTests
{
    private static CatalogDbContext MemoryContext() => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseInMemoryDatabase("productos-propiedad-" + Guid.NewGuid()).Options);

    private static ProductosController CreateController(
        CatalogDbContext db,
        string? role,
        Guid? actorId = null,
        Guid? vendedorId = null)
    {
        var claims = new List<Claim>();
        if (actorId.HasValue)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, actorId.Value.ToString()));
        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim(ClaimTypes.Role, role));
        if (vendedorId.HasValue)
            claims.Add(new Claim("VendedorId", vendedorId.Value.ToString()));

        var identity = role is null && !actorId.HasValue && !vendedorId.HasValue
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "test-auth");

        var principal = new ClaimsPrincipal(identity);
        var mockImport = new Mock<ICatalogImportService>();

        return new ProductosController(db, mockImport.Object, NullLogger<ProductosController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static Producto CreateTestProduct(Guid vendedorId, string sku, string nombre, decimal precio = 100m, bool publicado = true)
    {
        return new Producto
        {
            Id = Guid.NewGuid(),
            Sku = sku,
            Nombre = nombre,
            Descripcion = "Descripción " + nombre,
            CategoriaNombre = "Tecnología",
            Subcategoria = "Laptops",
            PrecioB2C = precio,
            PrecioB2B = precio * 0.9m,
            StockMinimo = 5,
            ProveedorNombre = "Proveedor Test",
            PaisOrigen = "El Salvador",
            PesoKg = 1.5m,
            DimensionesCm = "30x20x2",
            UnidadMedida = "Unidad",
            EstadoProducto = "Activo",
            EstaPublicado = publicado,
            VendedorId = vendedorId,
            CategoriaId = Guid.NewGuid(),
            FechaCreacion = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task Comprador_BusquedaYDetalle_Permitidos_RetornaDatosReales()
    {
        await using var db = MemoryContext();
        var sellerId = Guid.NewGuid();
        var prod1 = CreateTestProduct(sellerId, "SKU-LAPTOP-01", "Laptop Gamer Ultra", 1500m);
        var prod2 = CreateTestProduct(sellerId, "SKU-MOUSE-01", "Mouse Inalámbrico", 30m);
        db.Productos.AddRange(prod1, prod2);
        await db.SaveChangesAsync();

        var controller = CreateController(db, "Comprador", actorId: Guid.NewGuid());

        // 1. Búsqueda de productos
        var searchResult = await controller.Buscar("Laptop", null, null, null, null, null);
        var okSearch = Assert.IsType<OkObjectResult>(searchResult);
        var searchDoc = JsonDocument.Parse(JsonSerializer.Serialize(okSearch.Value));
        Assert.Equal(1, searchDoc.RootElement.GetProperty("Total").GetInt32());
        var searchItems = searchDoc.RootElement.GetProperty("Items").EnumerateArray().ToList();
        Assert.Single(searchItems);
        Assert.Equal("SKU-LAPTOP-01", searchItems[0].GetProperty("Sku").GetString());

        // 2. Detalle de producto
        var detailResult = await controller.GetById(prod1.Id);
        var okDetail = Assert.IsType<OkObjectResult>(detailResult);
        var prodDetail = Assert.IsType<Producto>(okDetail.Value);
        Assert.Equal("Laptop Gamer Ultra", prodDetail.Nombre);
        Assert.Equal(1500m, prodDetail.PrecioB2C);

        // 3. Catálogo publicado
        var catalogResult = await controller.GetPublicados(1, 10);
        var okCatalog = Assert.IsType<OkObjectResult>(catalogResult);
        var catalogDoc = JsonDocument.Parse(JsonSerializer.Serialize(okCatalog.Value));
        Assert.Equal(2, catalogDoc.RootElement.GetProperty("Total").GetInt32());
    }

    [Fact]
    public async Task Vendedor_LecturaCatalogo_Permitida()
    {
        await using var db = MemoryContext();
        var sellerId = Guid.NewGuid();
        var prod = CreateTestProduct(sellerId, "SKU-TEST-01", "Monitor 4K", 400m);
        db.Productos.Add(prod);
        await db.SaveChangesAsync();

        var controller = CreateController(db, "Vendedor", actorId: Guid.NewGuid(), vendedorId: sellerId);

        var listResult = await controller.GetPublicados(1, 10);
        Assert.IsType<OkObjectResult>(listResult);

        var searchResult = await controller.Buscar("Monitor", null, null, null, null, null);
        Assert.IsType<OkObjectResult>(searchResult);

        var getResult = await controller.GetById(prod.Id);
        Assert.IsType<OkObjectResult>(getResult);

        var b2bResult = await controller.GetCatalogoB2B(null);
        Assert.IsType<OkObjectResult>(b2bResult);
    }

    [Fact]
    public async Task Vendedor_OperacionesLegitimas_PermitidasSobreSusPropiosProductos()
    {
        await using var db = MemoryContext();
        var sellerA = Guid.NewGuid();
        var prodA = CreateTestProduct(sellerA, "SKU-OWN-01", "Teclado Original", 50m);
        db.Productos.Add(prodA);
        await db.SaveChangesAsync();

        var controller = CreateController(db, "Vendedor", actorId: Guid.NewGuid(), vendedorId: sellerA);

        // 1. Create asignado a su propio VendedorId
        var nuevoProd = CreateTestProduct(sellerA, "SKU-NEW-01", "Nuevo Producto Vendedor A", 80m);
        var createResult = await controller.Create(nuevoProd);
        var created = Assert.IsType<CreatedAtActionResult>(createResult);
        var createdProd = Assert.IsType<Producto>(created.Value);
        Assert.Equal(sellerA, createdProd.VendedorId);

        // 2. Create sin especificar VendedorId -> se asigna automáticamente el del token
        var prodSinVendedor = CreateTestProduct(Guid.Empty, "SKU-NEW-02", "Producto Auto Vendedor", 90m);
        var autoCreateResult = await controller.Create(prodSinVendedor);
        var autoCreated = Assert.IsType<CreatedAtActionResult>(autoCreateResult);
        var autoCreatedProd = Assert.IsType<Producto>(autoCreated.Value);
        Assert.Equal(sellerA, autoCreatedProd.VendedorId);

        // 3. Update sobre producto propio
        var updateRequest = new ActualizarProductoRequest(
            "SKU-OWN-01", "Teclado Actualizado", "Nueva descripcion", "Tecnología", "Periféricos",
            55m, 45m, 10, "Proveedor Test", "El Salvador", 0.8m, "45x15x3", "Unidad", "Activo", true);
        var updateResult = await controller.Update(prodA.Id, updateRequest);
        var okUpdate = Assert.IsType<OkObjectResult>(updateResult);
        var updatedProd = Assert.IsType<Producto>(okUpdate.Value);
        Assert.Equal("Teclado Actualizado", updatedProd.Nombre);

        // 4. Publicar producto propio
        var publishResult = await controller.Publicar(prodA.Id);
        Assert.IsType<NoContentResult>(publishResult);

        // 5. Delete (baja lógica) sobre producto propio
        var deleteResult = await controller.Delete(prodA.Id);
        Assert.IsType<NoContentResult>(deleteResult);
        var deletedInDb = await db.Productos.FindAsync(prodA.Id);
        Assert.False(deletedInDb!.EstaPublicado);
        Assert.Equal("Inactivo", deletedInDb.EstadoProducto);
    }

    [Fact]
    public async Task Vendedor_ModificacionProductosAjenos_Denegada()
    {
        await using var db = MemoryContext();
        var sellerA = Guid.NewGuid();
        var sellerB = Guid.NewGuid();
        var prodB = CreateTestProduct(sellerB, "SKU-SELLER-B", "Producto Exclusivo Seller B", 200m);
        db.Productos.Add(prodB);
        await db.SaveChangesAsync();

        var controllerSellerA = CreateController(db, "Vendedor", actorId: Guid.NewGuid(), vendedorId: sellerA);

        // 1. Intento de Create con VendedorId ajeno
        var prodAjeno = CreateTestProduct(sellerB, "SKU-INTRUSION", "Intrusión", 100m);
        var createResult = await controllerSellerA.Create(prodAjeno);
        Assert.IsType<ForbidResult>(createResult);

        // 2. Intento de Update sobre producto de sellerB
        var updateRequest = new ActualizarProductoRequest(
            "SKU-SELLER-B", "Intento Modificacion", "Hack", "Tecnología", "Hacks",
            1m, 1m, 1, "Proveedor Test", "El Salvador", 1m, "10x10x10", "Unidad", "Activo", true);
        var updateResult = await controllerSellerA.Update(prodB.Id, updateRequest);
        Assert.IsType<ForbidResult>(updateResult);

        // 3. Intento de Delete sobre producto de sellerB
        var deleteResult = await controllerSellerA.Delete(prodB.Id);
        Assert.IsType<ForbidResult>(deleteResult);

        // 4. Intento de Publicar producto de sellerB
        var publishResult = await controllerSellerA.Publicar(prodB.Id);
        Assert.IsType<ForbidResult>(publishResult);

        // Verificar que el producto en base de datos quedó intacto
        var prodInDb = await db.Productos.FindAsync(prodB.Id);
        Assert.Equal("Producto Exclusivo Seller B", prodInDb!.Nombre);
        Assert.True(prodInDb.EstaPublicado);
        Assert.Equal("Activo", prodInDb.EstadoProducto);
    }

    [Fact]
    public async Task Vendedor_AlertasStock_AisladasPorVendedor()
    {
        await using var db = MemoryContext();
        var sellerA = Guid.NewGuid();
        var sellerB = Guid.NewGuid();

        var prodA = CreateTestProduct(sellerA, "SKU-ALERTA-A", "Alerta Seller A", 100m);
        prodA.StockMinimo = 10;
        prodA.Stocks.Add(new StockFisico
        {
            Id = Guid.NewGuid(), ProductoId = prodA.Id, CantidadTotal = 3, CantidadReservada = 0, CantidadBloqueada = 0,
            UbicacionCodigo = "PAS-01-A"
        });

        var prodB = CreateTestProduct(sellerB, "SKU-ALERTA-B", "Alerta Seller B", 100m);
        prodB.StockMinimo = 10;
        prodB.Stocks.Add(new StockFisico
        {
            Id = Guid.NewGuid(), ProductoId = prodB.Id, CantidadTotal = 2, CantidadReservada = 0, CantidadBloqueada = 0,
            UbicacionCodigo = "PAS-02-B"
        });

        db.Productos.AddRange(prodA, prodB);
        await db.SaveChangesAsync();

        // 1. Seller A solo ve alertas de sus propios productos
        var controllerSellerA = CreateController(db, "Vendedor", actorId: Guid.NewGuid(), vendedorId: sellerA);
        var alertasResultA = await controllerSellerA.GetAlertasStock();
        var okA = Assert.IsType<OkObjectResult>(alertasResultA);
        var docA = JsonDocument.Parse(JsonSerializer.Serialize(okA.Value));
        Assert.Equal(1, docA.RootElement.GetProperty("TotalAlertas").GetInt32());

        // 2. Admin ve alertas globales de todos los vendedores
        var controllerAdmin = CreateController(db, "Admin", actorId: Guid.NewGuid());
        var alertasResultAdmin = await controllerAdmin.GetAlertasStock();
        var okAdmin = Assert.IsType<OkObjectResult>(alertasResultAdmin);
        var docAdmin = JsonDocument.Parse(JsonSerializer.Serialize(okAdmin.Value));
        Assert.Equal(2, docAdmin.RootElement.GetProperty("TotalAlertas").GetInt32());
    }

    [Fact]
    public async Task Admin_OperacionesAdministrativasPermitidas()
    {
        await using var db = MemoryContext();
        var seller = Guid.NewGuid();
        var prod = CreateTestProduct(seller, "SKU-ADMIN-TEST", "Producto Control Admin", 100m);
        db.Productos.Add(prod);
        await db.SaveChangesAsync();

        var controllerAdmin = CreateController(db, "Admin", actorId: Guid.NewGuid());

        // Admin puede actualizar cualquier producto sin importar el VendedorId
        var updateRequest = new ActualizarProductoRequest(
            "SKU-ADMIN-TEST", "Nombre Actualizado por Admin", "Desc", "Tecnología", "General",
            120m, 100m, 10, "Proveedor", "El Salvador", 1m, "10x10x10", "Unidad", "Activo", true);
        var updateResult = await controllerAdmin.Update(prod.Id, updateRequest);
        Assert.IsType<OkObjectResult>(updateResult);

        // Admin puede dar de baja lógica
        var deleteResult = await controllerAdmin.Delete(prod.Id);
        Assert.IsType<NoContentResult>(deleteResult);

        // Admin puede publicar
        var publishResult = await controllerAdmin.Publicar(prod.Id);
        Assert.IsType<NoContentResult>(publishResult);
    }
}
