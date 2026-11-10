using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Fail-closed target check for the dedicated Render staging service. This checks
/// configuration before EF opens a connection; Neon branch ownership is checked
/// separately against Neon metadata before deployment.
/// </summary>
public static class StagingDatabaseTargetGuard
{
    public const string ServiceName = "marketplaceerp-api-staging";
    public const string TargetMarker = "NeonTest";
    public const string DirectHost = "ep-jolly-breeze-bbw4v48n.c-15.us-east-1.aws.neon.tech";
    public const string DatabaseName = "marketplaceerp_test";
    public const string DatabaseUser = "marketplaceerp_test_owner";

    /// <summary>
    /// Every remotely mutating maintenance command is restricted to the exact
    /// authorized Neon staging endpoint, with the full Render staging identity.
    /// Loopback QA is allowed only without ANY staging marker.
    /// Production maintenance needs a separate, expressly reviewed release gate.
    /// DescribeTarget remains offline and is deliberately exempt.
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
        var markedStaging = string.Equals(configuration["Deployment:Target"], TargetMarker, StringComparison.Ordinal);
        var loopback = target.Host is "localhost" or "127.0.0.1" or "::1";
        if (loopback && !namedStaging && !markedStaging)
            return; // Explicitly confirmed localhost-only QA, never a remote service.

        if (!namedStaging || !markedStaging ||
            !string.Equals(target.Host, DirectHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Mantenimiento remoto no autorizado: únicamente Neon staging con identidad completa.");

        // Inherit the existing strict host, port, DB, role, TLS, payment and
        // single-source connection requirements from HTTP staging.
        Validate(configuration, renderServiceName, environmentName, resolvedConnection);
    }

    public static void Validate(IConfiguration configuration, string? renderServiceName,
        string environmentName, string? resolvedConnection)
    {
        var namedStaging = string.Equals(renderServiceName, ServiceName, StringComparison.Ordinal);
        var markedStaging = string.Equals(configuration["Deployment:Target"], TargetMarker, StringComparison.Ordinal);
        if (!namedStaging && !markedStaging) return;

        if (!namedStaging || !markedStaging ||
            !string.Equals(environmentName, "Production", StringComparison.Ordinal) ||
            !string.Equals(configuration["Payments:Provider"], "Disabled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La identidad o el modo seguro del servicio de ensayo no coincide.");

        // Require one explicit source. A stale alias must not silently redirect staging.
        if (string.IsNullOrWhiteSpace(configuration["CONNECTION_STRING"]) ||
            !string.IsNullOrWhiteSpace(configuration["DATABASE_URL"]) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            throw new InvalidOperationException("La conexión de ensayo debe tener una sola fuente explícita.");

        NpgsqlConnectionStringBuilder connection;
        try { connection = new NpgsqlConnectionStringBuilder(resolvedConnection); }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("La conexión de ensayo no es válida.");
        }

        if (!string.Equals(connection.Host, DirectHost, StringComparison.OrdinalIgnoreCase) ||
            connection.Port != 5432 ||
            !string.Equals(connection.Database, DatabaseName, StringComparison.Ordinal) ||
            !string.Equals(connection.Username, DatabaseUser, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(connection.Password) ||
            connection.SslMode != SslMode.VerifyFull ||
            !string.Equals(connection.ChannelBinding.ToString(), "Require", StringComparison.Ordinal))
            throw new InvalidOperationException("El destino o la protección TLS de ensayo no coincide.");
    }
}
