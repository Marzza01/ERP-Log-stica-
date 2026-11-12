using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>Resolución única para HTTP y mantenimiento; nunca registra la conexión.</summary>
public static class DatabaseConnectionConfiguration
{
    public static string? Resolve(IConfiguration configuration)
    {
        var explicitConnection = configuration["CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(explicitConnection)) return explicitConnection;

        var databaseUrl = configuration["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(databaseUrl))
            return configuration.GetConnectionString("DefaultConnection");

        try
        {
            if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "postgres" && uri.Scheme != "postgresql") ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new FormatException();

            var credentials = uri.UserInfo.Split(':', 2);
            var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            var port = uri.Port == -1 ? 5432 : uri.Port;
            if (credentials.Length != 2 || string.IsNullOrEmpty(credentials[0]) ||
                string.IsNullOrEmpty(credentials[1]) || string.IsNullOrWhiteSpace(database) ||
                database.Contains('/') || port is < 1 or > 65535)
                throw new FormatException();

            return new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = port,
                Database = database,
                Username = Uri.UnescapeDataString(credentials[0]),
                Password = Uri.UnescapeDataString(credentials[1]),
                SslMode = SslMode.Require
            }.ConnectionString;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or UriFormatException)
        {
            // No adjuntar la excepción: puede contener el URL con credenciales.
            throw new InvalidOperationException("DATABASE_URL inválida; revise el formato de la conexión.");
        }
    }
}
