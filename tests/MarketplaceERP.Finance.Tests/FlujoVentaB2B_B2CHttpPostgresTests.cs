using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Venta sintética B2B/B2C: tres esquemas en una base PostgreSQL 15 temporal,
/// API HTTP real con JWT Admin y conciliación de OMS/WMS/contabilidad.
/// La orden se crea APROBADA con datos de prueba; NO prueba checkout/pago real.
/// </summary>
public sealed class FlujoVentaB2B_B2CHttpPostgresTests : IAsyncLifetime
{
    private readonly string _dbName = "ci_venta_integral_" + Guid.NewGuid().ToString("N");
    private string? _admin;
    private string? _connection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CI exige PostgreSQL local desechable.");
            return;
        }

        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                cs.Host, StringComparer.OrdinalIgnoreCase) ||
            cs.Port != 5432 || cs.Database != "postgres" ||
            cs.Username != "postgres" || string.IsNullOrWhiteSpace(cs.Password))
            throw new InvalidOperationException("Solo postgres@localhost:5432/postgres; nunca Neon.");

        _admin = cs.ConnectionString;
        cs.Database = _dbName;
        _connection = cs.ConnectionString;
        await using (var admin = new NpgsqlConnection(_admin))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
            await cmd.ExecuteNonQueryAsync();
            _created = true;
        }

        await using (var core = Core()) await core.Database.MigrateAsync();
        await using (var catalog = Catalog()) await catalog.Database.MigrateAsync();
        await using (var finance = Finance()) await finance.Database.MigrateAsync();
    }

    private CoreDbContext Core() => new(
        new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(_connection!, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "core")).Options);
    private CatalogDbContext Catalog() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_connection!, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")).Options);
    private FinanceDbContext Finance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_connection!, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "finance")).Options);

    [Theory]
    [InlineData(TipoCliente.B2C)]
    [InlineData(TipoCliente.B2B)]
    public async Task VentaSintetica_CoreCatalogFinance_PickingPackingDespachoHttp_Conciliados(
        TipoCliente tipoCliente)
    {
        if (!_created) return;

        var orderId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var numeroOrden = "QA-" + tipoCliente + "-" + Guid.NewGuid().ToString("N")[..10];
        var sku = "QA-" + tipoCliente + "-" + Guid.NewGuid().ToString("N")[..10];
        var ubicacionCodigo = "PAS-QA-RCK-01-NIV-01-POS-01";
        var unitPrice = tipoCliente == TipoCliente.B2B ? 200m : 225m;
        const int quantity = 3;
        var subTotal = unitPrice * quantity;
        var iva = decimal.Round(subTotal * .13m, 2);
        var comprobante = tipoCliente == TipoCliente.B2B ? "CreditoFiscalCCF" : "FacturaConsumidorFinal";

        await using (var core = Core())
        {
            core.Proveedores.Add(new Proveedor
            {
                Id = providerId, RazonSocial = "Proveedor sintético QA S.A. de C.V.",
                NombreComercial = "Proveedor QA", Activo = true,
                Nit = "0000-000000-000-0"
            });
            await core.SaveChangesAsync();
        }
        await using (var catalog = Catalog())
        {
            catalog.Productos.Add(new Producto
            {
                Id = productId, Sku = sku, Nombre = "Producto sintético de evaluación",
                VendedorId = vendorId, CategoriaId = Guid.NewGuid(),
                CategoriaNombre = "Pruebas QA", Subcategoria = "Flujo integrado",
                PrecioB2C = 225m, PrecioB2B = 200m,
                StockInicial = 12, StockMinimo = 2, Moneda = "USD",
                ProveedorNombre = "Proveedor QA", PaisOrigen = "El Salvador",
                PesoKg = 1m, DimensionesCm = "10x10x10",
                UnidadMedida = "Unidad", EstadoProducto = "Activo",
                Descripcion = "Solo datos sintéticos"
            });
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = locationId, Codigo = ubicacionCodigo,
                Pasillo = "QA", Rack = "01", Nivel = 1,
                Posicion = "01", EstaOcupada = true
            });
            catalog.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productId,
                UbicacionAlmacenId = locationId, UbicacionCodigo = ubicacionCodigo,
                CantidadTotal = 12
            });
            await catalog.SaveChangesAsync();
            var stockService = new StockService(catalog, NullLogger<StockService>.Instance);
            var reserva = await stockService.ReservarStockAsync(productId, quantity, orderId);
            Assert.True(reserva.Exitoso, reserva.MensajeError);
            await stockService.ConfirmarReservaAsync(reserva.ReservaId);
        }
        await using (var finance = Finance())
        {
            finance.Ordenes.Add(new Orden
            {
                Id = orderId, NumeroOrden = numeroOrden,
                CompradorId = buyerId, VendedorId = vendorId,
                TipoCliente = tipoCliente, TipoComprobante = comprobante,
                CondicionPago = tipoCliente == TipoCliente.B2B ? "Credito30" : "Inmediato",
                Estado = EstadoOrden.Aprobada, Moneda = "USD",
                SubTotal = subTotal, ImpuestoIva = iva,
                MontoTotal = subTotal + iva, DireccionEntrega = "Dirección sintética",
                Items = new List<ItemOrden>
                {
                    new()
                    {
                        Id = Guid.NewGuid(), ProductoId = productId,
                        SkuReferencia = sku, NombreProducto = "Producto sintético de evaluación",
                        Cantidad = quantity, PrecioUnitario = unitPrice
                    }
                }
            });
            await finance.SaveChangesAsync();
            var accounting = new ContabilidadService(
                finance, NullLogger<ContabilidadService>.Instance);
            var asiento = await accounting.GenerarAsientoVentaAsync(
                orderId, subTotal, iva, comprobante);
            Assert.Equal(subTotal + iva, asiento.TotalDebe);
            Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        }

        const string issuer = "qa-fulfillment-local";
        const string secret = "QA-FULFILLMENT-POSTGRES-ONLY-JWT-DO-NOT-DEPLOY-2026";
        var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<CoreDbContext>(o => o.UseNpgsql(_connection));
                services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(_connection));
                services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(_connection));
                services.AddScoped<IStockService>(sp =>
                    new StockService(sp.GetRequiredService<CatalogDbContext>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<StockService>>()));
                services.AddScoped<IFulfillmentService>(sp =>
                    new FulfillmentService(sp.GetRequiredService<CatalogDbContext>(),
                        sp.GetRequiredService<IStockService>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FulfillmentService>>()));
                services.AddScoped<IInventoryService>(sp =>
                    (IInventoryService)ActivatorUtilities.CreateInstance(sp,
                        typeof(IInventoryService).Assembly.GetType(
                            "MarketplaceERP.Catalog.Services.InventoryService", throwOnError: true)!,
                        sp.GetRequiredService<CatalogDbContext>()));
                services.AddScoped<IRmaService, RmaService>();
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = secret, Issuer = issuer, Audience = issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers()
                    .AddApplicationPart(typeof(FulfillmentController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

        var route = $"/api/fulfillment/ordenes/{orderId}/generar-picklist";
        using var anon = server.CreateClient();
        using (var unauthorized = await anon.PostAsync(route, null))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var jwt = new JwtSecurityToken(
            issuer: issuer, audience: issuer,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(jwtKey, SecurityAlgorithms.HmacSha256));
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));

        using var pickResponse = await http.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.OK, pickResponse.StatusCode);
        using var pickJson = JsonDocument.Parse(await pickResponse.Content.ReadAsStringAsync());
        var pickId = pickJson.RootElement.GetProperty("pickListId").GetGuid();
        var itemId = pickJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(quantity,
            pickJson.RootElement.GetProperty("items")[0].GetProperty("cantidadSolicitada").GetInt32());

        using var confirm = await http.PostAsJsonAsync(
            $"/api/fulfillment/picklists/{pickId}/confirmar",
            new[] { new ConfirmarItemPickingRequest(itemId, quantity) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using var packing = await http.PostAsJsonAsync(
            $"/api/fulfillment/ordenes/{orderId}/empacar",
            new EmpacarDespachoRequest(
                orderId, numeroOrden, 3m, 30m, 20m, 10m, "Transportista QA",
                "San Salvador", "Centro", "Dirección QA",
                "Destinatario QA", "7000-0000"));
        Assert.Equal(HttpStatusCode.OK, packing.StatusCode);

        Guid packageId;
        string guide;
        await using (var c = Catalog())
        {
            var package = await c.PaquetesDespacho.AsNoTracking().SingleAsync();
            packageId = package.Id;
            guide = package.NumeroGuia;
        }

        using var shipped = await http.PostAsync(
            $"/api/fulfillment/paquetes/{packageId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, shipped.StatusCode);
        using var retry = await http.PostAsync(
            $"/api/fulfillment/paquetes/{packageId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        await using var verifyCore = Core();
        await using var verifyCatalog = Catalog();
        await using var verifyFinance = Finance();
        Assert.True((await verifyCore.Proveedores.AsNoTracking().SingleAsync()).Activo);

        var order = await verifyFinance.Ordenes.AsNoTracking().SingleAsync();
        Assert.Equal(tipoCliente, order.TipoCliente);
        Assert.Equal(EstadoOrden.Enviada, order.Estado);
        Assert.Equal(guide, order.GuiaEnvio);
        Assert.NotNull(order.FechaEnvio);
        Assert.Equal(subTotal + iva, order.MontoTotal);
        Assert.Equal("USD", order.Moneda);

        var pick = await verifyCatalog.PickLists.Include(x => x.Items).SingleAsync();
        Assert.Equal(pickId, pick.Id);
        Assert.Equal(EstadoPicking.Completado, pick.Estado);
        Assert.Equal(quantity, pick.Items.Sum(x => x.CantidadRecogida));
        var packageFinal = await verifyCatalog.PaquetesDespacho.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoPacking.Despachado, packageFinal.Estado);
        Assert.Equal(guide, packageFinal.NumeroGuia);

        var balance = await verifyCatalog.StockFisico.AsNoTracking().SingleAsync();
        Assert.Equal(9, balance.CantidadTotal);
        Assert.Equal(0, balance.CantidadReservada);
        Assert.Equal(1, await verifyCatalog.MovimientosInventario.CountAsync(x =>
            x.OrdenId == orderId &&
            x.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound));

        var partida = await verifyFinance.PartidasContables.Include(x => x.Lineas)
            .SingleAsync(x => x.TipoOperacion == "Venta" && x.ReferenciaOrigenId == orderId);
        Assert.Equal(subTotal + iva, partida.TotalDebe);
        Assert.Equal(partida.TotalDebe, partida.TotalHaber);
        Assert.Equal(partida.TotalDebe, partida.Lineas.Sum(x => x.Debe));
        Assert.Equal(partida.TotalHaber, partida.Lineas.Sum(x => x.Haber));
        Assert.Single(partida.Lineas.Where(x => x.CodigoCuenta ==
            (tipoCliente == TipoCliente.B2B ? "1105" : "1101")));

        // La devolucion exige una venta entregada. Antes, responder 400 sin
        // crear un RMA; ni el comprador ni la fecha se infieren del request.
        using (var invalidBeforeDelivery = await http.PostAsJsonAsync("/api/rma",
            new RegistrarRmaRequest(orderId, productId, 1, "RMA prematuro")))
            Assert.Equal(HttpStatusCode.BadRequest, invalidBeforeDelivery.StatusCode);

        // Entrega confirmada sintéticamente: esta transición NO representa
        // una prueba de transportista ni un endpoint de última milla.
        await using (var delivered = Finance())
        {
            var entity = await delivered.Ordenes.SingleAsync(x => x.Id == orderId);
            entity.Estado = EstadoOrden.Entregada;
            entity.FechaEntregaReal = DateTime.UtcNow;
            await delivered.SaveChangesAsync();
        }

        using (var tooMany = await http.PostAsJsonAsync("/api/rma",
            new RegistrarRmaRequest(orderId, productId, quantity + 1, "Excede venta")))
            Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        using var requestRma = await http.PostAsJsonAsync("/api/rma",
            new RegistrarRmaRequest(orderId, productId, 1, "Devolución QA de una unidad"));
        Assert.Equal(HttpStatusCode.Created, requestRma.StatusCode);
        using var rmaPayload = JsonDocument.Parse(await requestRma.Content.ReadAsStringAsync());
        var rmaId = rmaPayload.RootElement.GetProperty("id").GetGuid();

        var inspection = new DictaminarRmaRequest(
            ClasificacionRMA.ReStock, "Inspector QA", "Unidad apta para reventa",
            UbicacionDestinoId: locationId, OperacionId: Guid.NewGuid());
        using (var inspect = await http.PostAsJsonAsync(
            $"/api/rma/{rmaId}/inspeccionar", inspection))
            Assert.Equal(HttpStatusCode.OK, inspect.StatusCode);

        // Un reintento con nueva OperacionId mantiene un solo retorno al WMS.
        using (var retryInspect = await http.PostAsJsonAsync(
            $"/api/rma/{rmaId}/inspeccionar",
            inspection with { OperacionId = Guid.NewGuid() }))
            Assert.Equal(HttpStatusCode.OK, retryInspect.StatusCode);

        // La misma clase de error afectaba a GET /api/rma porque la consulta
        // incluye Producto. El contrato plano no serializa relaciones EF.
        using (var listResponse = await http.GetAsync("/api/rma"))
        {
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using var listJson = JsonDocument.Parse(
                await listResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, listJson.RootElement.GetArrayLength());
            Assert.Equal(rmaId, listJson.RootElement[0].GetProperty("id").GetGuid());
            Assert.Equal("Completado",
                listJson.RootElement[0].GetProperty("estado").GetString());
            Assert.False(listJson.RootElement[0].TryGetProperty("producto", out _));
        }

        await using var finalCatalog = Catalog();
        var finalStock = await finalCatalog.StockFisico.AsNoTracking().SingleAsync();
        Assert.Equal(10, finalStock.CantidadTotal);
        Assert.Equal(0, finalStock.CantidadReservada);
        var completedRma = await finalCatalog.DevolucionesRMA.AsNoTracking().SingleAsync();
        Assert.Equal(rmaId, completedRma.Id);
        Assert.Equal(EstadoRMA.Completado, completedRma.Estado);
        Assert.Equal(ClasificacionRMA.ReStock, completedRma.Clasificacion);
        Assert.Equal(1, await finalCatalog.MovimientosInventario.CountAsync(m =>
            m.RmaId == rmaId && m.TipoMovimiento == TipoMovimientoInventario.RmaRestock));
        Assert.Equal(1, await finalCatalog.MovimientosInventario.CountAsync(m =>
            m.OrdenId == orderId && m.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound));
    }

    public async Task DisposeAsync()
    {
        if (!_created || _admin is null ||
            !_dbName.StartsWith("ci_venta_integral_", StringComparison.Ordinal)) return;
        if (_connection is not null)
        {
            using var pool = new NpgsqlConnection(_connection);
            NpgsqlConnection.ClearPool(pool);
        }

        await using var root = new NpgsqlConnection(_admin);
        await root.OpenAsync();
        await using var cmd = root.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
        _created = false;
    }
}
