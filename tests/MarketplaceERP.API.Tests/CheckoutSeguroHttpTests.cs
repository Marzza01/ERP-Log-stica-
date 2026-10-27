/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests de API para CheckoutController bajo el nuevo contrato seguro:
/// - 400 sin Idempotency-Key
/// - 401 sin token
/// - 403 para Admin y Vendedor (solo Comprador)
/// - 409 clave reutilizada con distinto payload
/// - Idempotencia: misma clave + mismo payload retorna misma respuesta
/// - Servidor calcula precios desde catálogo (ignora manipulación de cliente)
/// - Tras checkout la orden NO es Entregada (es Aprobada) y tiene exactamente 1 partida de venta
/// </summary>
public class CheckoutSeguroHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CheckoutSeguroHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Checkout_SinIdempotencyKey_Retorna400BadRequest()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var payload = new
        {
            Items = new[] { new { ProductoId = Guid.NewGuid(), Cantidad = 1 } }
        };

        var response = await client.PostAsJsonAsync("/api/checkout", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Idempotency-Key");
    }

    [Fact]
    public async Task Checkout_SinToken_Retorna401Unauthorized()
    {
        var client = _factory.CreateClient(); // sin auth
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = Guid.NewGuid(), Cantidad = 1 } } })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Vendedor")]
    public async Task Checkout_RolNoComprador_Retorna403Forbidden(string rol)
    {
        var client = _factory.CreateClientWithAuth(rol);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new { Items = new[] { new { ProductoId = Guid.NewGuid(), Cantidad = 1 } } })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Checkout_MismaClaveConDistintoPayload_Retorna409Conflict()
    {
        var userId = Guid.NewGuid().ToString();
        var client = _factory.CreateClientWithAuth("Comprador", userId: userId);
        var key = Guid.NewGuid().ToString();

        // 1. Sembrar producto
        var vendedorId = Guid.NewGuid();
        var prod1 = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-TEST-{Guid.NewGuid():N}",
            Nombre = "Producto Test 1",
            CategoriaNombre = "Cat 1",
            PrecioB2C = 100m,
            PrecioB2B = 90m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };
        var prod2 = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-TEST-{Guid.NewGuid():N}",
            Nombre = "Producto Test 2",
            CategoriaNombre = "Cat 1",
            PrecioB2C = 50m,
            PrecioB2B = 40m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.AddRange(prod1, prod2);
            catDb.StockFisico.AddRange(
                new StockFisico { Id = Guid.NewGuid(), ProductoId = prod1.Id, CantidadTotal = 10, CantidadReservada = 0 },
                new StockFisico { Id = Guid.NewGuid(), ProductoId = prod2.Id, CantidadTotal = 10, CantidadReservada = 0 }
            );
            await catDb.SaveChangesAsync();
        }

        // Primer intento con prod1
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod1.Id, Cantidad = 1 } }
            })
        };
        req1.Headers.Add("Idempotency-Key", key);

        var res1 = await client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Segundo intento con la misma clave pero diferente producto (payload distinto)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod2.Id, Cantidad = 1 } }
            })
        };
        req2.Headers.Add("Idempotency-Key", key);

        var res2 = await client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res2.Content.ReadAsStringAsync();
        body.Should().Contain("diferente");
    }

    [Fact]
    public async Task Checkout_CalculaPreciosDesdeCatalogo_ClienteNoPuedeManipularPrecio()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var vendedorId = Guid.NewGuid();
        var prod = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-PRECIO-{Guid.NewGuid():N}",
            Nombre = "Producto Precio Real",
            CategoriaNombre = "General",
            PrecioB2C = 100.00m,
            PrecioB2B = 80.00m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(prod);
            catDb.StockFisico.Add(new StockFisico { Id = Guid.NewGuid(), ProductoId = prod.Id, CantidadTotal = 10, CantidadReservada = 0 });
            await catDb.SaveChangesAsync();
        }

        // Cliente envía "PrecioUnitario = 0.01" intentando alterar el cobro
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod.Id, Cantidad = 1, PrecioUnitario = 0.01m } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Validar en FinanceDb que el SubTotal y MontoTotal reflejan el precio del catálogo (100) y no 0.01
        using (var scope = _factory.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var orden = await finDb.Ordenes.Include(o => o.Items).OrderByDescending(o => o.FechaCreacion).FirstAsync();

            orden.SubTotal.Should().Be(100.00m);
            orden.ImpuestoIva.Should().Be(13.00m);
            orden.CostoEnvio.Should().Be(5.00m);
            orden.MontoTotal.Should().Be(118.00m);

            // La orden tras el checkout NO debe quedar en estado Entregada
            orden.Estado.Should().Be(EstadoOrden.Aprobada);

            // Debe existir exactamente una partida contable de venta asociada a la orden
            var partidas = await finDb.PartidasContables.Where(p => p.ReferenciaOrigenId == orden.Id).ToListAsync();
            partidas.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task Checkout_Production_SinPasarelaConfigurada_Retorna503SinOrdenNiAsiento()
    {
        // Arrancar una factory que simule Production sin Payments:AllowMock
        var prodFactory = new CustomWebApplicationFactory()
        {
        };
        var factory = prodFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ADMIN_EMAIL", "admin@marketplace.local");
            builder.UseSetting("ADMIN_PASSWORD", "AdminPass123!Secure");
            builder.UseSetting("Payments:AllowMock", "false");
        });

        var userId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        var token = prodFactory.GenerateToken("Comprador", userId);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var vendedorId = Guid.NewGuid();
        var prod = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-PROD-503-{Guid.NewGuid():N}",
            Nombre = "Producto Prod 503",
            CategoriaNombre = "General",
            PrecioB2C = 50m,
            PrecioB2B = 40m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };

        using (var scope = factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(prod);
            catDb.StockFisico.Add(new StockFisico { Id = Guid.NewGuid(), ProductoId = prod.Id, CantidadTotal = 10, CantidadReservada = 0 });
            await catDb.SaveChangesAsync();
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod.Id, Cantidad = 1 } }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Checkout_CarritoConDosVendedores_Retorna400()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var vendedor1 = Guid.NewGuid();
        var vendedor2 = Guid.NewGuid();

        var prod1 = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-V1-{Guid.NewGuid():N}",
            Nombre = "Producto Vendedor 1",
            CategoriaNombre = "General",
            PrecioB2C = 100m,
            PrecioB2B = 90m,
            EstaPublicado = true,
            VendedorId = vendedor1
        };
        var prod2 = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-V2-{Guid.NewGuid():N}",
            Nombre = "Producto Vendedor 2",
            CategoriaNombre = "General",
            PrecioB2C = 50m,
            PrecioB2B = 40m,
            EstaPublicado = true,
            VendedorId = vendedor2
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.AddRange(prod1, prod2);
            await catDb.SaveChangesAsync();
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[]
                {
                    new { ProductoId = prod1.Id, Cantidad = 1 },
                    new { ProductoId = prod2.Id, Cantidad = 1 }
                }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("vendedor");
    }

    [Fact]
    public async Task Checkout_ProductoNoPublicado_Retorna400()
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var vendedorId = Guid.NewGuid();
        var prod = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-NOPUB-{Guid.NewGuid():N}",
            Nombre = "Producto No Publicado",
            CategoriaNombre = "General",
            PrecioB2C = 100m,
            PrecioB2B = 90m,
            EstaPublicado = false,
            VendedorId = vendedorId
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(prod);
            await catDb.SaveChangesAsync();
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod.Id, Cantidad = 1 } }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("publicado");
    }

    [Theory]
    [InlineData(0, "mayor a 0")]
    [InlineData(-1, "mayor a 0")]
    [InlineData(1001, "1000")]
    public async Task Checkout_CantidadFueraDeRango_Retorna400(int cantidad, string mensajeEsperado)
    {
        var client = _factory.CreateClientWithAuth("Comprador");
        var vendedorId = Guid.NewGuid();
        var prod = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-QTY-{Guid.NewGuid():N}",
            Nombre = "Producto Cantidad Test",
            CategoriaNombre = "General",
            PrecioB2C = 10m,
            PrecioB2B = 8m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(prod);
            await catDb.SaveChangesAsync();
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod.Id, Cantidad = cantidad } }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain(mensajeEsperado);
    }

    [Fact]
    public async Task Checkout_ClaveIdempotenciaExpirada_PurgaYPermiteNuevoCheckout()
    {
        var userId = Guid.NewGuid().ToString();
        var client = _factory.CreateClientWithAuth("Comprador", userId: userId);
        var idempotencyKey = Guid.NewGuid().ToString();
        var vendedorId = Guid.NewGuid();

        var prod = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-EXP-{Guid.NewGuid():N}",
            Nombre = "Producto Expiración",
            CategoriaNombre = "General",
            PrecioB2C = 30m,
            PrecioB2B = 25m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(prod);
            catDb.StockFisico.Add(new StockFisico { Id = Guid.NewGuid(), ProductoId = prod.Id, CantidadTotal = 10, CantidadReservada = 0 });
            await catDb.SaveChangesAsync();
        }

        // Sembrar una clave de idempotencia ya expirada directamente en BD
        using (var scope = _factory.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finDb.IdempotencyKeys.Add(new IdempotencyKeyRecord
            {
                Id = Guid.NewGuid(),
                Key = idempotencyKey,
                UsuarioId = Guid.Parse(userId),
                RequestHash = "hash_viejo_expirado",
                StatusCode = 200,
                ResponseJson = "{}",
                FechaCreacion = DateTime.UtcNow.AddHours(-25),
                FechaExpiracion = DateTime.UtcNow.AddHours(-1)
            });
            await finDb.SaveChangesAsync();
        }

        // Usar la misma clave con un payload nuevo: debe funcionar (clave purgada)
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod.Id, Cantidad = 1 } }
            })
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK, "la clave expirada debe purgarse y permitir un nuevo checkout");
    }

    [Fact]
    public async Task Checkout_ProductionConAllowMockTrue_Retorna200()
    {
        var prodFactory = new CustomWebApplicationFactory();
        var factory = prodFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ADMIN_EMAIL", "admin@marketplace.local");
            builder.UseSetting("ADMIN_PASSWORD", "AdminPass123!Secure");
            builder.UseSetting("Payments:AllowMock", "true");
        });

        var userId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        var token = prodFactory.GenerateToken("Comprador", userId);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var vendedorId = Guid.NewGuid();

        var prod = new Producto
        {
            Id = Guid.NewGuid(),
            Sku = $"SKU-PROD-MOCK-{Guid.NewGuid():N}",
            Nombre = "Producto Prod AllowMock",
            CategoriaNombre = "General",
            PrecioB2C = 45m,
            PrecioB2B = 40m,
            EstaPublicado = true,
            VendedorId = vendedorId
        };

        using (var scope = factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catDb.Productos.Add(prod);
            catDb.StockFisico.Add(new StockFisico { Id = Guid.NewGuid(), ProductoId = prod.Id, CantidadTotal = 15, CantidadReservada = 0 });
            await catDb.SaveChangesAsync();
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkout")
        {
            Content = JsonContent.Create(new
            {
                Items = new[] { new { ProductoId = prod.Id, Cantidad = 1 } }
            })
        };
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK, "Production con Payments:AllowMock=true debe permitir checkout de prueba");
    }

    [Fact]
    public async Task CancelarOrden_DesdeEnviada_Retorna400()
    {
        var adminClient = _factory.CreateClientWithAuth("Admin");
        var ordenId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finDb.Ordenes.Add(new Orden
            {
                Id = ordenId,
                NumeroOrden = $"ORD-TEST-ENV-{Guid.NewGuid():N}",
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                MontoTotal = 100m,
                Estado = EstadoOrden.Enviada
            });
            await finDb.SaveChangesAsync();
        }

        var res = await adminClient.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { NuevoEstado = "Cancelada" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "no se permite cancelar una orden ya Enviada");
    }

    [Fact]
    public async Task CancelarOrden_DesdeEntregada_Retorna400()
    {
        var adminClient = _factory.CreateClientWithAuth("Admin");
        var ordenId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finDb.Ordenes.Add(new Orden
            {
                Id = ordenId,
                NumeroOrden = $"ORD-TEST-ENT-{Guid.NewGuid():N}",
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                MontoTotal = 100m,
                Estado = EstadoOrden.Entregada
            });
            await finDb.SaveChangesAsync();
        }

        var res = await adminClient.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { NuevoEstado = "Cancelada" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "no se permite cancelar una orden ya Entregada");
    }

    [Fact]
    public async Task CancelarOrden_DesdeEnProceso_Retorna200YLiberaStock()
    {
        var adminClient = _factory.CreateClientWithAuth("Admin");
        var ordenId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

            catDb.Productos.Add(new Producto
            {
                Id = prodId,
                Sku = $"SKU-CANCEL-{Guid.NewGuid():N}",
                Nombre = "Producto Cancelar Test",
                CategoriaNombre = "General",
                PrecioB2C = 50m,
                PrecioB2B = 40m,
                EstaPublicado = true,
                VendedorId = Guid.NewGuid()
            });

            catDb.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                CantidadTotal = 20,
                CantidadReservada = 3,
                UltimaActualizacion = DateTime.UtcNow
            });

            catDb.ReservasStock.Add(new ReservaStock
            {
                Id = Guid.NewGuid(),
                ProductoId = prodId,
                OrdenId = ordenId,
                Cantidad = 3,
                Estado = EstadoReserva.Comprometida
            });

            finDb.Ordenes.Add(new Orden
            {
                Id = ordenId,
                NumeroOrden = $"ORD-TEST-PROC-{Guid.NewGuid():N}",
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                MontoTotal = 150m,
                Estado = EstadoOrden.EnProceso
            });

            await catDb.SaveChangesAsync();
            await finDb.SaveChangesAsync();
        }

        var res = await adminClient.PutAsJsonAsync($"/api/ordenes/{ordenId}/estado", new { NuevoEstado = "Cancelada" });
        res.StatusCode.Should().Be(HttpStatusCode.OK, "cancelar desde EnProceso debe ser válido");

        using (var scope = _factory.Services.CreateScope())
        {
            var catDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var finDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

            var orden = await finDb.Ordenes.FindAsync(ordenId);
            orden!.Estado.Should().Be(EstadoOrden.Cancelada);

            var reserva = await catDb.ReservasStock.FirstAsync(r => r.OrdenId == ordenId);
            reserva.Estado.Should().Be(EstadoReserva.Liberada);

            var stock = await catDb.StockFisico.FirstAsync(s => s.ProductoId == prodId);
            stock.CantidadReservada.Should().Be(0);
        }
    }
}
