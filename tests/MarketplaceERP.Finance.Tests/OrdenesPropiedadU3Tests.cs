using System.Security.Claims;
using System.Text.Json;
using MarketplaceERP.API.Contracts;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

// Executes only the read action against EF's nonrelational InMemory provider.
// No application host, PostgreSQL, EnsureCreated, migrations or payment/inventory services.
public class OrdenesPropiedadU3Tests
{
    private static FinanceDbContext MemoryContext() => new(new DbContextOptionsBuilder<FinanceDbContext>()
        .UseInMemoryDatabase("u3-" + Guid.NewGuid()).Options);

    private static OrdenesController Controller(FinanceDbContext db, string? role, Guid actor,
        params Claim[] additional)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actor.ToString()) };
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        claims.AddRange(additional);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, role is null ? null : "isolated-test"));
        return new OrdenesController(db, null!, NullLogger<OrdenesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
        };
    }

    private static Orden Order(Guid buyer, Guid seller) => new()
    {
        Id = Guid.NewGuid(), NumeroOrden = "TEST-" + Guid.NewGuid(), CompradorId = buyer, VendedorId = seller,
        SubTotal = 10m, ImpuestoIva = 1.3m, CostoEnvio = 5m, MontoTotal = 16.3m,
        IdempotencyKey = Guid.NewGuid().ToString("N"), CheckoutPayloadHash = Guid.NewGuid().ToString("N"),
        Items = new List<ItemOrden>
        {
            new() { Id = Guid.NewGuid(), ProductoId = Guid.NewGuid(), Cantidad = 1,
                PrecioUnitario = 10m, NombreProducto = "Synthetic item", SkuReferencia = "TEST" }
        },
        SplitPago = new SplitPago { Id = Guid.NewGuid(), TransaccionId = Guid.NewGuid().ToString("N"),
            MontoTotal = 16.3m, MontoVendedor = 14.67m, MontoMarketplace = 1.63m }
    };

    [Fact]
    public async Task BuyerA_CannotReadBuyerB_OrderOrItsItems()
    {
        await using var db = MemoryContext();
        var buyerA = Guid.NewGuid(); var buyerB = Guid.NewGuid();
        var own = Order(buyerA, Guid.NewGuid()); var other = Order(buyerB, Guid.NewGuid());
        db.AddRange(own, other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var controller = Controller(db, "Comprador", buyerA);
        Assert.IsType<NotFoundResult>(await controller.Obtener(other.Id, default));
        Assert.Empty(db.ChangeTracker.Entries());
        var result = Assert.IsType<OkObjectResult>(await controller.Obtener(own.Id, default));
        var response = Assert.IsType<OrdenCompradorResponse>(result.Value);
        Assert.Equal(own.Id, response.Id);
        Assert.Equal(own.Items.Single().ProductoId, Assert.Single(response.Items).ProductoId);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ManipulatedOwnerInputs_DoNotOverrideTokenIdentity()
    {
        await using var db = MemoryContext();
        var buyerA = Guid.NewGuid(); var buyerB = Guid.NewGuid(); var seller = Guid.NewGuid();
        var other = Order(buyerB, seller); db.Add(other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var controller = Controller(db, "Comprador", buyerA,
            new Claim("CompradorId", buyerB.ToString()), new Claim("VendedorId", seller.ToString()),
            new Claim(ClaimTypes.Name, buyerB.ToString()));
        controller.Request.QueryString = new QueryString($"?compradorId={buyerB}&vendedorId={seller}");
        Assert.IsType<NotFoundResult>(await controller.Obtener(other.Id, default));
    }

    [Fact]
    public async Task ForeignMissingAndEmptyIds_ReturnTheSameNotFoundShape()
    {
        await using var db = MemoryContext();
        var other = Order(Guid.NewGuid(), Guid.NewGuid()); db.Add(other); await db.SaveChangesAsync();
        var controller = Controller(db, "Comprador", Guid.NewGuid());
        foreach (var id in new[] { other.Id, Guid.NewGuid(), Guid.Empty })
            Assert.IsType<NotFoundResult>(await controller.Obtener(id, default));
    }

    [Fact]
    public async Task BuyerView_ExcludesPaymentInternalsAndNavigationGraphs()
    {
        await using var db = MemoryContext();
        var buyer = Guid.NewGuid(); var own = Order(buyer, Guid.NewGuid());
        // Multiple items do not prove a single seller: buyer may read their purchase,
        // but no seller is granted access to the whole order.
        own.Items.Add(new ItemOrden { Id = Guid.NewGuid(), ProductoId = Guid.NewGuid(),
            Cantidad = 1, PrecioUnitario = 1m, NombreProducto = "Second synthetic item" });
        db.Add(own); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result = Assert.IsType<OkObjectResult>(await Controller(db, "Comprador", buyer).Obtener(own.Id, default));
        var response = Assert.IsType<OrdenCompradorResponse>(result.Value);
        Assert.Equal(2, response.Items.Count);
        var json = JsonSerializer.Serialize(response);
        foreach (var property in new[] { "SplitPago", "TransaccionId", "MontoVendedor", "MontoMarketplace",
                     "IdempotencyKey", "CheckoutPayloadHash", "CompradorId", "VendedorId" })
            Assert.DoesNotContain("\"" + property + "\"", json);
        Assert.DoesNotContain(own.IdempotencyKey!, json);
        Assert.DoesNotContain(own.CheckoutPayloadHash!, json);
        Assert.Equal(own.MontoTotal, response.MontoTotal);
    }

    [Fact]
    public async Task Admin_RetainsOriginalFullOrderResponse()
    {
        await using var db = MemoryContext();
        var order = Order(Guid.NewGuid(), Guid.NewGuid()); db.Add(order); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var result = Assert.IsType<OkObjectResult>(await Controller(db, "Admin", Guid.NewGuid()).Obtener(order.Id, default));
        var full = Assert.IsType<Orden>(result.Value);
        Assert.Equal(order.Id, full.Id);
        Assert.NotNull(full.SplitPago);
        Assert.Single(full.Items);
        Assert.Equal(order.CompradorId, full.CompradorId);
        Assert.Equal(order.IdempotencyKey, full.IdempotencyKey);
    }

    [Theory]
    [InlineData("Vendedor")]
    [InlineData("Unknown")]
    public async Task SellerOrUnknownRole_IsDeniedEvenWithMatchingSellerClaim(string role)
    {
        // No provider: a query here would fail, proving the denial occurs before data access.
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().Options);
        Assert.IsType<ForbidResult>(await Controller(db, role, Guid.NewGuid(),
            new Claim("VendedorId", Guid.NewGuid().ToString())).Obtener(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Anonymous_IsDeniedBeforeDataAccess()
    {
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().Options);
        Assert.IsType<UnauthorizedResult>(await Controller(db, null, Guid.NewGuid()).Obtener(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task EmptyOrConflictingIdentity_IsDeniedBeforeDataAccess()
    {
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().Options);
        Assert.IsType<UnauthorizedResult>(await Controller(db, "Comprador", Guid.Empty).Obtener(Guid.NewGuid(), default));
        Assert.IsType<UnauthorizedResult>(await Controller(db, "Comprador", Guid.NewGuid(),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())).Obtener(Guid.NewGuid(), default));
    }
}
