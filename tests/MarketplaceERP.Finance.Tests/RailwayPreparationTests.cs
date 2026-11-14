using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class RailwayPreparationTests
{
    [Theory]
    [InlineData("postgres")]
    [InlineData("postgresql")]
    public void DatabaseUrl_WithoutPort_UsesPostgresDefault(string scheme)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = $"{scheme}://person:p%40ss%3Aword@db.internal/store"
        }).Build();

        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseConnectionConfiguration.Resolve(config));
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("db.internal", parsed.Host);
        Assert.Equal("store", parsed.Database);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal(Npgsql.SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void DatabaseUrl_ExplicitZeroPort_IsRejectedWithoutEchoingCredentials()
    {
        const string secret = "invalid-port-test-only";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = $"postgresql://person:{secret}@db.internal:0/store"
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => DatabaseConnectionConfiguration.Resolve(config));
        Assert.DoesNotContain(secret, error.Message);
    }

    [Theory]
    [InlineData(5432)]
    [InlineData(6543)]
    public void DatabaseUrl_ExplicitPort_IsPreserved(int port)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = $"postgresql://person:test-only@db.internal:{port}/store"
        }).Build();

        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseConnectionConfiguration.Resolve(config));
        Assert.Equal(port, parsed.Port);
        Assert.Equal(Npgsql.SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void ConnectionResolution_IsSharedAndDoesNotLoseEscapedCredentials()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://person:p%40ss%3Aword@db.internal:5432/store",
            ["ConnectionStrings:DefaultConnection"] = "Host=other.invalid;Database=other"
        }).Build();
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseConnectionConfiguration.Resolve(config));
        Assert.Equal("db.internal", parsed.Host);
        Assert.Equal("store", parsed.Database);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal(Npgsql.SslMode.Require, parsed.SslMode);

        config["CONNECTION_STRING"] = "Host=explicit.internal;Database=explicit;Username=person";
        Assert.Equal(config["CONNECTION_STRING"], DatabaseConnectionConfiguration.Resolve(config));
    }

    [Fact]
    public void InvalidDatabaseUrl_DoesNotEchoCredentials()
    {
        const string secret = "sensitive-example";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = $"postgresql://person:{secret}@db.internal:5432/store?unexpected=true"
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => DatabaseConnectionConfiguration.Resolve(config));
        Assert.DoesNotContain(secret, error.Message);
    }

    [Theory]
    [InlineData("8080", "http://0.0.0.0:8080")]
    [InlineData("12345", "http://0.0.0.0:12345")]
    [InlineData(null, null)]
    public void RuntimePort_BindsAllInterfaces(string? input, string? expected) =>
        Assert.Equal(expected, RuntimePortConfiguration.ResolveUrls(input));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData(" ")]
    public void RuntimePort_RejectsInvalidValues(string input) =>
        Assert.Throws<InvalidOperationException>(() => RuntimePortConfiguration.ResolveUrls(input));

    [Fact]
    public void DisabledMode_StopsActionBeforeAnyBusinessCode()
    {
        var services = new ServiceCollection().AddSingleton(new PaymentOperationsMode(true)).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(action, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), new object());
        new PaymentOperationsGuardAttribute().OnActionExecuting(context);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void FinancialMutationEndpoints_HaveThePreActionGuard()
    {
        foreach (var (controller, actions) in new[]
        {
            (typeof(CheckoutController), new[] { "ProcesarCheckout" }),
            (typeof(LiquidacionesController), new[] { "Generar", "ProcesarPago" }),
            (typeof(CheckoutRecuperacionController), new[] { "Conciliar", "Reembolsar" }),
            (typeof(OrdenesController), new[] { "TransicionarEstado" }),
            (typeof(ContabilidadController), new[] { "AsientoManual" })
        })
            foreach (var action in actions)
                Assert.NotNull(controller.GetMethod(action)!.GetCustomAttributes(typeof(PaymentOperationsGuardAttribute), true).SingleOrDefault());
    }

    [Theory]
    [InlineData(typeof(ContabilidadController), "AsientoManual", "asiento-manual")]
    public async Task DisabledMode_BlocksNewFinancialRoutesBeforeAction(
        Type controller, string actionName, string route)
    {
        var method = controller.GetMethod(actionName)!;
        var guard = Assert.IsType<PaymentOperationsGuardAttribute>(
            Assert.Single(method.GetCustomAttributes(typeof(PaymentOperationsGuardAttribute), true)));
        Assert.Equal(route, Assert.IsType<HttpPostAttribute>(
            Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true))).Template);
        Assert.Equal("Admin", Assert.IsType<AuthorizeAttribute>(
            Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true))).Roles);

        var services = new ServiceCollection().AddSingleton(new PaymentOperationsMode(true)).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var filters = new List<IFilterMetadata> { guard };
        var context = new ActionExecutingContext(action, filters,
            new Dictionary<string, object?>(), new object());
        var actionCalls = 0;
        await guard.OnActionExecutionAsync(context, () =>
        {
            actionCalls++;
            return Task.FromResult(new ActionExecutedContext(action, filters, new object()));
        });

        Assert.Equal(0, actionCalls);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.Equal("no-store", http.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task DisabledMode_LiquidationServiceDoesNotPersistOrPostEntries()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new FinanceDbContext(options);
        var accounting = new Mock<IContabilidadService>(MockBehavior.Strict);
        var service = new LiquidacionService(db, accounting.Object,
            NullLogger<LiquidacionService>.Instance, new PaymentOperationsMode(true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerarLiquidacionAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProcesarPagoAsync(Guid.NewGuid()));
        accounting.VerifyNoOtherCalls();
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.Liquidaciones.ToListAsync());
        Assert.Empty(await db.PartidasContables.ToListAsync());
    }
}
