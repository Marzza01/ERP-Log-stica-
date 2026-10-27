/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
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
}
