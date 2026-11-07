using Microsoft.Extensions.Configuration;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

// Only configuration providers and source files are read. No host, EF or database is created.
public class ConfiguracionSeguraU1Tests
{
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "MarketplaceERP.API", "Program.cs")))
                return directory.FullName;
        }

        throw new InvalidOperationException("No se encontró la raíz del repositorio para las pruebas U1.");
    }

    private static ConfigurationRoot JsonConfiguration(string? environment)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(RepositoryRoot(), "src", "MarketplaceERP.API"))
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        if (environment is not null)
            builder.AddJsonFile($"appsettings.{environment}.json", optional: false, reloadOnChange: false);
        return (ConfigurationRoot)builder.Build();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Development")]
    public void VersionedJson_HasNoOperationalConnectionOrSigningKey(string? environment)
    {
        using var configuration = JsonConfiguration(environment);
        Assert.Equal(string.Empty, configuration.GetConnectionString("DefaultConnection"));
        Assert.Equal(string.Empty, configuration["Jwt:Key"]);
        Assert.False(string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]));
        Assert.False(string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Development")]
    public void CanonicalEnvironmentKeys_OverrideJsonWithoutStartingApplication(string? environment)
    {
        // Unique prefix avoids reading or changing the developer's real connection/JWT variables.
        var prefix = $"LOGIMKT_U1_TEST_{Guid.NewGuid():N}_";
        var connectionName = prefix + "ConnectionStrings__DefaultConnection";
        var jwtName = prefix + "Jwt__Key";
        const string syntheticConnection = "Host=configuration-test.invalid;Database=not_used;Username=not_used";
        var syntheticKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(connectionName, syntheticConnection);
            Environment.SetEnvironmentVariable(jwtName, syntheticKey);
            using var json = JsonConfiguration(environment);
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
                .AddConfiguration(json)
                .AddEnvironmentVariables(prefix)
                .Build();

            Assert.Equal(syntheticConnection, configuration.GetConnectionString("DefaultConnection"));
            Assert.Equal(syntheticKey, configuration["Jwt:Key"]);
            Assert.Equal(json["Jwt:Issuer"], configuration["Jwt:Issuer"]);
            Assert.Equal(json["Jwt:Audience"], configuration["Jwt:Audience"]);
            Assert.Equal(json["Jwt:ExpirationMinutes"], configuration["Jwt:ExpirationMinutes"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionName, null);
            Environment.SetEnvironmentVariable(jwtName, null);
        }
    }

    [Fact]
    public void Template_HasEmptySecretFieldsAndDocumentsCanonicalVariables()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), ".env.example"));
        foreach (var key in new[] { "POSTGRES_PASSWORD", "JWT_KEY", "ADMIN_PASSWORD", "CONNECTION_STRING",
                     "ConnectionStrings__DefaultConnection", "Jwt__Key" })
        {
            var assignment = Assert.Single(lines, line => line.StartsWith(key + "=", StringComparison.Ordinal));
            Assert.Equal(key + "=", assignment.Trim());
        }
    }

    [Fact]
    public void ComposeSource_RequiresExternalSecretsInsteadOfLiteralFallbacks()
    {
        // Static contract only: Docker interpolation and containers are not executed by this test.
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docker-compose.yml"));
        var password = Assert.Single(lines, line => line.TrimStart().StartsWith("POSTGRES_PASSWORD:", StringComparison.Ordinal));
        var jwt = Assert.Single(lines, line => line.TrimStart().StartsWith("Jwt__Key:", StringComparison.Ordinal));
        var connection = Assert.Single(lines, line => line.TrimStart().StartsWith("ConnectionStrings__DefaultConnection:", StringComparison.Ordinal));
        Assert.Contains("${POSTGRES_PASSWORD:?", password);
        Assert.Contains("${JWT_KEY:?", jwt);
        Assert.Contains("${CONNECTION_STRING:?", connection);
        Assert.DoesNotContain("${POSTGRES_PASSWORD:-", password);
        Assert.DoesNotContain("${JWT_KEY:-", jwt);
        Assert.DoesNotContain("${CONNECTION_STRING:-", connection);
    }
}
