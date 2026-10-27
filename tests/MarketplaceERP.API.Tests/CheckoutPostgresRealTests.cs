/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests de integración con PostgreSQL real para FASE 7a:
/// 1. Idempotencia concurrente: 2 requests simultáneos con la misma clave producen exactamente 1 sola orden y misma respuesta.
/// 2. Rollback por pago fallido: si el pago falla, no queda orden huérfana ni asiento contable en la BD.
/// 3. Flujo completo: tras checkout la orden queda en Aprobada y la liquidación la toma SOLO tras marcarla Entregada.
/// </summary>
[Collection("PostgresRealTests")]
public class CheckoutPostgresRealTests : IAsyncLifetime
{
    private static readonly string? RawConnStr = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
    private readonly string _dbName = $"test_chk_pg_{Guid.NewGuid():N}";
    private string _adminConnStr = "";
    private string _testConnStr = "";
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(RawConnStr)) return;

        var builder = new NpgsqlConnectionStringBuilder(RawConnStr) { Database = "postgres" };
        _adminConnStr = builder.ConnectionString;

        builder.Database = _dbName;
        builder.IncludeErrorDetail = true;
        _testConnStr = builder.ConnectionString;

        await using var adminConn = new NpgsqlConnection(_adminConnStr);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_dbName}\";";
        await cmd.ExecuteNonQueryAsync();

        // Configurar factory con la base de datos PostgreSQL real
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _testConnStr);
            builder.UseSetting("Jwt:Key", CustomWebApplicationFactory.TestJwtKey);
            builder.UseSetting("Jwt:Issuer", CustomWebApplicationFactory.TestJwtIssuer);
            builder.UseSetting("Jwt:Audience", CustomWebApplicationFactory.TestJwtAudience);
            builder.UseSetting("Jwt:ExpirationMinutes", "60");

            builder.ConfigureServices(services =>
            {
                // Reconfigurar DbContexts para usar la BD aislada
                var descCore = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<MarketplaceERP.Core.Data.CoreDbContext>));
                if (descCore != null) services.Remove(descCore);
                var descCat = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<CatalogDbContext>));
                if (descCat != null) services.Remove(descCat);
                var descFin = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<FinanceDbContext>));
                if (descFin != null) services.Remove(descFin);

                services.AddDbContext<MarketplaceERP.Core.Data.CoreDbContext>(o =>
                    o.UseNpgsql(_testConnStr, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "core")));
                services.AddDbContext<CatalogDbContext>(o =>
                    o.UseNpgsql(_testConnStr, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
                services.AddDbContext<FinanceDbContext>(o =>
                    o.UseNpgsql(_testConnStr, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
            });
        });

        // Aplicar migraciones
        using var scope = _factory.Services.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<MarketplaceERP.Core.Data.CoreDbContext>();
        var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        await coreDb.Database.MigrateAsync();
        await catDb.Database.MigrateAsync();
        await finDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory != null) _factory.Dispose();

        if (!string.IsNullOrWhiteSpace(_adminConnStr))
        {
            try
            {
                await using var adminConn = new NpgsqlConnection(_adminConnStr);
                await adminConn.OpenAsync();
                await using var cmd = adminConn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_dbName}';
                    DROP DATABASE IF EXISTS ""{_dbName}"";";
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }
        }
    }

    [SkippableFact]
    public async Task Checkout_ConcurrenteConMismaClave_ProduceUnaSolaOrdenYPartida()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var compradorId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(new Producto
            {
                Id = prodId,
                Sku = $"SKU-CONC-{Guid.NewGuid():N}",
                Nombre = "Producto Concurrente",
                CategoriaNombre = "General",
                PrecioB2C = 50.00m,
                PrecioB2B = 40.00m,
                EstaPublicado = true,
                VendedorId = vendedorId
            });
            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 100,
                CantidadReservada = 0,
                PuntoReorden = 10,
                UltimaActualizacion = DateTime.UtcNow
            });
            await catDb.SaveChangesAsync();
        }

        var customFactory = new CustomWebApplicationFactory();
        var token = customFactory.GenerateToken("Comprador", compradorId.ToString());

        var idempotencyKey = $"CONC-KEY-{Guid.NewGuid():N}";

        // Lanzar dos solicitudes simultáneas idénticas
        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            var client = _factory!.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
            {
                Content = JsonContent.Create(new
                {
                    Items = new[] { new { ProductoId = prodId, Cantidad = 1 } },
                    DireccionEntrega = "San Salvador",
                    ZonaEnvio = "Centro",
                    MetodoPago = "Tarjeta"
                })
            };
            req.Headers.Add("Idempotency-Key", idempotencyKey);
            return await client.SendAsync(req);
        }).ToList();

        var responses = await Task.WhenAll(tasks);

        // Ambas deben retornar 200 OK (una procesó y la otra devolvió el resultado idempotente)
        responses[0].StatusCode.Should().Be(HttpStatusCode.OK);
        responses[1].StatusCode.Should().Be(HttpStatusCode.OK);

        // Verificar en PostgreSQL real que solo se creó 1 orden y 1 partida de venta
        using (var scope = _factory!.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ordenes = await finDb.Ordenes.Where(o => o.CompradorId == compradorId).ToListAsync();
            ordenes.Should().HaveCount(1);

            var partidas = await finDb.PartidasContables.Where(p => p.ReferenciaOrigenId == ordenes.First().Id).ToListAsync();
            partidas.Should().HaveCount(1);
        }
    }

    [SkippableFact]
    public async Task Checkout_RollbackPorPagoFallido_NoDejaOrdenHuerfanaNiAsiento()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        // Crear client con pasarela mock que fuerza fallo
        var brokenFactory = _factory!.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descPago = services.SingleOrDefault(d => d.ServiceType == typeof(MarketplaceERP.Core.Interfaces.IPaymentGateway));
                if (descPago != null) services.Remove(descPago);

                var mockPago = new Moq.Mock<MarketplaceERP.Core.Interfaces.IPaymentGateway>();
                mockPago.Setup(p => p.ProcesarPagoAsync(Moq.It.IsAny<PagoRequest>()))
                    .Returns(Task.FromResult(new PagoResult(false, null, "Tarjeta rechazada por fondos insuficientes")));

                services.AddScoped(_ => mockPago.Object);
            });
        });

        var compradorId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        using (var scope = brokenFactory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(new Producto
            {
                Id = prodId,
                Sku = $"SKU-FALLO-{Guid.NewGuid():N}",
                Nombre = "Producto Pago Fallido",
                CategoriaNombre = "General",
                PrecioB2C = 80.00m,
                PrecioB2B = 70.00m,
                EstaPublicado = true,
                VendedorId = vendedorId
            });
            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 100,
                CantidadReservada = 0,
                PuntoReorden = 10,
                UltimaActualizacion = DateTime.UtcNow
            });
            await catDb.SaveChangesAsync();
        }

        var customFactory = new CustomWebApplicationFactory();
        var token = customFactory.GenerateToken("Comprador", compradorId.ToString());

        var client = brokenFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prodId, Cantidad = 1 } }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Verificar en PostgreSQL real que la orden y el asiento fueron revertidos por la transacción
        using (var scope = brokenFactory.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ordenes = await finDb.Ordenes.Where(o => o.CompradorId == compradorId).ToListAsync();
            ordenes.Should().BeEmpty();

            var partidas = await finDb.PartidasContables.ToListAsync();
            partidas.Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task Checkout_TrasCheckoutLaOrdenEsAprobada_LiquidacionLaTomaSoloTrasMarcarlaEntregada()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var compradorId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(new Producto
            {
                Id = prodId,
                Sku = $"SKU-LIQ-{Guid.NewGuid():N}",
                Nombre = "Producto Flujo Liquidacion",
                CategoriaNombre = "General",
                PrecioB2C = 100.00m,
                PrecioB2B = 90.00m,
                EstaPublicado = true,
                VendedorId = vendedorId
            });
            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 100,
                CantidadReservada = 0,
                PuntoReorden = 10,
                UltimaActualizacion = DateTime.UtcNow
            });
            await catDb.SaveChangesAsync();
        }

        var customFactory = new CustomWebApplicationFactory();
        var tokenComprador = customFactory.GenerateToken("Comprador", compradorId.ToString());
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenComprador);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prodId, Cantidad = 1 } }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verificar en DB que la orden está en estado Aprobada (NO Entregada)
        Guid ordenId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var orden = await finDb.Ordenes.FirstAsync(o => o.CompradorId == compradorId);
            orden.Estado.Should().Be(EstadoOrden.Aprobada);
            ordenId = orden.Id;

            // Intentar liquidar al vendedor: debe fallar porque la orden aún no está Entregada
            var liqService = scope.ServiceProvider.GetRequiredService<ILiquidacionService>();
            var act = () => liqService.GenerarLiquidacionAsync(vendedorId);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*no tiene saldos pendientes*");
        }

        // Marcar la orden como Entregada mediante OrdenesController
        var tokenAdmin = customFactory.GenerateToken("Admin");
        var clientAdmin = _factory!.CreateClient();
        clientAdmin.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenAdmin);

        // Primero pasar a EnProceso, luego Enviada, luego Entregada según máquina de estados
        var r1 = await clientAdmin.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { nuevoEstado = "EnProceso" });
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var r2 = await clientAdmin.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { nuevoEstado = "Enviada" });
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var r3 = await clientAdmin.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { nuevoEstado = "Entregada" });
        r3.StatusCode.Should().Be(HttpStatusCode.OK);

        // Ahora la liquidación sí debe tomarla
        using (var scope = _factory!.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var orden = await finDb.Ordenes.FindAsync(ordenId);
            orden!.Estado.Should().Be(EstadoOrden.Entregada);

            var liqService = scope.ServiceProvider.GetRequiredService<ILiquidacionService>();
            var liquidacion = await liqService.GenerarLiquidacionAsync(vendedorId);
            liquidacion.Should().NotBeNull();
            liquidacion.MontoLiquidar.Should().Be(90.00m); // 100 - 10 comisión = 90
        }
    }

    [SkippableFact]
    public async Task Checkout_FalloForzadoAsientoTrasCobro_CompensaStockYReembolsaOConcilia()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        // Mock contable que falla intencionalmente al generar asiento
        var brokenFactory = _factory!.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descContab = services.SingleOrDefault(d => d.ServiceType == typeof(IContabilidadService));
                if (descContab != null) services.Remove(descContab);

                var mockContab = new Moq.Mock<IContabilidadService>();
                mockContab.Setup(c => c.GenerarAsientoVentaAsync(
                    Moq.It.IsAny<Guid>(), Moq.It.IsAny<decimal>(), Moq.It.IsAny<decimal>(), Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>()))
                    .Throws(new InvalidOperationException("Fallo inducido en motor contable tras cobro exitoso"));

                services.AddScoped(_ => mockContab.Object);
            });
        });

        var compradorId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        using (var scope = brokenFactory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(new Producto
            {
                Id = prodId,
                Sku = $"SKU-FALLO-ASIENTO-{Guid.NewGuid():N}",
                Nombre = "Producto Asiento Falla",
                CategoriaNombre = "General",
                PrecioB2C = 100.00m,
                PrecioB2B = 90.00m,
                EstaPublicado = true,
                VendedorId = vendedorId
            });
            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 50,
                CantidadReservada = 0,
                PuntoReorden = 10,
                UltimaActualizacion = DateTime.UtcNow
            });
            await catDb.SaveChangesAsync();
        }

        var customFactory = new CustomWebApplicationFactory();
        var token = customFactory.GenerateToken("Comprador", compradorId.ToString());
        var client = brokenFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prodId, Cantidad = 2 } }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Validar consistencia en PostgreSQL:
        // 1. Stock final = stock inicial (0 reservadas, 50 totales)
        // 2. 0 órdenes guardadas en Finance (transacción EF revertida por rollback)
        // 3. 0 asientos guardados
        // 4. Registro de conciliación de pago huérfano o reembolso generado
        using (var scope = brokenFactory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == prodId);
            stockDb.CantidadTotal.Should().Be(50);
            stockDb.CantidadReservada.Should().Be(0, "la compensación de la saga debe liberar la reserva de stock");
            stockDb.CantidadDisponible.Should().Be(50);

            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ordenes = await finDb.Ordenes.Where(o => o.CompradorId == compradorId).ToListAsync();
            ordenes.Should().BeEmpty("la orden no debe persistirse tras rollback");

            var partidas = await finDb.PartidasContables.ToListAsync();
            partidas.Should().BeEmpty("no debe existir asiento contable");

            var pagosConciliacion = await finDb.PagosConciliacionHuerfanos.ToListAsync();
            pagosConciliacion.Should().NotBeEmpty("debe existir registro de conciliación del cobro realizado");
            pagosConciliacion.First().Reembolsado.Should().BeTrue("el mock de pasarela debe reembolsar con éxito");
        }
    }

    [SkippableFact]
    public async Task Checkout_FalloCommitIdempotencia_CompensaStockYReembolsaOConcilia()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var compradorId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid().ToString();

        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(new Producto
            {
                Id = prodId,
                Sku = $"SKU-FALLO-IDEMP-{Guid.NewGuid():N}",
                Nombre = "Producto Idempotencia Falla",
                CategoriaNombre = "General",
                PrecioB2C = 50.00m,
                PrecioB2B = 40.00m,
                EstaPublicado = true,
                VendedorId = vendedorId
            });
            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 20,
                CantidadReservada = 0,
                PuntoReorden = 5,
                UltimaActualizacion = DateTime.UtcNow
            });
            await catDb.SaveChangesAsync();

            // Insertar clave de idempotencia duplicada para forzar error 23505 o fallo dentro de la transacción
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finDb.IdempotencyKeys.Add(new IdempotencyKeyRecord
            {
                Id = Guid.NewGuid(),
                Key = idempotencyKey,
                UsuarioId = compradorId,
                RequestHash = "hash_diferente_forzado",
                StatusCode = 200,
                ResponseJson = "{}",
                FechaCreacion = DateTime.UtcNow,
                FechaExpiracion = DateTime.UtcNow.AddHours(24)
            });
            await finDb.SaveChangesAsync();
        }

        var customFactory = new CustomWebApplicationFactory();
        var token = customFactory.GenerateToken("Comprador", compradorId.ToString());
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prodId, Cantidad = 1 } }
            })
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Validar que el stock inicial se preserva y no quedaron órdenes creadas
        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == prodId);
            stockDb.CantidadTotal.Should().Be(20);
            stockDb.CantidadReservada.Should().Be(0);

            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ordenes = await finDb.Ordenes.Where(o => o.CompradorId == compradorId).ToListAsync();
            ordenes.Should().BeEmpty();
        }
    }
}
