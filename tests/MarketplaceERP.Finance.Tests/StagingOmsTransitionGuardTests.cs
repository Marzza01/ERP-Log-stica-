using MarketplaceERP.API.Services;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class StagingOmsTransitionGuardTests
{
    private const string QaHost = "ep-oms-qa-isolated.c-15.us-east-1.aws.neon.tech";
    private const string SyntheticConnection =
        "Host=" + QaHost + ";Port=5432;Database=marketplaceerp_test;" +
        "Username=marketplaceerp_test_owner;Password=synthetic-qa-only;" +
        "SSL Mode=VerifyFull;Channel Binding=Require";

    private static IConfiguration Config(bool enabled = true, string serviceName = "marketplaceerp-api-staging",
        string target = "NeonIsolated", string provider = "Disabled", string host = QaHost)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Oms:StagingTransitions:Enabled"] = enabled ? "true" : "false",
            ["RENDER_SERVICE_NAME"] = serviceName,
            ["Deployment:Target"] = target,
            ["Payments:Provider"] = provider,
            ["Deployment:ApprovedStagingHost"] = host,
            ["CONNECTION_STRING"] = SyntheticConnection,
            ["ConnectionStrings:DefaultConnection"] = SyntheticConnection
        }).Build();
    }

    private static ActionExecutingContext Execute(IConfiguration config, bool paymentsDisabled = true)
    {
        var services = new ServiceCollection()
            .AddSingleton(config)
            .AddSingleton(new PaymentOperationsMode(paymentsDisabled))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(
            action, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        new StagingOmsTransitionGuardAttribute().OnActionExecuting(executing);
        return executing;
    }

    [Fact]
    public void Isolated_staging_with_explicit_opt_in_and_disabled_payments_can_transition()
    {
        var action = Execute(Config());
        Assert.Null(action.Result);
    }

    [Theory]
    [InlineData(false, "marketplaceerp-api-staging", "NeonIsolated", "Disabled", QaHost)]
    [InlineData(true, "marketplaceerp-api", "NeonIsolated", "Disabled", QaHost)]
    [InlineData(true, "marketplaceerp-api-staging", "Production", "Disabled", QaHost)]
    [InlineData(true, "marketplaceerp-api-staging", "NeonIsolated", "Mock", QaHost)]
    [InlineData(true, "marketplaceerp-api-staging", "NeonIsolated", "Disabled", "ep-other.c-15.us-east-1.aws.neon.tech")]
    public void Ineligible_targets_fail_closed(
        bool enabled, string serviceName, string target, string provider, string host)
    {
        var action = Execute(Config(enabled, serviceName, target, provider, host));
        var response = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(503, response.StatusCode);
        Assert.Equal("no-store", action.HttpContext.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void Prior_enabled_payments_mode_keeps_existing_controller_behavior()
    {
        var action = Execute(Config(enabled: false), paymentsDisabled: false);
        Assert.Null(action.Result);
    }
}
