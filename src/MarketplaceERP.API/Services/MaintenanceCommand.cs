using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace MarketplaceERP.API.Services;

public enum MaintenanceAction { Http, DescribeTarget, Migrate, BootstrapAdmin, SeedDemo }

public sealed record MaintenanceCommand(MaintenanceAction Action, string? Host = null,
    int? Port = null, string? Database = null, string? Fingerprint = null)
{
    public static MaintenanceCommand Parse(string[] args)
    {
        if (args.SequenceEqual(new[] { "--describe-maintenance-target" }))
            return new(MaintenanceAction.DescribeTarget);
        if (args.Length == 5 && args[0].StartsWith("--maintenance=", StringComparison.Ordinal) &&
            args[1].StartsWith("--expected-host=", StringComparison.Ordinal) &&
            args[2].StartsWith("--expected-port=", StringComparison.Ordinal) &&
            args[3].StartsWith("--expected-database=", StringComparison.Ordinal) &&
            args[4].StartsWith("--confirm-target=", StringComparison.Ordinal))
        {
            var action = args[0]["--maintenance=".Length..] switch
            {
                "migrate" => MaintenanceAction.Migrate,
                "bootstrap-admin" => MaintenanceAction.BootstrapAdmin,
                "seed-demo" => MaintenanceAction.SeedDemo,
                _ => throw new ArgumentException("Acción de mantenimiento no reconocida.")
            };
            var host = args[1]["--expected-host=".Length..];
            var database = args[3]["--expected-database=".Length..];
            var fingerprint = args[4]["--confirm-target=".Length..];
            if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(database) &&
                int.TryParse(args[2]["--expected-port=".Length..], out var port) && port is >= 1 and <= 65535 &&
                fingerprint.Length == 64 && fingerprint.All(Uri.IsHexDigit))
                return new(action, host, port, database, fingerprint);
        }
        if (args.Any(a => new[] { "maintenance", "describe-maintenance-target", "expected-", "confirm-", "migrate", "bootstrap", "seed", "diagnose" }
                .Any(word => a.TrimStart('-').StartsWith(word, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Comando de mantenimiento inválido o sin confirmación completa.");
        return new(MaintenanceAction.Http);
    }

    public static async Task<bool> RunMaintenanceIfRequestedAsync(MaintenanceCommand command, Func<Task> run)
    {
        if (command.Action == MaintenanceAction.Http) return false;
        await run();
        return true; // Caller must return; HTTP and maintenance never execute together.
    }
}

public sealed record MaintenanceTarget(string Host, int Port, string Database, string Fingerprint)
{
    // Offline configuration identity, not proof of a physical server. Never hashes a password.
    public static MaintenanceTarget Describe(string? connectionString)
    {
        NpgsqlConnectionStringBuilder c;
        try { c = new(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("Conexión de mantenimiento inválida."); }
        if (string.IsNullOrWhiteSpace(c.Host) || c.Host.Contains(',') || string.IsNullOrWhiteSpace(c.Database) ||
            string.IsNullOrWhiteSpace(c.Username))
            throw new InvalidOperationException("Mantenimiento requiere host único, puerto, base y usuario explícitos.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            foreach (var value in new[] { "logimkt:maintenance:u4", c.Host, c.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), c.Database, c.Username })
                writer.Write(value);
        return new(c.Host, c.Port, c.Database, Convert.ToHexString(SHA256.HashData(stream.ToArray())));
    }

    public void Confirm(MaintenanceCommand command)
    {
        if (command.Host != Host || command.Port != Port || command.Database != Database ||
            command.Fingerprint is null || command.Fingerprint.Length != 64 || !command.Fingerprint.All(Uri.IsHexDigit) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(command.Fingerprint), Convert.FromHexString(Fingerprint)))
            throw new InvalidOperationException("El destino no coincide con la confirmación explícita.");
    }
}
