using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
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
using MarketplaceERP.Shared.DTOs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Nuevo ensayo B2C integrado en el sandbox: checkout real HTTP (pago Moq) ->
/// Fulfillment HTTP -> devolución RMA HTTP -> asiento Finance por servicio.
/// NO se usa pasarela real, ni entorno remoto, ni se finge transición externa
/// de entrega. Falla cerrado salvo postgres@localhost:5432.
/// </summary>
public sealed class CheckoutB2CIntegralQaPostgresTests : IAsyncLifetime
{
    private readonly string _database = "ci_checkout_b2c_rma_" + Guid.NewGuid().ToString("N");
    private string? _rootConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("TEST_PG_CONNECTION QA local es obligatoria.");
        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                cs.Host, StringComparer.OrdinalIgnoreCase) ||
            cs.Port != 5432 || cs.Database != "postgres" ||
            cs.Username != "postgres" || string.IsNullOrWhiteSpace(cs.Password))
            throw new InvalidOperationException("Solo PostgreSQL local efímero; nunca Neon.");
        _rootConnection = cs.ConnectionString;
        cs.Database = _database;
        _testConnection = cs.ConnectionString;
        await using (var root = new NpgsqlConnection(_rootConnection))
        {
            await root.OpenAsync();
            await using var create = new NpgsqlCommand(
                "CREATE DATABASE \"" + _database + "\"", root);
            await create.ExecuteNonQueryAsync();
            _created = true;
        }
        await using (var core = NewCore()) await core.Database.MigrateAsync();
        await using (var catalog = NewCatalog()) await catalog.Database.MigrateAsync();
        await using (var finance = NewFinance()) await finance.Database.MigrateAsync();
    }

    private CoreDbContext NewCore() => new(new DbContextOptionsBuilder<CoreDbContext>()
        .UseNpgsql(_testConnection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "core"))
        .Options);
    private CatalogDbContext NewCatalog() => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseNpgsql(_testConnection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
        .Options);
    private FinanceDbContext NewFinance() => new(new DbContextOptionsBuilder<FinanceDbContext>()
        .UseNpgsql(_testConnection, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
        .Options);

    [Fact]
    public async Task B2CDesdeCheckoutHttpHastaRmaYAsiento_FinanceCatalogCore_Conciliados()
    {
        if (!_created) throw new InvalidOperationException("Base efímera QA no creada.");
        var productId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var sku = "QA-CHK-E2E-" + Guid.NewGuid().ToString("N")[..10];
        const int cantidad = 2;
        const decimal precioB2C = 75m;
        const decimal monto = precioB2C * cantidad;

        await using (var core = NewCore())
        {
            core.Proveedores.Add(new Proveedor
            {
                Id = supplierId,
                RazonSocial = "Proveedor QA B2C S.A. de C.V.",
                NombreComercial = "Proveedor QA B2C", Activo = true,
                Nit = "0000-000000-000-0"
            });
            await core.SaveChangesAsync();
        }
        await using (var catalog = NewCatalog())
        {
            catalog.Productos.Add(new Producto
            {
                Id = productId, VendedorId = sellerId, CategoriaId = Guid.NewGuid(),
                Sku = sku, Nombre = "Producto checkout→RMA QA",
                CategoriaNombre = "Pruebas integrales",
                PrecioB2C = precioB2C, PrecioB2B = 60m,
                StockInicial = 0, StockMinimo = 1,
                Moneda = "USD", EstaPublicado = true, EstadoProducto = "Activo",
                ProveedorNombre = "Proveedor QA B2C",
                PaisOrigen = "El Salvador", PesoKg = 1m,
                DimensionesCm = "10x10x10", UnidadMedida = "Unidad"
            });
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = locationId, Codigo = "QA-E2E-PAS-01-R-01-N-01-P-01",
                Pasillo = "QA", Rack = "01", Nivel = 1, Posicion = "01",
                EstaOcupada = true
            });
            catalog.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productId,
                UbicacionAlmacenId = locationId,
                UbicacionCodigo = "QA-E2E-PAS-01-R-01-N-01-P-01",
                CantidadTotal = 5, CantidadReservada = 0
            });
            await catalog.SaveChangesAsync();
        }

        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(true, "QA-SIM-B2C-E2E-20261109", null));
        var commissions = new Mock<ICommissionService>(MockBehavior.Strict);
        commissions.Setup(x => x.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()))
            .ReturnsAsync((Guid _, decimal total) => decimal.Round(total * .1m, 2));
        const string issuer = "qa-b2c-e2e-local";
        const string secret = "ONLY-LOCAL-B2C-E2E-QA-JWT-KEY-20261109-NO-PRODUCTION";
        var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<CoreDbContext>(o => o.UseNpgsql(_testConnection));
                services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(_testConnection));
                services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(_testConnection));
                services.AddScoped<IStockService>(sp =>
                    new StockService(sp.GetRequiredService<CatalogDbContext>(),
                        sp.GetRequiredService<ILogger<StockService>>()));
                services.AddScoped<IFulfillmentService>(sp =>
                    new FulfillmentService(sp.GetRequiredService<CatalogDbContext>(),
                        sp.GetRequiredService<IStockService>(),
                        sp.GetRequiredService<ILogger<FulfillmentService>>()));
                services.AddScoped<IInventoryService>(sp =>
                    (IInventoryService)ActivatorUtilities.CreateInstance(sp,
                        typeof(IInventoryService).Assembly.GetType(
                            "MarketplaceERP.Catalog.Services.InventoryService", throwOnError: true)!,
                        sp.GetRequiredService<CatalogDbContext>()));
                services.AddScoped<IRmaService, RmaService>();
                services.AddScoped<CheckoutOrchestrator>();
                services.AddSingleton(gateway.Object);
                services.AddSingleton(commissions.Object);
                services.AddSingleton(new PaymentOperationsMode(false));
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = secret, Issuer = issuer, Audience = issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers()
                    .AddApplicationPart(typeof(CheckoutController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

        string Token(Guid id, string role)
        {
            var jwt = new JwtSecurityToken(
                issuer: issuer, audience: issuer,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                    new Claim(ClaimTypes.Role, role)
                },
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(jwtKey, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
        using var anonymous = server.CreateClient();
        using (var denied = await anonymous.PostAsJsonAsync(
            "/api/checkout", new CheckoutRequest(productId, cantidad)))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var buyer = server.CreateClient();
        buyer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(buyerId, "Comprador"));
        buyer.DefaultRequestHeaders.Add("Idempotency-Key", "QA-FULL-B2C-HTTP-E2E-20261109");

        using var created = await buyer.PostAsJsonAsync(
            "/api/checkout", new CheckoutRequest(productId, cantidad));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var orderId = createdJson.RootElement.GetProperty("ordenId").GetGuid();
        using (var repeat = await buyer.PostAsJsonAsync(
            "/api/checkout", new CheckoutRequest(productId, cantidad)))
        {
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
            using var repeatJson = JsonDocument.Parse(await repeat.Content.ReadAsStringAsync());
            Assert.Equal(orderId, repeatJson.RootElement.GetProperty("ordenId").GetGuid());
        }
        gateway.Verify(g => g.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once());

        string orderNumber;
        await using (var verify = NewFinance())
        {
            var order = await verify.Ordenes.Include(x => x.Items)
                .Include(x => x.SplitPago).SingleAsync(x => x.Id == orderId);
            Assert.Equal(buyerId, order.CompradorId);
            Assert.Equal(sellerId, order.VendedorId);
            Assert.Equal(EstadoOrden.Aprobada, order.Estado);
            Assert.Equal(TipoCliente.B2C, order.TipoCliente);
            Assert.Equal(monto, order.MontoTotal);
            Assert.Equal("FacturaConsumidorFinal", order.TipoComprobante);
            Assert.Single(order.Items, i => i.ProductoId == productId && i.Cantidad == cantidad);
            Assert.NotNull(order.SplitPago);
            Assert.Equal(monto, order.SplitPago.MontoTotal);
            Assert.Equal(15m, order.SplitPago.MontoMarketplace);
            Assert.Single(await verify.IntentosPago.AsNoTracking().ToListAsync());
            orderNumber = order.NumeroOrden;

            // Límite explícito del E2E: el checkout no publica asientos por sí
            // solo. Se invoca el servicio de contabilidad para demostrar la
            // compatibilidad contable de la misma orden sin fabricar partidas.
            var contabilidad = new ContabilidadService(
                verify, NullLogger<ContabilidadService>.Instance);
            var partida = await contabilidad.GenerarAsientoVentaAsync(
                orderId, monto, 0m, order.TipoComprobante);
            Assert.Equal(monto, partida.TotalDebe);
            Assert.Equal(monto, partida.TotalHaber);
        }
        await using (var catalog = NewCatalog())
        {
            var reservation = await catalog.ReservasStock.AsNoTracking()
                .SingleAsync(x => x.OrdenId == orderId);
            Assert.Equal(EstadoReserva.Confirmada, reservation.Estado);
            Assert.Equal(cantidad, reservation.CantidadOriginal);
            var stock = await catalog.StockFisico.AsNoTracking().SingleAsync();
            Assert.Equal(5, stock.CantidadTotal);
            Assert.Equal(cantidad, stock.CantidadReservada);
        }

        using var admin = server.CreateClient();
        admin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(Guid.NewGuid(), "Admin"));
        var pickRoute = $"/api/fulfillment/ordenes/{orderId}/generar-picklist";
        using (var roleDenied = await buyer.PostAsync(pickRoute, null))
            Assert.Equal(HttpStatusCode.Forbidden, roleDenied.StatusCode);
        using var pick = await admin.PostAsync(pickRoute, null);
        Assert.Equal(HttpStatusCode.OK, pick.StatusCode);
        using var pickJson = JsonDocument.Parse(await pick.Content.ReadAsStringAsync());
        var pickId = pickJson.RootElement.GetProperty("pickListId").GetGuid();
        var pickItemId = pickJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        using (var picking = await admin.PostAsJsonAsync(
            $"/api/fulfillment/picklists/{pickId}/confirmar",
            new[] { new ConfirmarItemPickingRequest(pickItemId, cantidad) }))
            Assert.Equal(HttpStatusCode.OK, picking.StatusCode);
        using (var packed = await admin.PostAsJsonAsync(
            $"/api/fulfillment/ordenes/{orderId}/empacar",
            new EmpacarDespachoRequest(
                orderId, orderNumber, 2m, 30m, 20m, 10m, "Transportista QA",
                "San Salvador", "Centro", "Dirección QA",
                "Destinatario QA", "7000-0000")))
            Assert.Equal(HttpStatusCode.OK, packed.StatusCode);

        Guid packageId;
        string guide;
        await using (var catalog = NewCatalog())
        {
            var pkg = await catalog.PaquetesDespacho.AsNoTracking().SingleAsync();
            packageId = pkg.Id;
            guide = pkg.NumeroGuia;
        }
        using (var shipped = await admin.PostAsync(
            $"/api/fulfillment/paquetes/{packageId}/despachar", null))
            Assert.Equal(HttpStatusCode.OK, shipped.StatusCode);
        using (var repeatShip = await admin.PostAsync(
            $"/api/fulfillment/paquetes/{packageId}/despachar", null))
            Assert.Equal(HttpStatusCode.OK, repeatShip.StatusCode);

        using (var earlyReturn = await admin.PostAsJsonAsync(
            "/api/rma", new RegistrarRmaRequest(orderId, productId, 1, "Prematuro QA")))
            Assert.Equal(HttpStatusCode.BadRequest, earlyReturn.StatusCode);

        // No existe integración al operador last-mile en esta prueba.
        // Marcar entregado es un paso controlado explícito de escenario.
        await using (var finance = NewFinance())
        {
            var order = await finance.Ordenes.SingleAsync(x => x.Id == orderId);
            Assert.Equal(EstadoOrden.Enviada, order.Estado);
            Assert.Equal(guide, order.GuiaEnvio);
            order.Estado = EstadoOrden.Entregada;
            order.FechaEntregaReal = DateTime.UtcNow;
            await finance.SaveChangesAsync();
        }
        using var returned = await admin.PostAsJsonAsync(
            "/api/rma", new RegistrarRmaRequest(
                orderId, productId, 1, "ReStock QA de una unidad"));
        Assert.Equal(HttpStatusCode.Created, returned.StatusCode);
        using var returnedJson = JsonDocument.Parse(await returned.Content.ReadAsStringAsync());
        var rmaId = returnedJson.RootElement.GetProperty("id").GetGuid();

        var inspect = new DictaminarRmaRequest(
            ClasificacionRMA.ReStock, "Inspector QA", "Recuperable",
            UbicacionDestinoId: locationId, OperacionId: Guid.NewGuid());
        using (var inspected = await admin.PostAsJsonAsync(
            $"/api/rma/{rmaId}/inspeccionar", inspect))
            Assert.Equal(HttpStatusCode.OK, inspected.StatusCode);
        using (var again = await admin.PostAsJsonAsync(
            $"/api/rma/{rmaId}/inspeccionar",
            inspect with { OperacionId = Guid.NewGuid() }))
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        await using var coreFinal = NewCore();
        await using var catalogFinal = NewCatalog();
        await using var financeFinal = NewFinance();
        Assert.True((await coreFinal.Proveedores.AsNoTracking()
            .SingleAsync(x => x.Id == supplierId)).Activo);
        var finalOrder = await financeFinal.Ordenes.AsNoTracking()
            .SingleAsync(x => x.Id == orderId);
        Assert.Equal(EstadoOrden.Entregada, finalOrder.Estado);
        Assert.Equal(guide, finalOrder.GuiaEnvio);
        Assert.Equal(monto, finalOrder.MontoTotal);
        Assert.Equal(1, await financeFinal.SplitsPago.CountAsync(x => x.OrdenId == orderId));
        Assert.Equal(1, await financeFinal.IntentosPago.CountAsync(x => x.OrdenId == orderId));

        var finalStock = await catalogFinal.StockFisico.AsNoTracking()
            .SingleAsync(x => x.ProductoId == productId);
        Assert.Equal(4, finalStock.CantidadTotal);
        Assert.Equal(0, finalStock.CantidadReservada);
        Assert.Equal(1, await catalogFinal.MovimientosInventario.CountAsync(x =>
            x.OrdenId == orderId &&
            x.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound));
        Assert.Equal(1, await catalogFinal.MovimientosInventario.CountAsync(x =>
            x.RmaId == rmaId &&
            x.TipoMovimiento == TipoMovimientoInventario.RmaRestock));
        Assert.Equal(EstadoRMA.Completado,
            (await catalogFinal.DevolucionesRMA.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(EstadoPicking.Completado,
            (await catalogFinal.PickLists.AsNoTracking().SingleAsync()).Estado);
        Assert.Equal(EstadoPacking.Despachado,
            (await catalogFinal.PaquetesDespacho.AsNoTracking().SingleAsync()).Estado);

        var asientoFinal = await financeFinal.PartidasContables.Include(x => x.Lineas)
            .SingleAsync(x => x.TipoOperacion == "Venta" && x.ReferenciaOrigenId == orderId);
        Assert.Equal(monto, asientoFinal.TotalDebe);
        Assert.Equal(asientoFinal.TotalDebe, asientoFinal.TotalHaber);
        Assert.Equal(asientoFinal.TotalDebe, asientoFinal.Lineas.Sum(x => x.Debe));
        Assert.Equal(asientoFinal.TotalHaber, asientoFinal.Lineas.Sum(x => x.Haber));
        Assert.Contains(asientoFinal.Lineas, x => x.CodigoCuenta == "1101");
        gateway.Verify(g => g.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once());
    }

    public async Task DisposeAsync()
    {
        if (!_created || _rootConnection is null ||
            !_database.StartsWith("ci_checkout_b2c_rma_", StringComparison.Ordinal) ||
            _database.Length != "ci_checkout_b2c_rma_".Length + 32)
            return;
        if (_testConnection is not null)
        {
            using var pool = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(pool);
        }
        await using var root = new NpgsqlConnection(_rootConnection);
        await root.OpenAsync();
        await using (var count = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = @db", root))
        {
            count.Parameters.AddWithValue("db", _database);
            var active = Convert.ToInt32(await count.ExecuteScalarAsync());
            if (active != 0)
                throw new InvalidOperationException(
                    $"No se elimina base temporal QA con {active} sesiones.");
        }
        await using var drop = new NpgsqlCommand(
            "DROP DATABASE IF EXISTS \"" + _database + "\"", root);
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }
}
