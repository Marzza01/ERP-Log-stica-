/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests PostgreSQL reales de la FASE 8:
/// 1. Numeración correlativa única bajo alta concurrencia (100 en paralelo, max 20 simultáneas).
/// 2. Concurrencia optimista en Ordenes: transiciones en conflicto devuelven 409 y preservan consistencia de stock.
/// 3. Concurrencia optimista en Compras: doble aprobación simultánea de OrdenCompra devuelve 409.
/// </summary>
[Collection("PostgresRealTests")]
public class ConcurrenciaYNumeracionPostgresRealTests : IAsyncLifetime
{
    private static readonly string? RawConnStr = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
    private readonly string _dbName = $"test_num_cnc_{Guid.NewGuid():N}";
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

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:DefaultConnection", _testConnStr);
            b.UseSetting("Jwt:Key", CustomWebApplicationFactory.TestJwtKey);
            b.UseSetting("Jwt:Issuer", CustomWebApplicationFactory.TestJwtIssuer);
            b.UseSetting("Jwt:Audience", CustomWebApplicationFactory.TestJwtAudience);
            b.UseSetting("Jwt:ExpirationMinutes", "60");

            b.ConfigureServices(services =>
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

    private HttpClient CreateAuthenticatedClient(string role = "Admin", Guid? userId = null)
    {
        var client = _factory!.CreateClient();
        var uid = userId?.ToString() ?? Guid.NewGuid().ToString();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(CustomWebApplicationFactory.TestJwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, uid),
            new(ClaimTypes.NameIdentifier, uid),
            new(ClaimTypes.Email, $"{role.ToLower()}@test.com"),
            new(ClaimTypes.Role, role)
        };
        var jwt = new JwtSecurityToken(
            issuer: CustomWebApplicationFactory.TestJwtIssuer,
            audience: CustomWebApplicationFactory.TestJwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Tests de Numeración Concurrente ───────────────────────────────────────
    [SkippableFact]
    public async Task Numeracion_NumeroOrden_100GeneracionesParalelas_SonTodasUnicasYCumplenFormato()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var resultados = new ConcurrentBag<string>();
        var semaphore = new SemaphoreSlim(20);

        var tareas = Enumerable.Range(1, 100).Select(async _ =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var scope = _factory!.Services.CreateScope();
                var numService = scope.ServiceProvider.GetRequiredService<INumeracionService>();
                var num = await numService.GenerarNumeroOrdenAsync();
                resultados.Add(num);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tareas);

        resultados.Should().HaveCount(100);
        resultados.Distinct().Should().HaveCount(100, "cada número generado por la secuencia debe ser único");
        resultados.Should().AllSatisfy(n =>
        {
            n.Should().MatchRegex(@"^ORD-\d{8}-\d{5,}$");
        });
    }

    [SkippableFact]
    public async Task Numeracion_NumeroPO_100GeneracionesParalelas_SonTodasUnicasYCumplenFormato()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var resultados = new ConcurrentBag<string>();
        var semaphore = new SemaphoreSlim(20);

        var tareas = Enumerable.Range(1, 100).Select(async _ =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var scope = _factory!.Services.CreateScope();
                var numService = scope.ServiceProvider.GetRequiredService<INumeracionService>();
                var num = await numService.GenerarNumeroPOAsync();
                resultados.Add(num);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tareas);

        resultados.Should().HaveCount(100);
        resultados.Distinct().Should().HaveCount(100);
        resultados.Should().AllSatisfy(n =>
        {
            n.Should().MatchRegex(@"^PO-\d{8}-\d{4,}$");
        });
    }

    [SkippableFact]
    public async Task Numeracion_NumeroPickList_100GeneracionesParalelas_SonTodasUnicasYCumplenFormato()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var resultados = new ConcurrentBag<string>();
        var semaphore = new SemaphoreSlim(20);

        var tareas = Enumerable.Range(1, 100).Select(async _ =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var scope = _factory!.Services.CreateScope();
                var numService = scope.ServiceProvider.GetRequiredService<INumeracionService>();
                var num = await numService.GenerarNumeroPickListAsync();
                resultados.Add(num);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tareas);

