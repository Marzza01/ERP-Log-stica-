using MarketplaceERP.API.Controllers;
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
            return; // CI exige esta variable y PostgreSQL disponible.

        var database = "p0_flow_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(connection);
        var testConnection = new NpgsqlConnectionStringBuilder(connection) { Database = database }.ConnectionString;

        await using (var root = new NpgsqlConnection(admin.ConnectionString))
        {
            await root.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", root);
            await command.ExecuteNonQueryAsync();
        }

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

        var match = await matching.EjecutarMatchAsync(
            new Validar3WayMatchRequest(po.Id, factura.Id, 36));
        Assert.Equal(Estado3WayMatch.Conforme, match.Resultado);
        Assert.True(match.AutorizadoParaPago); // banderín contable, NO pago ejecutado

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
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(o =>
                    {
                        o.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true, ValidIssuer = issuer,
                            ValidateAudience = true, ValidAudience = issuer,
                            ValidateIssuerSigningKey = true, IssuerSigningKey = jwtKey,
                            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
                        };
                    });
                services.AddAuthorization();
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
    }
}
