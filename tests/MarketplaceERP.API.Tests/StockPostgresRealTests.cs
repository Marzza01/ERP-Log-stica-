/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Shared.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests de integración con PostgreSQL real para FASE 7b (Stock real y reservas atómicas):
/// (a) stock=1, 2 concurrent checkouts → exactamente 1 OK + 1 conflict 409, final stock=0
/// (b) rejected payment → stock returns to initial value (compensación de la Saga)
/// (c) order cancellation → stock released
/// (d) out-of-stock product → 409
/// (e) expired reservation → released by cleanup job
/// (f) 20 concurrent checkouts for stock of 5 → stock never negative, exactly 5 OK
/// </summary>
[Collection("PostgresRealTests")]
public class StockPostgresRealTests : IAsyncLifetime
{
    private static readonly string? RawConnStr = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
    private readonly string _dbName = $"test_stock_pg_{Guid.NewGuid():N}";
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

        // Configurar factory con la base de datos PostgreSQL real y StockService real
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _testConnStr);
            builder.UseSetting("Jwt:Key", CustomWebApplicationFactory.TestJwtKey);
            builder.UseSetting("Jwt:Issuer", CustomWebApplicationFactory.TestJwtIssuer);
            builder.UseSetting("Jwt:Audience", CustomWebApplicationFactory.TestJwtAudience);
            builder.UseSetting("Jwt:ExpirationMinutes", "60");
            builder.UseSetting("Payments:AllowMock", "true");
            builder.UseSetting("Payments:SimulateFailures", "false");
            builder.UseSetting("Stock:EnableCleanupJob", "false"); // Control manual en tests

            builder.ConfigureServices(services =>
            {
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

    private async Task<(Guid ProductoId, Guid VendedorId)> CrearProductoConStockAsync(int cantidadTotal)
    {
        using var scope = _factory!.Services.CreateScope();
        var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var vendedorId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        var prod = new Producto
        {
            Id = prodId,
            Sku = $"SKU-STK-{Guid.NewGuid():N}",
            Nombre = $"Producto Test Stock {prodId}",
            CategoriaNombre = "General",
            PrecioB2C = 20.00m,
            PrecioB2B = 15.00m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };

        var stock = new StockFisico
        {
            Id = Guid.NewGuid(),
            ProductoId = prodId,
            CantidadTotal = cantidadTotal,
            CantidadReservada = 0,
            PuntoReorden = 5,
            UltimaActualizacion = DateTime.UtcNow
        };

        catDb.Productos.Add(prod);
        catDb.StockFisico.Add(stock);
        await catDb.SaveChangesAsync();

        return (prodId, vendedorId);
    }

    [SkippableFact]
    public async Task TestA_Stock1_DosCheckoutsConcurrentes_ExactamenteUnoOK_Y_Uno409_StockFinalCero()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 1);

        var customFactory = new CustomWebApplicationFactory();
        var client1 = _factory!.CreateClient();
        var client2 = _factory!.CreateClient();

        var token1 = customFactory.GenerateToken("Comprador", Guid.NewGuid().ToString());
        var token2 = customFactory.GenerateToken("Comprador", Guid.NewGuid().ToString());

        client1.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token1);
        client2.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token2);

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = productoId, Cantidad = 1 } } })
        };
        req1.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = productoId, Cantidad = 1 } } })
        };
        req2.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        // Disparar concurrentemente
        var task1 = client1.SendAsync(req1);
        var task2 = client2.SendAsync(req2);
        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.OK);
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.Conflict);

        // Verificar en BD que el stock disponible es 0
        using var scope = _factory.Services.CreateScope();
        var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
        (stockDb.CantidadTotal - stockDb.CantidadReservada).Should().Be(0);
    }

    [SkippableFact]
    public async Task TestB_PagoRechazado_RetornaStockAValorInicial()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 10);

        // Crear client donde la pasarela simule fallo
        var brokenFactory = _factory!.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descPago = services.SingleOrDefault(d => d.ServiceType == typeof(IPaymentGateway));
                if (descPago != null) services.Remove(descPago);

                var mockPago = new Moq.Mock<IPaymentGateway>();
                mockPago.Setup(p => p.ProcesarPagoAsync(Moq.It.IsAny<PagoRequest>()))
                    .Returns(Task.FromResult(new PagoResult(false, null, "Tarjeta sin fondos")));

                services.AddScoped(_ => mockPago.Object);
            });
        });

        var customFactory = new CustomWebApplicationFactory();
        var token = customFactory.GenerateToken("Comprador", Guid.NewGuid().ToString());
        var client = brokenFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = productoId, Cantidad = 3 } } })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Verificar que la reserva fue liberada y el stock reservado volvió a 0
        using var scope = brokenFactory.Services.CreateScope();
        var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
        stockDb.CantidadTotal.Should().Be(10);
        stockDb.CantidadReservada.Should().Be(0);
        stockDb.CantidadDisponible.Should().Be(10);
    }

    [SkippableFact]
    public async Task TestC_CancelacionDeOrden_LiberaStockReservado()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 10);

        // Realizar checkout normal (compra 4 unidades)
        var customFactory = new CustomWebApplicationFactory();
        var compradorId = Guid.NewGuid();
        var tokenComprador = customFactory.GenerateToken("Comprador", compradorId.ToString());
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenComprador);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = productoId, Cantidad = 4 } } })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Obtener la orden creada
        using (var scope = _factory.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var orden = await finDb.Ordenes.FirstAsync(o => o.CompradorId == compradorId);

            // Cancelar la orden vía endpoint PUT /api/ordenes/{id}/estado con rol Admin
            var tokenAdmin = customFactory.GenerateToken("Admin", Guid.NewGuid().ToString());
            var adminClient = _factory.CreateClient();
            adminClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenAdmin);

            var cancelReq = await adminClient.PutAsJsonAsync($"/api/ordenes/{orden.Id}/estado", new { NuevoEstado = "Cancelada" });
            cancelReq.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verificar que el stock fue liberado
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada.Should().Be(0);
        }
    }

    [SkippableFact]
    public async Task TestD_ProductoSinStock_Retorna409Conflict()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 0);

        var customFactory = new CustomWebApplicationFactory();
        var token = customFactory.GenerateToken("Comprador", Guid.NewGuid().ToString());
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = productoId, Cantidad = 1 } } })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("StockInsuficiente");
    }

    [SkippableFact]
    public async Task TestE_ReservaExpirada_EsLiberadaPorCleanupJob()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 5);

        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var stockService = scope.ServiceProvider.GetRequiredService<IStockService>();

            // Reservar manualmente 3 unidades
            var res = await stockService.ReservarStockAsync(productoId, 3, Guid.NewGuid());
            res.Exitoso.Should().BeTrue();

            // Modificar la fecha de expiración para que ya haya expirado en el pasado
            var reservaDb = await catDb.ReservasStock.FirstAsync(r => r.Id == res.ReservaId);
            reservaDb.FechaExpiracion = DateTime.UtcNow.AddMinutes(-5);
            await catDb.SaveChangesAsync();

            // Ejecutar la purga del job
            var cleanup = scope.ServiceProvider.GetRequiredService<IHostedService>() as ReservaStockCleanupService
                ?? new ReservaStockCleanupService(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<IConfiguration>(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ReservaStockCleanupService>>());

            int purgadas = await cleanup.PurgarReservasExpiradasAsync();
            purgadas.Should().BeGreaterThanOrEqualTo(1);

            // Verificar que el stock reservado volvió a 0
            await catDb.Entry(reservaDb).ReloadAsync();
            reservaDb.Estado.Should().Be(EstadoReserva.Liberada);

            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada.Should().Be(0);
        }
    }

    [SkippableFact]
    public async Task TestF_VeinteCheckoutsConcurrentesParaStockDe5_StockNuncaNegativo_Exactamente5OK()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 5);

        var customFactory = new CustomWebApplicationFactory();
        var tasks = new List<Task<HttpResponseMessage>>();

        for (int i = 0; i < 20; i++)
        {
            var client = _factory!.CreateClient();
            var token = customFactory.GenerateToken("Comprador", Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
            {
                Content = JsonContent.Create(new { Items = new[] { new { ProductoId = productoId, Cantidad = 1 } } })
            };
            req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

            tasks.Add(client.SendAsync(req));
        }

        var responses = await Task.WhenAll(tasks);

        int okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        int conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        okCount.Should().Be(5, "exactamente 5 solicitudes debieron obtener stock");
        conflictCount.Should().Be(15, "las 15 solicitudes restantes debieron recibir 409 Conflict");

        // Verificar que el stock físico nunca quedó negativo
        using var scope = _factory.Services.CreateScope();
        var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
        (stockDb.CantidadTotal - stockDb.CantidadReservada).Should().Be(0);
    }

    [SkippableFact]
    public async Task TestG_ReservaComprometida_SobreviveAPurga_YSeLiberaAlCancelarOrden()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 10);

        var customFactory = new CustomWebApplicationFactory();
        var compradorId = Guid.NewGuid();
        var tokenComprador = customFactory.GenerateToken("Comprador", compradorId.ToString());
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenComprador);

        // 1. Checkout exitoso de 4 unidades
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = productoId, Cantidad = 4 } } })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid ordenId;
        Guid reservaId;

        using (var scope = _factory.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            var orden = await finDb.Ordenes.FirstAsync(o => o.CompradorId == compradorId);
            ordenId = orden.Id;

            // La reserva debe estar en estado Comprometida tras completarse el checkout
            var reserva = await catDb.ReservasStock.FirstAsync(r => r.OrdenId == ordenId);
            reservaId = reserva.Id;
            reserva.Estado.Should().Be(EstadoReserva.Comprometida);
            reserva.FechaExpiracion.Should().BeNull();

            // Forzar que FechaExpiracion quede en el pasado
            reserva.FechaExpiracion = DateTime.UtcNow.AddMinutes(-30);
            await catDb.SaveChangesAsync();

            // 2. Ejecutar la purga periódica del job de limpieza
            var cleanup = scope.ServiceProvider.GetRequiredService<IHostedService>() as ReservaStockCleanupService
                ?? new ReservaStockCleanupService(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<IConfiguration>(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ReservaStockCleanupService>>());

            int purgadas = await cleanup.PurgarReservasExpiradasAsync();

            // La reserva NO debe haber sido purgada porque su estado es Comprometida (el job solo toca Pendiente)
            await catDb.Entry(reserva).ReloadAsync();
            reserva.Estado.Should().Be(EstadoReserva.Comprometida);

            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada.Should().Be(4, "el stock reservado no debe cambiar tras la purga");
            stockDb.CantidadDisponible.Should().Be(6);
        }

        // 3. Cancelar la orden vía PUT /api/ordenes/{id}/estado con rol Admin
        var tokenAdmin = customFactory.GenerateToken("Admin", Guid.NewGuid().ToString());
        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenAdmin);

        var cancelRes = await adminClient.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { NuevoEstado = "Cancelada" });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Verificar que la reserva Comprometida fue liberada y el stock quedó restaurado
        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var reserva = await catDb.ReservasStock.FirstAsync(r => r.Id == reservaId);
            reserva.Estado.Should().Be(EstadoReserva.Liberada);

            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada.Should().Be(0);
            stockDb.CantidadDisponible.Should().Be(10);
        }
    }

    [SkippableFact]
    public async Task TestH_ReservaPendienteVencida_DeOrdenAprobada_EsReconciliadaAComprometidaPorJob()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, vendedorId) = await CrearProductoConStockAsync(cantidadTotal: 10);
        var compradorId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();
        var reservaId = Guid.NewGuid();

        // Simular que el proceso murió justo tras commitear la orden en Finance (Aprobada)
        // pero antes de llamar a ComprometerReservasOrdenAsync (la reserva quedó Pendiente y venció)
        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

            // Orden pagada/aprobada en Finance
            finDb.Ordenes.Add(new Orden
            {
                Id = ordenId,
                NumeroOrden = $"ORD-REC-{Guid.NewGuid():N}",
                CompradorId = compradorId,
                VendedorId = vendedorId,
                SubTotal = 40m,
                MontoTotal = 50.20m,
                Estado = EstadoOrden.Aprobada,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-20),
                FechaAprobacion = DateTime.UtcNow.AddMinutes(-20)
            });
            await finDb.SaveChangesAsync();

            // Reserva asociada a la orden en estado Pendiente y ya vencida
            catDb.ReservasStock.Add(new ReservaStock
            {
                Id = reservaId,
                ProductoId = productoId,
                OrdenId = ordenId,
                Cantidad = 3,
                Estado = EstadoReserva.Pendiente,
                FechaReserva = DateTime.UtcNow.AddMinutes(-20),
                FechaExpiracion = DateTime.UtcNow.AddMinutes(-5)
            });

            // Actualizar stock reservado
            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada = 3;
            await catDb.SaveChangesAsync();
        }

        // Ejecutar el job de limpieza
        using (var scope = _factory!.Services.CreateScope())
        {
            var cleanup = scope.ServiceProvider.GetRequiredService<IHostedService>() as ReservaStockCleanupService
                ?? new ReservaStockCleanupService(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<IConfiguration>(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ReservaStockCleanupService>>());

            int liberadas = await cleanup.PurgarReservasExpiradasAsync();
            liberadas.Should().Be(0, "la reserva no debe ser liberada porque su orden ya está aprobada/pagada");

            // Verificar que la reserva pasó a Comprometida y se eliminó la expiración
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var reserva = await catDb.ReservasStock.FirstAsync(r => r.Id == reservaId);
            reserva.Estado.Should().Be(EstadoReserva.Comprometida);
            reserva.FechaExpiracion.Should().BeNull();

            // El stock físico sigue reservado
            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada.Should().Be(3);
            stockDb.CantidadDisponible.Should().Be(7);
        }
    }

    [SkippableFact]
    public async Task TestI_ReservaPendienteVencida_DeOrdenInexistente_EsLiberadaPorJob()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var (productoId, _) = await CrearProductoConStockAsync(cantidadTotal: 10);
        var reservaId = Guid.NewGuid();
        var ordenInexistenteId = Guid.NewGuid();

        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            catDb.ReservasStock.Add(new ReservaStock
            {
                Id = reservaId,
                ProductoId = productoId,
                OrdenId = ordenInexistenteId,
                Cantidad = 2,
                Estado = EstadoReserva.Pendiente,
                FechaReserva = DateTime.UtcNow.AddMinutes(-20),
                FechaExpiracion = DateTime.UtcNow.AddMinutes(-5)
            });

            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada = 2;
            await catDb.SaveChangesAsync();
        }

        // Ejecutar job
        using (var scope = _factory!.Services.CreateScope())
        {
            var cleanup = scope.ServiceProvider.GetRequiredService<IHostedService>() as ReservaStockCleanupService
                ?? new ReservaStockCleanupService(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<IConfiguration>(), Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<ReservaStockCleanupService>>());

            int liberadas = await cleanup.PurgarReservasExpiradasAsync();
            liberadas.Should().Be(1, "la reserva de orden inexistente debe ser liberada");

            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var reserva = await catDb.ReservasStock.FirstAsync(r => r.Id == reservaId);
            reserva.Estado.Should().Be(EstadoReserva.Liberada);

            var stockDb = await catDb.StockFisico.FirstAsync(s => s.ProductoId == productoId);
            stockDb.CantidadReservada.Should().Be(0);
            stockDb.CantidadDisponible.Should().Be(10);
        }
    }
}