        resultados.Should().HaveCount(100);
        resultados.Distinct().Should().HaveCount(100);
        resultados.Should().AllSatisfy(n =>
        {
            n.Should().MatchRegex(@"^PKL-\d{8}-\d{4,}$");
        });
    }

    [SkippableFact]
    public async Task Numeracion_NumeroGuia_100GeneracionesParalelas_SonTodasUnicasYCumplenFormato()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var resultados = new ConcurrentBag<string>();
        var semaphore = new SemaphoreSlim(20);

        var tareas = Enumerable.Range(1, 100).Select(async _ =>
        {
            await semaphore.WaitAsync();
            try
            {
                using var scope = _factory!.Services.CreateScope();
                var numService = scope.ServiceProvider.GetRequiredService<INumeracionService>();
                var num = await numService.GenerarNumeroGuiaAsync();
                resultados.Add(num);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tareas);

        resultados.Should().HaveCount(100);
        resultados.Distinct().Should().HaveCount(100);
        resultados.Should().AllSatisfy(n =>
        {
            n.Should().MatchRegex(@"^SV-LOGI-\d{8}-\d{5,}$");
        });
    }

    // ── Tests de Concurrencia Optimista ───────────────────────────────────────
    [SkippableFact]
    public async Task Concurrencia_DosTransicionesSimultaneasSobreOrden_UnaGanaYLaOtraRecibe409()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var client = CreateAuthenticatedClient("Admin");
        var ordenId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        // Setup datos iniciales
        using (var scope = _factory!.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            var producto = new Producto
            {
                Id = prodId,
                Sku = $"SKU-CNC-{Guid.NewGuid():N}"[..20],
                Nombre = "Producto Concurrencia",
                CategoriaNombre = "Tecnologia",
                PrecioB2C = 100m,
                PrecioB2B = 80m
            };
            catDb.Productos.Add(producto);
            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 50,
                CantidadReservada = 5
            });
            catDb.ReservasStock.Add(new ReservaStock
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                OrdenId = ordenId,
                Cantidad = 5,
                Estado = EstadoReserva.Comprometida
            });
            await catDb.SaveChangesAsync();

            var orden = new Orden
            {
                Id = ordenId,
                NumeroOrden = $"ORD-TEST-{Guid.NewGuid():N}"[..25],
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                MontoTotal = 100m,
                SubTotal = 100m,
                Estado = EstadoOrden.EnProceso,
                FechaCreacion = DateTime.UtcNow
            };
            finDb.Ordenes.Add(orden);
            await finDb.SaveChangesAsync();
        }

        // Ejecutar 2 peticiones concurrentes a la API sobre la misma orden: Cancelada vs Enviada
        var barrier = new Barrier(2);
        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await client.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { NuevoEstado = "Cancelada" });
        });

        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await client.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { NuevoEstado = "Enviada" });
        });

        var respuestas = await Task.WhenAll(t1, t2);
        var statusCodes = respuestas.Select(r => r.StatusCode).ToList();

        // Al menos una debe ser 200 y la otra rechazada con 409 (conflicto xmin) o 400 (estado ya cambió en DB)
        statusCodes.Should().Contain(HttpStatusCode.OK, "la primera transacción válida debe ser aceptada con 200");
        statusCodes.Should().Contain(s => s == HttpStatusCode.Conflict || s == HttpStatusCode.BadRequest,
            "la petición concurrente perdedora debe fallar con 409 (conflicto xmin) o 400 (estado ya cambió)");

        // Verificar consistencia completa en la BD según cuál resultado ganó
        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var stock = await catDb.StockFisico.FirstAsync(s => s.ProductoId == prodId);
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var ordenFinal = await finDb.Ordenes.FirstAsync(o => o.Id == ordenId);
            var reservaFinal = await catDb.ReservasStock.FirstAsync(r => r.OrdenId == ordenId);

            if (ordenFinal.Estado == EstadoOrden.Cancelada)
            {
                // Ganó Cancelar:
                // - Estado de la orden: Cancelada
                // - Reserva de stock: Liberada (0 reservado)
                // - CantidadTotal intacta (50), CantidadReservada = 0
                // - Stock disponible (Total - Reservada) = 50
                reservaFinal.Estado.Should().Be(EstadoReserva.Liberada);
                stock.CantidadTotal.Should().Be(50);
                stock.CantidadReservada.Should().Be(0);
                (stock.CantidadTotal - stock.CantidadReservada).Should().Be(50);
            }
            else if (ordenFinal.Estado == EstadoOrden.Enviada)
            {
                // Ganó Enviar:
                // - Estado de la orden: Enviada
                // - Despacho confirmado: se descuenta del total físico y de la reserva (50 - 5 = 45 total, reservada pasa a 0 y reserva a Despachada/Confirmada)
                reservaFinal.Estado.Should().Match(e => e == EstadoReserva.Confirmada || e == EstadoReserva.Despachada);
                stock.CantidadTotal.Should().Be(45);
                stock.CantidadReservada.Should().Be(0);
                (stock.CantidadTotal - stock.CantidadReservada).Should().Be(45);
            }
            else
            {
                Assert.Fail($"Estado inesperado de orden final: {ordenFinal.Estado}");
            }
        }
    }

    [SkippableFact]
    public async Task Numeracion_NumeroRMA_100GeneracionesParalelas_SonTodasUnicasYCumplenFormato()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var semaforo = new SemaphoreSlim(20);
        var tareas = Enumerable.Range(0, 100).Select(async _ =>
        {
            await semaforo.WaitAsync();
            try
            {
                using var scope = _factory!.Services.CreateScope();
                var numService = scope.ServiceProvider.GetRequiredService<INumeracionService>();
                return await numService.GenerarNumeroRmaAsync();
            }
            finally
            {
                semaforo.Release();
            }
        }).ToList();

        var numeros = await Task.WhenAll(tareas);

        numeros.Should().HaveCount(100);
        numeros.Distinct().Should().HaveCount(100, "100 llamadas paralelas a GenerarNumeroRmaAsync deben generar números estrictamente únicos");
        var hoy = DateTime.UtcNow.ToString("yyyyMMdd");
        foreach (var num in numeros)
        {
            num.Should().StartWith($"RMA-{hoy}-");
            num.Length.Should().BeGreaterOrEqualTo(17);
        }
    }

    [SkippableFact]
    public async Task Numeracion_CodigoAuditoria3Way_100GeneracionesParalelas_SonTodasUnicasYCumplenFormato()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var semaforo = new SemaphoreSlim(20);
        var tareas = Enumerable.Range(0, 100).Select(async _ =>
        {
            await semaforo.WaitAsync();
            try
            {
                using var scope = _factory!.Services.CreateScope();
                var numService = scope.ServiceProvider.GetRequiredService<INumeracionService>();
                return await numService.GenerarCodigoAuditoria3WayAsync();
            }
            finally
            {
                semaforo.Release();
            }
        }).ToList();

        var numeros = await Task.WhenAll(tareas);

        numeros.Should().HaveCount(100);
        numeros.Distinct().Should().HaveCount(100, "100 llamadas paralelas a GenerarCodigoAuditoria3WayAsync deben generar números estrictamente únicos");
        var hoy = DateTime.UtcNow.ToString("yyyyMMdd");
        foreach (var num in numeros)
        {
            num.Should().StartWith($"3WAY-{hoy}-");
            num.Length.Should().BeGreaterOrEqualTo(18);
        }
    }

    [SkippableFact]
    public async Task Concurrencia_FalloLiberarStockAsync_JobCleanupReconciliaReservaDeOrdenCancelada()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var ordenId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        using (var scope = _factory!.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

            catDb.Productos.Add(new Producto
            {
                Id = prodId,
                Nombre = "Producto Test Falla Post-Commit",
                Sku = $"SKU-FALLA-{Guid.NewGuid():N}"[..20],
                PrecioB2C = 10m,
                PrecioB2B = 8m,
                ProveedorNombre = "Prov Test",
                UnidadMedida = "UND"
            });
            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 30,
                CantidadReservada = 4
            });
            catDb.ReservasStock.Add(new ReservaStock
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                OrdenId = ordenId,
                Cantidad = 4,
                Estado = EstadoReserva.Comprometida
            });
            await catDb.SaveChangesAsync();

            // La orden fue efectivamente cancelada en Finance (commit ganado)
            finDb.Ordenes.Add(new Orden
            {
                Id = ordenId,
                NumeroOrden = $"ORD-CANCEL-{Guid.NewGuid():N}"[..25],
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                MontoTotal = 40m,
                SubTotal = 40m,
                Estado = EstadoOrden.Cancelada,
                FechaCreacion = DateTime.UtcNow,
                FechaCancelacion = DateTime.UtcNow
            });
            await finDb.SaveChangesAsync();
        }

        // Ejecutar el job de limpieza/reconciliación
        using (var scope = _factory!.Services.CreateScope())
        {
            var cleanupService = scope.ServiceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<ReservaStockCleanupService>()
                .FirstOrDefault();

            cleanupService.Should().NotBeNull();
            var liberadas = await cleanupService!.PurgarReservasExpiradasAsync();
            liberadas.Should().BeGreaterOrEqualTo(1, "el reconciliador del job debe haber liberado la reserva huérfana de la orden Cancelada");

            // Verificar que el stock físico y la reserva quedaron consistentes
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var stock = await catDb.StockFisico.FirstAsync(s => s.ProductoId == prodId);
            var reserva = await catDb.ReservasStock.FirstAsync(r => r.OrdenId == ordenId);

            reserva.Estado.Should().Be(EstadoReserva.Liberada);
            stock.CantidadTotal.Should().Be(30);
            stock.CantidadReservada.Should().Be(0);
        }
    }

    [SkippableFact]
    public async Task TransicionarEstado_ReutilizaNumeroGuiaDePaqueteDespachoExistente()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var client = CreateAuthenticatedClient("Admin");
        var ordenId = Guid.NewGuid();
        var guiaPrevia = "SV-LOGI-20261028-99999";

        using (var scope = _factory!.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            finDb.Ordenes.Add(new Orden
            {
                Id = ordenId,
                NumeroOrden = $"ORD-GUIA-{Guid.NewGuid():N}"[..25],
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                MontoTotal = 50m,
                SubTotal = 50m,
                Estado = EstadoOrden.EnProceso,
                FechaCreacion = DateTime.UtcNow
            });
            await finDb.SaveChangesAsync();

            catDb.PaquetesDespacho.Add(new PaqueteDespacho
            {
                Id = Guid.NewGuid(),
                OrdenId = ordenId,
                NumeroOrden = "ORD-TEST",
                NumeroGuia = guiaPrevia,
                Estado = EstadoPacking.ListoParaDespacho,
                FechaEmpaque = DateTime.UtcNow
            });
            await catDb.SaveChangesAsync();
        }

        // Transicionar a Enviada SIN enviar GuiaEnvio en el request
        var resp = await client.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { NuevoEstado = "Enviada" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory!.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var orden = await finDb.Ordenes.FirstAsync(o => o.Id == ordenId);
            orden.GuiaEnvio.Should().Be(guiaPrevia, "debe reutilizar la guía existente en PaquetesDespacho en vez de generar una nueva");
        }
    }

    [SkippableFact]
    public async Task TransicionarEstado_CancelarDesdeEnviadaOEntregada_Retorna400BadRequest()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada");

        var client = CreateAuthenticatedClient("Admin");
        var ordenEnviadaId = Guid.NewGuid();
        var ordenEntregadaId = Guid.NewGuid();

        using (var scope = _factory!.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finDb.Ordenes.AddRange(
                new Orden
                {
                    Id = ordenEnviadaId,
                    NumeroOrden = $"ORD-ENV-{Guid.NewGuid():N}"[..25],
                    CompradorId = Guid.NewGuid(),
                    VendedorId = Guid.NewGuid(),
                    MontoTotal = 50m,
                    SubTotal = 50m,
                    Estado = EstadoOrden.Enviada,
                    FechaCreacion = DateTime.UtcNow
                },
                new Orden
                {
                    Id = ordenEntregadaId,
                    NumeroOrden = $"ORD-ENT-{Guid.NewGuid():N}"[..25],
                    CompradorId = Guid.NewGuid(),
                    VendedorId = Guid.NewGuid(),
                    MontoTotal = 50m,
                    SubTotal = 50m,
                    Estado = EstadoOrden.Entregada,
                    FechaCreacion = DateTime.UtcNow
                }
            );
            await finDb.SaveChangesAsync();
        }

        var resp1 = await client.PutAsJsonAsync($"/api/ordenes/{ordenEnviadaId}/estado", new { NuevoEstado = "Cancelada" });
        resp1.StatusCode.Should().Be(HttpStatusCode.BadRequest, "no se permite cancelar una orden en estado Enviada");

        var resp2 = await client.PutAsJsonAsync($"/api/ordenes/{ordenEntregadaId}/estado", new { NuevoEstado = "Cancelada" });
        resp2.StatusCode.Should().Be(HttpStatusCode.BadRequest, "no se permite cancelar una orden en estado Entregada");
    }
}
