using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class CheckoutMultiSkuHttpTests : IAsyncLifetime
{
    private const string Prefix = "ci_checkout_http_multisku_";
    private const string Issuer = "checkout-multisku-http-tests";
    private const string Secret = "ONLY-LOCAL-TEST-KEY-CHECKOUT-MULTISKU-2026";
    private readonly string _database = Prefix + Guid.NewGuid().ToString("N");
    private string? _rootConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION")
            ?? throw new InvalidOperationException("Falta TEST_PG_CONNECTION");
        var c = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(c.Host, StringComparer.OrdinalIgnoreCase) ||
            c.Port != 5432 || c.Database != "postgres" || c.Username != "postgres" ||
            string.IsNullOrEmpty(c.Password))
            throw new InvalidOperationException("Prohibido conectar a PostgreSQL que no sea localhost:5432/postgres.");

        _rootConnection = c.ConnectionString;
        c.Database = _database;
        _testConnection = c.ConnectionString;

        await using (var admin = new NpgsqlConnection(_rootConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin);
            await create.ExecuteNonQueryAsync();
            _created = true;
        }

        await using (var catalog = NewCatalog()) await catalog.Database.MigrateAsync();
        await using (var finance = NewFinance()) await finance.Database.MigrateAsync();
    }

    private CatalogDbContext NewCatalog() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_testConnection!, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
            .Options);

    private FinanceDbContext NewFinance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!, x => x.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
            .Options);

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("uncertain")]
    [InlineData("noStock")]
    public async Task HttpV2Comercial_MultiSku_PagoUnico_YCompensacion(string escenario)
    {
        var seller = Guid.NewGuid();
        var buyer = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var location = Guid.NewGuid();
        await SeedCatalogAsync(seller, productA, productB, location, escenario == "noStock");

        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        if (escenario == "uncertain")
            gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
                .ThrowsAsync(new IOException("Respuesta del proveedor incierta"));
        else
            gateway.Setup(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
                .ReturnsAsync(escenario == "rejected"
                    ? new PagoResult(false, null, "Fondos insuficientes")
                    : new PagoResult(true, "TXN-MULTISKU-APPROVED", null));

        var commission = new Mock<ICommissionService>(MockBehavior.Strict);
        commission.Setup(x => x.CalcularComisionAsync(seller, 20m)).ReturnsAsync(2m);

        using var server = CreateHost(gateway.Object, commission.Object, disabled: false);
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(buyer, "Comprador"));
        const string key = "CHECKOUT-MULTISKU-HTTP-20261113";
        http.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var payload = new
        {
            items = new[]
            {
                new { productoId = productB, cantidad = 1 },
                new { productoId = productA, cantidad = 1 }
            }
        };

        using var response = await http.PostAsJsonAsync("/api/checkout/v2/comprar", payload);
        var body = await response.Content.ReadAsStringAsync();

        if (escenario == "noStock")
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("StockInsuficiente", body);
            gateway.Verify(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
            await using var financeEmpty = NewFinance();
            Assert.Empty(await financeEmpty.Ordenes.ToListAsync());
            Assert.Empty(await financeEmpty.IntentosPago.ToListAsync());
            Assert.Empty(await financeEmpty.IntentoReservasPago.ToListAsync());
            await using var catalogEmpty = NewCatalog();
            Assert.Empty(await catalogEmpty.ReservasStock.ToListAsync());
            Assert.All(await catalogEmpty.StockFisico.AsNoTracking().ToListAsync(),
                x => Assert.Equal(0, x.CantidadReservada));
            return;
        }

        Assert.Equal(
            escenario is "rejected" or "uncertain" ? HttpStatusCode.Conflict : HttpStatusCode.OK,
            response.StatusCode);
        if (escenario == "approved")
        {
            Assert.Contains("TXN-MULTISKU-APPROVED", body);
            using var repeated = await http.PostAsJsonAsync("/api/checkout/v2/comprar", payload);
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            using var differentPayload = await http.PostAsJsonAsync("/api/checkout/v2/comprar",
                new { items = new[] {
                    new { productoId = productA, cantidad = 1 },
                    new { productoId = productB, cantidad = 2 }
                }});
            Assert.Equal(HttpStatusCode.Conflict, differentPayload.StatusCode);
            Assert.Contains("IdempotenciaPayloadDistinto",
                await differentPayload.Content.ReadAsStringAsync());
        }
        else if (escenario == "rejected")
            Assert.Contains("PagoRechazado", body);
        else
            Assert.Contains("PagoPendienteConciliacion", body);

        gateway.Verify(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once);
        await using var finance = NewFinance();
        var order = await finance.Ordenes.AsNoTracking().SingleAsync();
        var attempt = await finance.IntentosPago.AsNoTracking().SingleAsync();
        var links = await finance.IntentoReservasPago.AsNoTracking().ToListAsync();

        Assert.Equal(2, await finance.ItemsOrden.CountAsync());
        Assert.Equal(2, links.Count);
        Assert.All(links, x => Assert.Equal("CheckoutMultiSkuV2", x.Procedencia));

        await using var catalog = NewCatalog();
        var reservations = await catalog.ReservasStock.AsNoTracking()
            .Where(x => x.OrdenId == order.Id).ToListAsync();
        var stocks = await catalog.StockFisico.AsNoTracking().ToListAsync();
        Assert.Equal(2, reservations.Count);

        if (escenario == "approved")
        {
            Assert.Equal(EstadoOrden.Aprobada, order.Estado);
            Assert.Equal(EstadoIntentoPago.Completado, attempt.Estado);
            Assert.Equal(1, await finance.SplitsPago.CountAsync());
            Assert.Equal(1, await finance.PartidasContables.CountAsync(
                x => x.TipoOperacion == "Venta" && x.ReferenciaOrigenId == order.Id));
            var split = await finance.SplitsPago.AsNoTracking().SingleAsync();
            Assert.Equal(20m, split.MontoTotal);
            Assert.Equal(18m, split.MontoVendedor);
            Assert.Equal(2m, split.MontoMarketplace);
            Assert.All(reservations, x => Assert.Equal(EstadoReserva.Confirmada, x.Estado));
            Assert.All(stocks, x => Assert.Equal(1, x.CantidadReservada));
            Assert.All(stocks, x => Assert.Equal(1, x.CantidadTotal));
            gateway.VerifyNoOtherCalls();
        }
        else if (escenario == "rejected")
        {
            Assert.Equal(EstadoOrden.Cancelada, order.Estado);
            Assert.Equal(EstadoIntentoPago.Rechazado, attempt.Estado);
            Assert.Empty(await finance.SplitsPago.ToListAsync());
            Assert.Empty(await finance.PartidasContables.ToListAsync());
            Assert.All(reservations, x => Assert.Equal(EstadoReserva.Liberada, x.Estado));
            Assert.All(stocks, x => Assert.Equal(0, x.CantidadReservada));
        }
        else
        {
            Assert.Equal(EstadoOrden.Pendiente, order.Estado);
            Assert.Equal(EstadoIntentoPago.Incierto, attempt.Estado);
            Assert.Empty(await finance.SplitsPago.ToListAsync());
            Assert.Empty(await finance.PartidasContables.ToListAsync());
            Assert.All(reservations, x => Assert.Equal(EstadoReserva.Pendiente, x.Estado));
            Assert.All(stocks, x => Assert.Equal(1, x.CantidadReservada));

            using var retry = await http.PostAsJsonAsync("/api/checkout/v2/comprar", payload);
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
            Assert.Contains("CheckoutEnProcesoNoReemitir",
                await retry.Content.ReadAsStringAsync());
            gateway.Verify(x => x.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once);
        }

        // El seguimiento operativo debe reconocer el mapa 1:N sin falsos positivos
        // tanto para una compra aprobada como para rechazo o conciliación pendiente.
        var seguimientoController = new CheckoutSeguimientoController(finance, catalog)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"))
                }
            }
        };
        var seguimientoAction = await seguimientoController.Seguimiento(
            order.Id, CancellationToken.None);
        var seguimiento = Assert.IsType<SeguimientoCheckoutDto>(
            Assert.IsType<OkObjectResult>(seguimientoAction).Value);
        Assert.DoesNotContain("CHECKOUT_MULTISKU_MAPA_RESERVAS_INCONSISTENTE", seguimiento.Alertas);
        Assert.DoesNotContain("CHECKOUT_MULTISKU_RESERVAS_EN_ESTADO_INESPERADO", seguimiento.Alertas);

        if (escenario == "approved")
        {
            // El preflight administrativo era 1:1 y rechazaba los 2 SKU
            // pese a tener pago, split y reservas correctamente conciliados.
            // Es lectura de datos simulados en una base PG local efímera:
            // NO es una autorización de cierre ni un cobro real.
            var preflight = new CierreCheckoutPreflight(finance, catalog);
            var consistente = await preflight.EvaluarAsync(order.Id);
            Assert.Contains("ORDEN_NO_PENDIENTE", consistente.Bloqueos);
            Assert.DoesNotContain("ITEMS_O_PRECIO_NO_COINCIDEN", consistente.Bloqueos);
            Assert.DoesNotContain("INTENTO_NO_CONCILIADO", consistente.Bloqueos);
            Assert.DoesNotContain("MAPA_RESERVAS_MULTISKU_INCONSISTENTE", consistente.Bloqueos);
            Assert.DoesNotContain("RESERVA_NO_CONCILIADA", consistente.Bloqueos);
            Assert.DoesNotContain("SALDOS_WMS_INCONSISTENTES", consistente.Bloqueos);

            // Si cambia una cantidad del mapa 1:N, el preflight falla cerrado.
            var enlaceAlterado = await finance.IntentoReservasPago.FirstAsync();
            enlaceAlterado.Cantidad += 1;
            await finance.SaveChangesAsync();
            var inconsistente = await preflight.EvaluarAsync(order.Id);
            Assert.Contains("MAPA_RESERVAS_MULTISKU_INCONSISTENTE", inconsistente.Bloqueos);
        }
    }

    [Fact]
    public async Task PagosDeshabilitados_BloqueaAntesDeCrearOrdenOReserva()
    {
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var commission = new Mock<ICommissionService>(MockBehavior.Strict);
        using var server = CreateHost(new PagoServiceDisabled(), commission.Object, disabled: true);
        using var http = server.CreateClient();
        var buyer = Guid.NewGuid();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Token(buyer, "Comprador"));
        http.DefaultRequestHeaders.Add("Idempotency-Key", "CHECKOUT-MULTISKU-DISABLED");
        using var response = await http.PostAsJsonAsync("/api/checkout/v2/comprar",
            new { items = new[] {
                new { productoId = Guid.NewGuid(), cantidad = 1 },
                new { productoId = Guid.NewGuid(), cantidad = 1 }
            }});
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("PagosDeshabilitados", await response.Content.ReadAsStringAsync());
        await using var finance = NewFinance();
        Assert.Empty(await finance.Ordenes.ToListAsync());
        Assert.Empty(await finance.IntentosPago.ToListAsync());
    }

    [Fact]
    public async Task HttpV2Comercial_ProtegeRoles_YNoEscribeConPagosDeshabilitados()
    {
        // HTTP TestServer real contra PostgreSQL efímero. Las credenciales
        // son JWT sintéticos del host aislado, nunca tokens de STAGING.
        var commission = new Mock<ICommissionService>(MockBehavior.Strict);
        using var server = CreateHost(new PagoServiceDisabled(), commission.Object, disabled: true);
        const string endpoint = "/api/checkout/v2/comprar";
        var solicitud = new { items = new[] {
            new { productoId = Guid.NewGuid(), cantidad = 1 },
            new { productoId = Guid.NewGuid(), cantidad = 1 }
        } };

        using (var anonimo = server.CreateClient())
        {
            anonimo.DefaultRequestHeaders.Add("Idempotency-Key", "F267-CHECKOUT-ROLES-ANON");
            using var respuesta = await anonimo.PostAsJsonAsync(endpoint, solicitud);
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        }

        foreach (var rol in new[] { "Admin", "Vendedor" })
        {
            using var ajeno = server.CreateClient();
            ajeno.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token(Guid.NewGuid(), rol));
            ajeno.DefaultRequestHeaders.Add("Idempotency-Key", "F267-CHECKOUT-ROLES-" + rol);
            using var respuesta = await ajeno.PostAsJsonAsync(endpoint, solicitud);
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }

        using (var comprador = server.CreateClient())
        {
            comprador.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Token(Guid.NewGuid(), "Comprador"));
            comprador.DefaultRequestHeaders.Add("Idempotency-Key", "F267-CHECKOUT-ROLES-BUYER");
            using var respuesta = await comprador.PostAsJsonAsync(endpoint, solicitud);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
            Assert.Contains("PagosDeshabilitados", await respuesta.Content.ReadAsStringAsync());
        }

        await using var finance = NewFinance();
        await using var catalog = NewCatalog();
        Assert.Empty(await finance.Ordenes.ToListAsync());
        Assert.Empty(await finance.IntentosPago.ToListAsync());
        Assert.Empty(await finance.IntentoReservasPago.ToListAsync());
        Assert.Empty(await catalog.ReservasStock.ToListAsync());
        commission.VerifyNoOtherCalls();
    }

    private async Task SeedCatalogAsync(
        Guid seller, Guid productA, Guid productB, Guid location, bool secondWithoutStock)
    {
        await using var catalog = NewCatalog();
        catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
        {
            Id = location, Codigo = "V2-MULTISKU-P-1-R-1-N-1-P-1",
            Pasillo = "QA", Rack = "1", Nivel = 1, Posicion = "1",
            EstaOcupada = true
        });
        foreach (var id in new[] { productA, productB })
        {
            catalog.Productos.Add(new Producto
            {
                Id = id, VendedorId = seller, CategoriaId = Guid.NewGuid(),
                Sku = "V2-" + id.ToString("N"),
                Nombre = "Checkout multi-SKU test", CategoriaNombre = "QA",
                PrecioB2C = 10m, PrecioB2B = 8m, Moneda = "USD",
                EstaPublicado = true, EstadoProducto = "Activo",
                StockInicial = 0, StockMinimo = 1,
                ProveedorNombre = "QA", PaisOrigen = "El Salvador",
                PesoKg = 1m, DimensionesCm = "1x1x1", UnidadMedida = "Unidad"
            });
            catalog.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = id,
                UbicacionAlmacenId = location,
                UbicacionCodigo = "V2-MULTISKU-P-1-R-1-N-1-P-1",
                CantidadTotal = secondWithoutStock && id == productB ? 0 : 1
            });
        }
        await catalog.SaveChangesAsync();
    }

    private TestServer CreateHost(
        IPaymentGateway gateway,
        ICommissionService commission,
        bool disabled)
        => new TestServer(new WebHostBuilder()
            .UseEnvironment("Development")
            .ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>()))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<FinanceDbContext>(o =>
                    o.UseNpgsql(_testConnection!,
                        x => x.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
                services.AddSingleton(new PaymentOperationsMode(disabled));
                services.AddScoped<IPaymentGateway>(_ => gateway);
                services.AddScoped<ICommissionService>(_ => commission);
                services.AddScoped<IContabilidadService>(sp =>
                    new ContabilidadService(
                        sp.GetRequiredService<FinanceDbContext>(),
                        NullLogger<ContabilidadService>.Instance));
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = Secret, Issuer = Issuer, Audience = Issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers()
                    .AddApplicationPart(typeof(CheckoutMultiSkuController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

    private static string Token(Guid buyer, string role)
    {
        var jwt = new JwtSecurityToken(
            issuer: Issuer, audience: Issuer,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, buyer.ToString()),
                new Claim(ClaimTypes.Role, role)
            },
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public async Task DisposeAsync()
    {
        if (!_created || _rootConnection is null ||
            !_database.StartsWith(Prefix, StringComparison.Ordinal))
            return;

        if (_testConnection is not null)
        {
            using var pool = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(pool);
        }

        await using var root = new NpgsqlConnection(_rootConnection);
        await root.OpenAsync();

        // Esta base fue creada exclusivamente por esta prueba y el nombre
        // conserva el prefijo temporal. Cerrar conexiones residuales del
        // TestServer/EF antes de eliminarla; nunca se toca una base compartida.
        await using (var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity " +
            "WHERE datname = @db AND pid <> pg_backend_pid()", root))
        {
            terminate.Parameters.AddWithValue("db", _database);
            await terminate.ExecuteNonQueryAsync();
        }

        await using (var count = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = @db", root))
        {
            count.Parameters.AddWithValue("db", _database);
            var active = Convert.ToInt32(await count.ExecuteScalarAsync());
            if (active != 0)
                throw new InvalidOperationException(
                    $"No se elimina base temporal con {active} sesiones activas.");
        }

        await using var drop = new NpgsqlCommand(
            "DROP DATABASE IF EXISTS \"" + _database + "\"", root);
        await drop.ExecuteNonQueryAsync();
        _created = false;
    }
}
