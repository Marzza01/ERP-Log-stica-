using System.Net;
using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class DeploymentHttpSecurityTests
{
    [Theory]
    [InlineData("marketplaceerp-api-staging", true)]
    [InlineData("marketplaceerp-api", true)]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    public void Render_UsaTerminacionTlsDelProveedor(string? nombre, bool esperado) =>
        Assert.Equal(esperado, DeploymentHttpSecurity.RenderTerminatesTls(nombre));

    [Fact]
    public void Forwarding_NoConfiaEnInternetNiEnRedesAbiertas()
    {
        var options = DeploymentHttpSecurity.LocalTrustedForwarders();
        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownNetworks);
        Assert.Equal(2, options.KnownProxies.Count);
        Assert.Contains(IPAddress.Loopback, options.KnownProxies);
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
        Assert.DoesNotContain(IPAddress.Any, options.KnownProxies);
        Assert.DoesNotContain(IPAddress.IPv6Any, options.KnownProxies);
    }

    [Fact]
    public async Task ProxyDesconocido_NoPuedeFalsificarIpNiEsquema()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.50";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(DeploymentHttpSecurity.LocalTrustedForwarders()));
        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse("198.51.100.25"), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task ProxyLoopbackVerificado_SigueFuncionando()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.52";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(DeploymentHttpSecurity.LocalTrustedForwarders()));
        await middleware.Invoke(context);

        Assert.Equal(IPAddress.Parse("203.0.113.52"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public void HstsRender_SinIncluirSubdominiosYNoReemplazaPoliticaExistente()
    {
        var headers = new HeaderDictionary();
        DeploymentHttpSecurity.AddRenderHsts(headers);
        Assert.Equal("max-age=2592000", headers.StrictTransportSecurity.ToString());

        headers.StrictTransportSecurity = "max-age=3600";
        DeploymentHttpSecurity.AddRenderHsts(headers);
        Assert.Equal("max-age=3600", headers.StrictTransportSecurity.ToString());
    }
}
