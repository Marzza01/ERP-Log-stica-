using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Render termina TLS en su balanceador y redirige HTTP externamente.
/// No se deben aceptar X-Forwarded-* de orígenes arbitrarios: la IP resultante
/// también se utiliza para limitar intentos de inicio de sesión.
/// </summary>
public static class DeploymentHttpSecurity
{
    // HSTS para el dominio HTTPS de la API; sin includeSubDomains ni preload.
    public const string RenderHsts = "max-age=2592000";

    public static bool RenderTerminatesTls(string? renderServiceName) =>
        !string.IsNullOrWhiteSpace(renderServiceName);

    /// <summary>
    /// Políticas de proxies para hosts que NO están en Render.
    /// Sólo el loopback está autorizado de forma predeterminada. Si otra
    /// plataforma necesita proxies adicionales, se deben registrar por IP/CIDR
    /// verificados en un cambio explícito, nunca confiar globalmente.
    /// </summary>
    public static ForwardedHeadersOptions LocalTrustedForwarders()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };

        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
        return options;
    }

    /// <summary>
    /// El navegador ve HTTPS en Render aunque el salto proxy→Kestrel sea HTTP.
    /// No sobrescribir políticas HSTS existentes agregadas por el proveedor.
    /// </summary>
    public static void AddRenderHsts(IHeaderDictionary headers) =>
        headers.TryAdd("Strict-Transport-Security", RenderHsts);
}
