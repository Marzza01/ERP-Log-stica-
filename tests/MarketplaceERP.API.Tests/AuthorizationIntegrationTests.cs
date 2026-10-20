/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.API.Tests;

public class AuthorizationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthorizationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        SeedTestData();
    }

    private void SeedTestData()
    {
        using var scope = _factory.Services.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        if (!coreDb.Vendedores.Any())
        {
            var vendedor = new Vendedor
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                NombreEmpresa = "Vendedor Test",
                Email = "vendedor@test.com",
                Telefono = "2222-3333",
                EstaActivo = true,
                FechaRegistro = DateTime.UtcNow
            };
            coreDb.Vendedores.Add(vendedor);
            coreDb.SaveChanges();
        }

        if (!catalogDb.Productos.Any())
        {
            var producto = new Producto
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Sku = "SKU-TEST-001",
                Nombre = "Producto Test",
                Descripcion = "Descripcion Test",
                PrecioB2C = 100m,
                PrecioB2B = 80m,
                EstaPublicado = true,
                VendedorId = Guid.Parse("11111111-1111-1111-1111-111111111111")
            };
            catalogDb.Productos.Add(producto);
            catalogDb.SaveChanges();
        }

        if (!financeDb.Ordenes.Any())
        {
            var orden = new Orden
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                NumeroOrden = "ORD-TEST-001",
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Estado = EstadoOrden.Pendiente,
                MontoTotal = 100m,
                Moneda = "USD",
                FechaCreacion = DateTime.UtcNow
            };
            financeDb.Ordenes.Add(orden);
            financeDb.SaveChanges();
        }
    }

    // ── Health y Login (Anónimos) ─────────────────────────────────────────────

    [Fact]
    public async Task Health_SinToken_Retorna200Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_SinToken_CredencialesInvalidas_Retorna401Unauthorized()
    {
        var client = _factory.CreateClient();
        var body = new { Username = "usuario_inexistente", Password = "password" };
        var response = await client.PostAsJsonAsync("/api/auth/login", body);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 1. ContabilidadController (Admin) ────────────────────────────────────

    [Fact]
    public async Task Contabilidad_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/contabilidad/balance-comprobacion");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Contabilidad_RolComprador_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.GetAsync("/api/contabilidad/balance-comprobacion");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Contabilidad_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/contabilidad/balance-comprobacion");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 2. ComprasController (Admin) ─────────────────────────────────────────

    [Fact]
    public async Task Compras_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/compras/po");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Compras_RolVendedor_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/compras/po");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Compras_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/compras/po");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 3. LiquidacionesController (Admin: generar/procesar; Admin/Vendedor: historial)

    [Fact]
    public async Task Liquidaciones_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/liquidaciones/historial");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LiquidacionesGenerar_RolVendedor_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.PostAsJsonAsync("/api/liquidaciones/generar", new { VendedorId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LiquidacionesHistorial_RolVendedor_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/liquidaciones/historial");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LiquidacionesHistorial_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/liquidaciones/historial");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 4. KpisDashboardController (Admin) ───────────────────────────────────

    [Fact]
    public async Task KpisDashboard_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/kpis-dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KpisDashboard_RolVendedor_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/kpis-dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task KpisDashboard_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/kpis-dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 5. OrdenesController (Listar: Admin,Vendedor) ─────────────────────────

    [Fact]
    public async Task Ordenes_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/ordenes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ordenes_RolComprador_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.GetAsync("/api/ordenes");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ordenes_RolVendedor_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/ordenes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ordenes_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/ordenes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 6. FulfillmentController (Admin, Vendedor) ───────────────────────────

    [Fact]
    public async Task Fulfillment_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/fulfillment/picklists");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Fulfillment_RolComprador_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.GetAsync("/api/fulfillment/picklists");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Fulfillment_RolVendedor_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/fulfillment/picklists");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Fulfillment_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/fulfillment/picklists");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 7. ComisionesController (Admin) ──────────────────────────────────────

    [Fact]
    public async Task Comisiones_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/comisiones");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Comisiones_RolVendedor_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/comisiones");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Comisiones_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/comisiones");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 8. VendedoresController (Listar: Admin, Vendedor) ────────────────────

    [Fact]
    public async Task Vendedores_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/vendedores");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Vendedores_RolComprador_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.GetAsync("/api/vendedores");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Vendedores_RolVendedor_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/vendedores");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 9. ProductosController (Listar: Autenticado; Crear: Admin, Vendedor) ─

    [Fact]
    public async Task Productos_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Productos_RolComprador_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.GetAsync("/api/productos");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProductosSeed500_RolVendedor_Retorna403()
    {
        // En ProductosController Seed500 requiere Admin
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.PostAsync("/api/productos/seed-500", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 10. WmsController (Mapa: Autenticado; Reubicar: Admin, Vendedor) ─────

    [Fact]
    public async Task Wms_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/wms/mapa");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WmsReubicar_RolComprador_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.PostAsJsonAsync("/api/wms/reubicar", new { StockFisicoId = Guid.NewGuid(), NuevaUbicacionId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WmsMapa_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/wms/mapa");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WmsMapa_RolVendedor_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/wms/mapa");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 11. RmaController (Listar: Autenticado; Inspeccionar: Admin, Vendedor)

    [Fact]
    public async Task Rma_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/rma");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RmaInspeccionar_RolComprador_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.PostAsJsonAsync($"/api/rma/{Guid.NewGuid()}/inspeccionar", new { Accion = "Restock" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RmaListar_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/rma");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RmaListar_RolVendedor_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var response = await client.GetAsync("/api/rma");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 12. CheckoutController (Checkout: Comprador; Ordenes: Admin) ──────────

    [Fact]
    public async Task Checkout_SinToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/checkout/ordenes");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CheckoutOrdenes_RolComprador_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var response = await client.GetAsync("/api/checkout/ordenes");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CheckoutOrdenes_RolAdmin_Retorna200()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var response = await client.GetAsync("/api/checkout/ordenes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CheckoutProcesar_RolAdmin_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var body = new
        {
            VendedorId = Guid.NewGuid(),
            ProductoId = Guid.NewGuid(),
            Cantidad = 1,
            PrecioUnitario = 10m
        };
        var response = await client.PostAsJsonAsync("/api/checkout", body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CheckoutProcesar_RolVendedor_Retorna403()
    {
        var client = _factory.CreateClientWithAuth("Vendedor");
        var body = new
        {
            VendedorId = Guid.NewGuid(),
            ProductoId = Guid.NewGuid(),
            Cantidad = 1,
            PrecioUnitario = 10m
        };
        var response = await client.PostAsJsonAsync("/api/checkout", body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── 13. AuthController (Fallback vs AllowAnonymous) ───────────────────────

    [Fact]
    public async Task AuthLogin_PermiteAnonimo_SinToken()
    {
        // El endpoint /api/auth/login tiene [AllowAnonymous] así que la FallbackPolicy no bloquea.
        // Con credenciales incorrectas, el controller retorna 401 desde su propia lógica.
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { Username = "no_existe", Password = "123" });
        // Debe retornar 401 (de lógica interna del controller) y NO 403 ni 500
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
