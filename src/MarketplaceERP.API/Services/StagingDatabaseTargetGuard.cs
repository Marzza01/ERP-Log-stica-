using System.Text.RegularExpressions;
using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Fail-closed para Render STAGING en un endpoint Neon aislado.
/// El host histórico queda bloqueado porque producción y staging reportaron
/// ese MISMO endpoint. Este guard no altera la configuración de producción.
/// No basta con aprobar el host por configuración: ANTES del despliegue debe
/// verificarse independientemente la asociación endpoint -> rama en Neon.
/// </summary>
public static class StagingDatabaseTargetGuard
{
    public const string ServiceName = "marketplaceerp-api-staging";
    public const string TargetMarker = "NeonIsolated";
    public const string LegacySharedProductionHost =
        "ep-jolly-breeze-bbw4v48n.c-15.us-east-1.aws.neon.tech";
    public const string DatabaseName = "marketplaceerp_test";
    public const string DatabaseUser = "marketplaceerp_test_owner";
    public const string ApprovedHostConfiguration = "Deployment:ApprovedStagingHost";

    // Rechazar pooler y hosts que no son Neon direct endpoints de la región
    // explícitamente verificada. Configuración arbitraria NO cambia este límite.
    private static readonly Regex DirectEndpointPattern = new(
        @"\Aep-[a-z0-9-]{6,100}\.c-15\.us-east-1\.aws\.neon\.tech\z",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));

    private static bool EsIdentidadStaging(
        IConfiguration configuration, string? renderServiceName) =>
        string.Equals(renderServiceName, ServiceName, StringComparison.Ordinal) &&
        string.Equals(configuration["Deployment:Target"], TargetMarker, StringComparison.Ordinal);

    /// <summary>
    /// HTTP y mantenimiento deben apuntar al MISMO destino aislado. Este
    /// guard inspecciona configuración sin abrir PostgreSQL.
    /// </summary>
    public static void Validate(
        IConfiguration configuration,
        string? renderServiceName,
        string environmentName,
        string? resolvedConnection)
    {
        var namedStaging = string.Equals(renderServiceName, ServiceName, StringComparison.Ordinal);
        var markedStaging = string.Equals(configuration["Deployment:Target"], TargetMarker, StringComparison.Ordinal);
        // Un marcador histórico no puede habilitar staging accidentalmente.
        var legacyMarker = string.Equals(configuration["Deployment:Target"], "NeonTest", StringComparison.Ordinal);
        if (!namedStaging && !markedStaging && !legacyMarker)
            return; // Otras identidades (incluida producción) no cambian.

        if (!EsIdentidadStaging(configuration, renderServiceName) ||
            !string.Equals(environmentName, "Production", StringComparison.Ordinal) ||
            !string.Equals(configuration["Payments:Provider"], "Disabled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Identidad o modo de STAGING aislado no autorizado.");

        if (string.IsNullOrWhiteSpace(configuration["CONNECTION_STRING"]) ||
            !string.IsNullOrWhiteSpace(configuration["DATABASE_URL"]) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            throw new InvalidOperationException("Staging requiere una sola conexión explícita.");

        var approved = configuration[ApprovedHostConfiguration]?.Trim();
        if (string.IsNullOrWhiteSpace(approved) ||
            !DirectEndpointPattern.IsMatch(approved) ||
            approved.Contains("-pooler.", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(approved, LegacySharedProductionHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host aprobado no es un endpoint aislado válido.");

        NpgsqlConnectionStringBuilder connection;
        try { connection = new NpgsqlConnectionStringBuilder(resolvedConnection); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("Conexión de staging inválida.");
        }

        if (string.IsNullOrWhiteSpace(connection.Host) ||
            !string.Equals(connection.Host, approved, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.Host, LegacySharedProductionHost, StringComparison.OrdinalIgnoreCase) ||
            connection.Host.Contains("-pooler.", StringComparison.OrdinalIgnoreCase) ||
            connection.Port != 5432 ||
            !string.Equals(connection.Database, DatabaseName, StringComparison.Ordinal) ||
            !string.Equals(connection.Username, DatabaseUser, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(connection.Password) ||
            connection.SslMode != SslMode.VerifyFull ||
            !string.Equals(connection.ChannelBinding.ToString(), "Require", StringComparison.Ordinal))
            throw new InvalidOperationException("Conexión de STAGING no coincide con destino aislado y TLS verificado.");
    }

    /// <summary>
    /// Las operaciones de mantenimiento remoto solo pueden usar el endpoint
    /// aislado de STAGING identificado completamente; no permiten PROD.
    /// localhost se admite únicamente para ensayos locales sin ningún marcador.
    /// </summary>
    public static void ValidateMaintenanceWriteTarget(
        IConfiguration configuration, string? renderServiceName,
        string environmentName, string? resolvedConnection)
    {
        NpgsqlConnectionStringBuilder target;
        try { target = new NpgsqlConnectionStringBuilder(resolvedConnection); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("El destino de mantenimiento es inválido.");
        }
        var namedStaging = string.Equals(renderServiceName, ServiceName, StringComparison.Ordinal);
        var marker = configuration["Deployment:Target"];
        var markedStaging = string.Equals(marker, TargetMarker, StringComparison.Ordinal);
        var legacyMarker = string.Equals(marker, "NeonTest", StringComparison.Ordinal);
        var local = target.Host is "localhost" or "127.0.0.1" or "::1";

        if (local && !namedStaging && !markedStaging && !legacyMarker)
            return;

        if (!namedStaging || !markedStaging || legacyMarker)
            throw new InvalidOperationException("Mantenimiento remoto bloqueado fuera de STAGING aislado.");

        Validate(configuration, renderServiceName, environmentName, resolvedConnection);
    }
}
