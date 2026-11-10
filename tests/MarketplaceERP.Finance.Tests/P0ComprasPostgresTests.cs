using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Shared.Settings;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// P0: recorrido real de Finance, Core y Catalog sobre un PostgreSQL efímero
/// independiente de Neon y de cualquier cuenta, proveedor o pago real.
/// </summary>
public class P0ComprasPostgresTests
{
    [Fact]
    public async Task Orden_RecepcionParcial_Reintentos_Factura_Match_Asiento()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"),
                "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("P0 requiere PostgreSQL local en CI.");
            return;
        }

        // Protección obligatoria: esta prueba CREA su propia base de datos.
        // Nunca permitir que una variable de conexión apunte por accidente
        // a Neon, Render o un PostgreSQL de producción.
        var admin = new NpgsqlConnectionStringBuilder(connection);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                admin.Host, StringComparer.OrdinalIgnoreCase) ||
            admin.Port != 5432 ||
            !string.Equals(admin.Database, "postgres", StringComparison.Ordinal) ||
            !string.Equals(admin.Username, "postgres", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "P0 solo puede crear bases efímeras en postgres@localhost:5432/postgres.");

        var database = "p0_flow_" + Guid.NewGuid().ToString("N");
        var testConnection = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = database
        }.ConnectionString;

        // La base efímera SIEMPRE debe eliminarse, incluso si fallan migraciones,
        // aserciones o peticiones HTTP. Los contextos/servidor declarados dentro
        // de este bloque se liberan antes del finally.
        try
        {
        await using (var root = new NpgsqlConnection(admin.ConnectionString))
        {
            await root.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", root);
            await command.ExecuteNonQueryAsync();
        }

        // Inyección opcional, exclusiva de QA local, para comprobar que el
        // finally elimina la base incluso ante una excepción temprana.
        if (Environment.GetEnvironmentVariable("P0_QA_FORCE_FAILURE_AFTER_CREATE") == "1")
            throw new InvalidOperationException("QA_TEARDOWN_FAILURE_INYECTADO");

        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(testConnection).Options;
        var catalogOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(testConnection).Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(testConnection).Options;

        await using var core = new CoreDbContext(coreOptions);
        await using var catalog = new CatalogDbContext(catalogOptions);
        await using var finance = new FinanceDbContext(financeOptions);
        // Cada módulo crea solo su esquema; no se necesita ni se altera Neon.
        await core.Database.ExecuteSqlRawAsync(core.Database.GenerateCreateScript());
        await catalog.Database.ExecuteSqlRawAsync(catalog.Database.GenerateCreateScript());
        await finance.Database.ExecuteSqlRawAsync(finance.Database.GenerateCreateScript());

        var proveedor = new Proveedor
        {
            Id = Guid.NewGuid(), NombreComercial = "Proveedor ficticio P0",
            RazonSocial = "Proveedor ficticio P0", Activo = true
        };
        core.Proveedores.Add(proveedor);
        await core.SaveChangesAsync();

        var ubicacion = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(), Codigo = "P0-REC-01", Pasillo = "A",
            Rack = "R1", Nivel = 1, Posicion = "P1", CapacidadMaxima = 100
        };
        catalog.UbicacionesAlmacen.Add(ubicacion);
        catalog.Productos.AddRange(
            new Producto
            {
                Id = Guid.NewGuid(), Sku = "P0-ARROZ", Nombre = "Arroz demo",
                CategoriaNombre = "Alimentos", PrecioB2C = 5m, PrecioB2B = 3.55m,
                StockMinimo = 5, EstadoProducto = "Activo"
            },
            new Producto
            {
                Id = Guid.NewGuid(), Sku = "P0-CAFE", Nombre = "Café demo",
                CategoriaNombre = "Alimentos", PrecioB2C = 14m, PrecioB2B = 10.06m,
                StockMinimo = 5, EstadoProducto = "Activo"
            });
        await catalog.SaveChangesAsync();

        // InventoryService es interno al módulo: crearlo por reflexión usando
        // su constructor público, sin añadir una API pública solo para tests.
        var impl = typeof(IInventoryService).Assembly.GetType(
            "MarketplaceERP.Catalog.Services.InventoryService", throwOnError: true)!;
        var container = new ServiceCollection();
        container.AddLogging();
        using var provider = container.BuildServiceProvider();
        var inventory = (IInventoryService)ActivatorUtilities.CreateInstance(provider, impl, catalog);

        var matching = new TresWayMatchingService(
            finance, NullLogger<TresWayMatchingService>.Instance);
        var ledger = new ContabilidadService(
            finance, NullLogger<ContabilidadService>.Instance);
        var controller = new ComprasController(
            finance, matching, ledger, inventory, core,
            NullLogger<ComprasController>.Instance);

        Assert.IsType<CreatedAtActionResult>(await controller.CrearPO(
            new CrearPORequest(proveedor.Id, "Proveedor ficticio P0", null,
                "Caso de prueba aislado", null, false,
                new List<ItemPORequest>
                {
                    new("P0-ARROZ", "Arroz demo", 24, 3.55m),
                    new("P0-CAFE", "Café demo", 12, 10.06m)
                }), CancellationToken.None));

        var po = await finance.OrdenesCompra.Include(x => x.Items).SingleAsync();
        Assert.Equal(205.92m, po.SubTotal);
        Assert.Equal(26.77m, po.ImpuestoIva);
        Assert.Equal(232.69m, po.MontoTotal);
        Assert.IsType<OkObjectResult>(await controller.EnviarPO(po.Id, CancellationToken.None));

        var arroz = po.Items.Single(i => i.Sku == "P0-ARROZ");
        var cafe = po.Items.Single(i => i.Sku == "P0-CAFE");
        var operacionArroz = Guid.NewGuid();
        var operacionCafe = Guid.NewGuid();

        // Primer ítem confirma 24; segundo falla por WMS inexistente.
        // Finance debe persistir únicamente el movimiento físico confirmado.
        var parcial = await controller.RecibirMercaderia(po.Id,
            new RecibirMercaderiaRequest(new List<RecibirMercaderiaItemRequest>
            {
                new(arroz.Id, 24, 24, ubicacion.Id, operacionArroz),
                new(cafe.Id, 12, 12, Guid.NewGuid(), operacionCafe)
            }, "Intento parcial"), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(parcial);

        await using (var check = new FinanceDbContext(financeOptions))
        {
            var saved = await check.OrdenesCompra.Include(x => x.Items).SingleAsync();
            Assert.Equal(EstadoOrdenCompra.RecibidaParcial, saved.Estado);
            Assert.Equal(24, saved.Items.Single(x => x.Sku == "P0-ARROZ").CantidadRecibida);
            Assert.Equal(0, saved.Items.Single(x => x.Sku == "P0-CAFE").CantidadRecibida);
        }
        Assert.Equal(24, await catalog.StockFisico.SumAsync(s => s.CantidadTotal));
        Assert.Single(await catalog.MovimientosInventario.ToListAsync());

        // Misma recepción de arroz + recepción pendiente de café.
        var requestValido = new RecibirMercaderiaRequest(new List<RecibirMercaderiaItemRequest>
        {
            new(arroz.Id, 24, 24, ubicacion.Id, operacionArroz),
            new(cafe.Id, 12, 12, ubicacion.Id, operacionCafe)
        }, "Recuperación idempotente");
        Assert.IsType<OkObjectResult>(await controller.RecibirMercaderia(
            po.Id, requestValido, CancellationToken.None));

        Assert.Equal(36, await catalog.StockFisico.SumAsync(s => s.CantidadTotal));
        Assert.Equal(2, await catalog.MovimientosInventario.CountAsync(
            m => m.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound));

        // El replay exacto no genera stock; uno con nuevo token se rechaza.
        Assert.IsType<OkObjectResult>(await controller.RecibirMercaderia(
            po.Id, requestValido, CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.RecibirMercaderia(
            po.Id, new RecibirMercaderiaRequest(new()
            {
                new(arroz.Id, 24, 24, ubicacion.Id, Guid.NewGuid())
            }, "Token inválido"), CancellationToken.None));

        // Documento sintético; no se ejecuta pago ni se llama a un proveedor.
        Assert.IsType<CreatedAtActionResult>(await controller.RegistrarFactura(
            new RegistrarFacturaRequest(po.Id, "FAC-P0-DEMO", 205.92m, 232.69m,
                null, null, new()
                {
                    new(arroz.Id, 24, 3.55m),
                    new(cafe.Id, 12, 10.06m)
                }), CancellationToken.None));
        var factura = await finance.FacturasProveedor.SingleAsync();
        Assert.False(factura.Pagada);
        Assert.IsType<ConflictObjectResult>(await controller.RegistrarFactura(
            new RegistrarFacturaRequest(po.Id, "FAC-P0-DEMO", 205.92m, 232.69m,
                null, null, new()
                {
                    new(arroz.Id, 24, 3.55m),
                    new(cafe.Id, 12, 10.06m)
                }), CancellationToken.None));
        Assert.Equal(1, await finance.FacturasProveedor.CountAsync());

        var match = await matching.EjecutarMatchAsync(
            new Validar3WayMatchRequest(po.Id, factura.Id, 36));
        Assert.Equal(Estado3WayMatch.Conforme, match.Resultado);
        Assert.True(match.AutorizadoParaPago); // banderín contable, NO pago ejecutado
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            matching.EjecutarMatchAsync(
                new Validar3WayMatchRequest(po.Id, factura.Id, 36)));
        Assert.Single(await finance.MatchesTridireccionales
            .Where(m => m.OrdenCompraId == po.Id && m.AutorizadoParaPago).ToListAsync());

        // Asiento de compra simulado sobre la misma base efímera.
        var asiento = await ledger.GenerarAsientoCompraAsync(
            po.Id, factura.MontoSubTotal, factura.MontoIva);
        Assert.Equal(232.69m, asiento.TotalDebe);
        Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        Assert.Equal(205.92m, asiento.Lineas.Single(x => x.CodigoCuenta == "1108").Debe);
        Assert.Equal(26.77m, asiento.Lineas.Single(x => x.CodigoCuenta == "1110").Debe);
        Assert.Equal(232.69m, asiento.Lineas.Single(x => x.CodigoCuenta == "2101").Haber);

        await using var verify = new FinanceDbContext(financeOptions);
        var partidas = await verify.PartidasContables.Include(x => x.Lineas).ToListAsync();
        Assert.Single(partidas);
        Assert.All(partidas, p =>
        {
            Assert.Equal(p.TotalDebe, p.Lineas.Sum(x => x.Debe));
            Assert.Equal(p.TotalHaber, p.Lineas.Sum(x => x.Haber));
            Assert.Equal(p.TotalDebe, p.TotalHaber);
        });
        Assert.Equal(36, (await verify.OrdenesCompra.Include(p => p.Items)
            .SingleAsync()).Items.Sum(i => i.CantidadRecibida));
        Assert.Equal(2, await catalog.MovimientosInventario.CountAsync(
            m => m.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound));
        Assert.False((await verify.FacturasProveedor.SingleAsync()).Pagada);

        // Dos peticiones separadas con distinto OperacionId compiten por una
        // nueva PO. Solo una puede registrar 10 unidades; la otra debe
        // recibir 409 incluso si llega inmediatamente después del commit.
        Assert.IsType<CreatedAtActionResult>(await controller.CrearPO(
            new CrearPORequest(proveedor.Id, "Proveedor ficticio P0", null,
                "Concurrencia de prueba", null, false,
                new List<ItemPORequest> { new("P0-ARROZ", "Arroz demo", 10, 3.55m) }),
            CancellationToken.None));
        var poConcurrencia = await finance.OrdenesCompra.Include(x => x.Items)
            .SingleAsync(x => x.Id != po.Id);
        Assert.IsType<OkObjectResult>(await controller.EnviarPO(
            poConcurrencia.Id, CancellationToken.None));
        var itemConcurrente = poConcurrencia.Items.Single();

        async Task<IActionResult> IntentoSimultaneoAsync(Guid nuevaOperacion)
        {
            await using var f = new FinanceDbContext(financeOptions);
            await using var c = new CatalogDbContext(catalogOptions);
            await using var k = new CoreDbContext(coreOptions);
            var inv = (IInventoryService)ActivatorUtilities.CreateInstance(
                provider, impl, c);
            var ctrl = new ComprasController(f,
                new TresWayMatchingService(f, NullLogger<TresWayMatchingService>.Instance),
                new ContabilidadService(f, NullLogger<ContabilidadService>.Instance),
                inv, k, NullLogger<ComprasController>.Instance);
            return await ctrl.RecibirMercaderia(poConcurrencia.Id,
                new RecibirMercaderiaRequest(new()
                {
                    new(itemConcurrente.Id, 10, 10, ubicacion.Id, nuevaOperacion)
                }, "Carrera de recepción"), CancellationToken.None);
        }

        var carreras = await Task.WhenAll(
            IntentoSimultaneoAsync(Guid.NewGuid()),
            IntentoSimultaneoAsync(Guid.NewGuid()));
        Assert.Single(carreras.OfType<OkObjectResult>());
        Assert.Single(carreras.OfType<ConflictObjectResult>());

        await using var finalFinance = new FinanceDbContext(financeOptions);
        await using var finalCatalog = new CatalogDbContext(catalogOptions);
        Assert.Equal(10, (await finalFinance.OrdenesCompra
            .Include(x => x.Items).SingleAsync(x => x.Id == poConcurrencia.Id))
            .Items.Sum(x => x.CantidadRecibida));
        Assert.Equal(46, await finalCatalog.StockFisico.SumAsync(x => x.CantidadTotal));
        Assert.Equal(3, await finalCatalog.MovimientosInventario.CountAsync(
            x => x.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound));

        // Misma base efímera, pero ahora con pipeline HTTP, middleware JWT,
        // binding de JSON, DI por request y controladores reales.
        const string issuer = "p0-http-pg-ci";
        const string secret = "P0-HTTP-POSTGRES-TEST-KEY-NEVER-DEPLOY-TO-PRODUCTION";
        var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<CoreDbContext>(o => o.UseNpgsql(testConnection));
                services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(testConnection));
                services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(testConnection));
                services.AddScoped<IInventoryService>(sp =>
                    (IInventoryService)ActivatorUtilities.CreateInstance(
                        sp, impl, sp.GetRequiredService<CatalogDbContext>()));
                services.AddScoped<ITresWayMatchingService, TresWayMatchingService>();
                services.AddScoped<IContabilidadService, ContabilidadService>();
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = secret,
                    Issuer = issuer,
                    Audience = issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers()
                    .AddApplicationPart(typeof(ComprasController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

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

        var crearHttp = await http.PostAsJsonAsync("/api/compras/po",
            new CrearPORequest(proveedor.Id, proveedor.NombreComercial,
                null, "Prueba P0 HTTP+PostgreSQL", null, false,
                new()
                {
                    new("P0-ARROZ", "Arroz demo", 24, 3.55m),
                    new("P0-CAFE", "Café demo", 12, 10.06m)
                }));
        Assert.Equal(HttpStatusCode.Created, crearHttp.StatusCode);
        await using var readHttp = new FinanceDbContext(financeOptions);
        var ordenHttp = await readHttp.OrdenesCompra.Include(x => x.Items)
            .SingleAsync(x => x.Id != po.Id && x.Id != poConcurrencia.Id);

        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync(
            $"/api/compras/po/{ordenHttp.Id}/enviar", null)).StatusCode);
        var arrozHttp = ordenHttp.Items.Single(x => x.Sku == "P0-ARROZ");
        var cafeHttp = ordenHttp.Items.Single(x => x.Sku == "P0-CAFE");
        var recibirHttp = new RecibirMercaderiaRequest(new()
        {
            new(arrozHttp.Id, 24, 24, ubicacion.Id, Guid.NewGuid()),
            new(cafeHttp.Id, 12, 12, ubicacion.Id, Guid.NewGuid())
        }, "Recepción HTTP");
        Assert.Equal(HttpStatusCode.OK, (await http.PutAsJsonAsync(
            $"/api/compras/po/{ordenHttp.Id}/recibir", recibirHttp)).StatusCode);
        Assert.Equal(82, await finalCatalog.StockFisico.SumAsync(x => x.CantidadTotal));

        var facturaHttpResponse = await http.PostAsJsonAsync(
            "/api/compras/facturas",
            new RegistrarFacturaRequest(
                ordenHttp.Id, "FAC-P0-HTTP", 205.92m, 232.69m, null, null,
                new()
                {
                    new(arrozHttp.Id, 24, 3.55m),
                    new(cafeHttp.Id, 12, 10.06m)
                }));
        Assert.Equal(HttpStatusCode.Created, facturaHttpResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync(
            "/api/compras/facturas",
            new RegistrarFacturaRequest(
                ordenHttp.Id, "FAC-P0-HTTP", 205.92m, 232.69m, null, null,
                new()
                {
                    new(arrozHttp.Id, 24, 3.55m),
                    new(cafeHttp.Id, 12, 10.06m)
                }))).StatusCode);

        // El endpoint /match está deliberadamente protegido por un guard
        // financiero. Se concilia aquí mediante el servicio real SIN pagar.
        var docHttp = await finance.FacturasProveedor.SingleAsync(
            f => f.OrdenCompraId == ordenHttp.Id);
        var matchHttp = await matching.EjecutarMatchAsync(
            new Validar3WayMatchRequest(ordenHttp.Id, docHttp.Id, 36));
        Assert.True(matchHttp.AutorizadoParaPago);
        Assert.Equal(Estado3WayMatch.Conforme, matchHttp.Resultado);
        var asientoHttp = await ledger.GenerarAsientoCompraAsync(
            ordenHttp.Id, docHttp.MontoSubTotal, docHttp.MontoIva);
        Assert.Equal(asientoHttp.TotalDebe, asientoHttp.TotalHaber);
        Assert.Equal(232.69m, asientoHttp.TotalDebe);
        Assert.False((await finance.FacturasProveedor.SingleAsync(
            f => f.Id == docHttp.Id)).Pagada);
        Assert.Equal(5, await finalCatalog.MovimientosInventario.CountAsync(
            x => x.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound));

        // P0 transaccional: una excepción después de autorizar 3-Way,
        // antes de generar la partida, NO debe dejar aprobado el pago.
        // Se reutiliza la segunda PO recibida, que aún no tiene factura.
        Assert.IsType<CreatedAtActionResult>(await controller.RegistrarFactura(
            new RegistrarFacturaRequest(
                poConcurrencia.Id, "FAC-P0-ROLLBACK", 35.50m, 40.12m,
                null, null, new()
                {
                    new(itemConcurrente.Id, 10, 3.55m)
                }), CancellationToken.None));
        var facturaRollback = await finance.FacturasProveedor
            .SingleAsync(f => f.OrdenCompraId == poConcurrencia.Id);

        var fallaContable = new Mock<IContabilidadService>();
        fallaContable.Setup(x => x.GenerarAsientoCompraAsync(
                poConcurrencia.Id, 35.50m, 4.62m,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fallo contable simulado"));

        await using (var falloDb = new FinanceDbContext(financeOptions))
        {
            var ctrlFallo = new ComprasController(
                falloDb,
                new TresWayMatchingService(
                    falloDb, NullLogger<TresWayMatchingService>.Instance),
                fallaContable.Object, inventory, core,
                NullLogger<ComprasController>.Instance);
            Assert.IsType<BadRequestObjectResult>(await ctrlFallo.EjecutarMatch(
                new Validar3WayMatchRequest(
                    poConcurrencia.Id, facturaRollback.Id, 10),
                CancellationToken.None));
        }
        fallaContable.Verify(x => x.GenerarAsientoCompraAsync(
            poConcurrencia.Id, 35.50m, 4.62m,
            It.IsAny<CancellationToken>()), Times.Once);

        await using (var rollbackCheck = new FinanceDbContext(financeOptions))
        {
            Assert.False((await rollbackCheck.FacturasProveedor.SingleAsync(
                x => x.Id == facturaRollback.Id)).AprobadaParaPago);
            Assert.Empty(await rollbackCheck.MatchesTridireccionales
                .Where(m => m.OrdenCompraId == poConcurrencia.Id).ToListAsync());
            Assert.Empty(await rollbackCheck.PartidasContables
                .Where(p => p.TipoOperacion == "Compra" &&
                            p.ReferenciaOrigenId == poConcurrencia.Id)
                .ToListAsync());
        }

        // Un reintento con el servicio contable real sí debe confirmar
        // exactamente una autorización y una partida balanceada.
        await using (var retryDb = new FinanceDbContext(financeOptions))
        {
            var retryController = new ComprasController(retryDb,
                new TresWayMatchingService(
                    retryDb, NullLogger<TresWayMatchingService>.Instance),
                new ContabilidadService(
                    retryDb, NullLogger<ContabilidadService>.Instance),
                inventory, core, NullLogger<ComprasController>.Instance);
            Assert.IsType<OkObjectResult>(await retryController.EjecutarMatch(
                new Validar3WayMatchRequest(
                    poConcurrencia.Id, facturaRollback.Id, 10),
                CancellationToken.None));
        }

        await using var afterRetry = new FinanceDbContext(financeOptions);
        Assert.Single(await afterRetry.MatchesTridireccionales
            .Where(m => m.OrdenCompraId == poConcurrencia.Id &&
                        m.AutorizadoParaPago).ToListAsync());
        var asientoRecuperado = await afterRetry.PartidasContables
            .Include(p => p.Lineas)
            .SingleAsync(p => p.TipoOperacion == "Compra" &&
                             p.ReferenciaOrigenId == poConcurrencia.Id);
        Assert.Equal(asientoRecuperado.TotalDebe, asientoRecuperado.TotalHaber);
        Assert.Equal(asientoRecuperado.TotalDebe,
            asientoRecuperado.Lineas.Sum(x => x.Debe));
        Assert.Equal(asientoRecuperado.TotalHaber,
            asientoRecuperado.Lineas.Sum(x => x.Haber));
        Assert.False((await afterRetry.FacturasProveedor.SingleAsync(
            x => x.Id == facturaRollback.Id)).Pagada);

        // P0 HTTP+Postgres: dos peticiones JWT simultáneas sobre una PO nueva.
        // El segundo token nunca debe crear movimiento extra en WMS.
        var crearCarreraHttp = await http.PostAsJsonAsync("/api/compras/po",
            new CrearPORequest(proveedor.Id, proveedor.NombreComercial,
                null, "P0 HTTP carrera", null, false, new()
                {
                    new("P0-ARROZ", "Arroz demo", 5, 3.55m)
                }));
        Assert.Equal(HttpStatusCode.Created, crearCarreraHttp.StatusCode);
        await using var raceRead = new FinanceDbContext(financeOptions);
        var poRace = await raceRead.OrdenesCompra.Include(x => x.Items)
            .SingleAsync(x => x.Id != po.Id &&
                              x.Id != poConcurrencia.Id &&
                              x.Id != ordenHttp.Id);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync(
            $"/api/compras/po/{poRace.Id}/enviar", null)).StatusCode);
        var lineaRace = poRace.Items.Single();
        Task<HttpResponseMessage> RequestRaceAsync(Guid operationId) =>
            http.PutAsJsonAsync(
                $"/api/compras/po/{poRace.Id}/recibir",
                new RecibirMercaderiaRequest(new()
                {
                    new(lineaRace.Id, 5, 5, ubicacion.Id, operationId)
                }, "HTTP concurrency test"));

        // Lanzar ambas peticiones antes de esperarlas: una debe registrar
        // las unidades y la otra devolver conflicto, nunca dos éxitos.
        var pendientes = await Task.WhenAll(
            RequestRaceAsync(Guid.NewGuid()), RequestRaceAsync(Guid.NewGuid()));
        try
        {
            Assert.Single(pendientes, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(pendientes, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in pendientes) response.Dispose();
        }

        await using var raceVerify = new FinanceDbContext(financeOptions);
        Assert.Equal(5, (await raceVerify.OrdenesCompra
            .Include(x => x.Items).SingleAsync(x => x.Id == poRace.Id))
            .Items.Sum(x => x.CantidadRecibida));
        await using var raceCatalog = new CatalogDbContext(catalogOptions);
        Assert.Equal(87, await raceCatalog.StockFisico.SumAsync(x => x.CantidadTotal));
        Assert.Equal(6, await raceCatalog.MovimientosInventario.CountAsync(
            x => x.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound));
        }
        finally
        {
            // La conexión administrativa es exclusivamente a postgres local.
            // No se terminan sesiones ni se utiliza DROP ... WITH (FORCE).
            if (!database.StartsWith("p0_flow_", StringComparison.Ordinal) ||
                database.Length != "p0_flow_".Length + 32)
                throw new InvalidOperationException("Nombre de BD temporal inválido.");

            // Npgsql conserva conexiones inactivas en su pool aun después de
            // Dispose de los DbContext. Vaciar SOLO el pool de esta BD QA;
            // nunca cerrar sesiones arbitrarias de otras bases.
            await using (var pooled = new NpgsqlConnection(testConnection))
                NpgsqlConnection.ClearPool(pooled);

            await using var cleanup = new NpgsqlConnection(admin.ConnectionString);
            await cleanup.OpenAsync();
            await using (var sesiones = new NpgsqlCommand(
                "SELECT COUNT(*) FROM pg_stat_activity WHERE datname = @db", cleanup))
            {
                sesiones.Parameters.AddWithValue("db", database);
                var activas = Convert.ToInt32(await sesiones.ExecuteScalarAsync());
                if (activas != 0)
                    throw new InvalidOperationException(
                        $"No se elimina {database}: tiene {activas} conexiones activas.");
            }

            await using var drop = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{database}\"", cleanup);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
