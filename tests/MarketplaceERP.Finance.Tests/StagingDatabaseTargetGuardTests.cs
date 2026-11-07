using MarketplaceERP.API.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class StagingDatabaseTargetGuardTests
{
    private static IConfiguration Configuration(string? connection = null, string? marker = "NeonTest",
        string? otherUrl = null) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CONNECTION_STRING"] = connection ?? Connection(),
        ["DATABASE_URL"] = otherUrl,
        ["Deployment:Target"] = marker,
        ["Payments:Provider"] = "Disabled"
    }).Build();

    private static string Connection(string? host = null, string? database = null, string? user = null,
        SslMode ssl = SslMode.VerifyFull, ChannelBinding binding = ChannelBinding.Require,
        bool withPassword = true) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host ?? StagingDatabaseTargetGuard.DirectHost,
            Port = 5432,
            Database = database ?? StagingDatabaseTargetGuard.DatabaseName,
            Username = user ?? StagingDatabaseTargetGuard.DatabaseUser,
            Password = withPassword ? new string('x', 12) : string.Empty, // synthetic test input
            SslMode = ssl,
            ChannelBinding = binding
        }.ConnectionString;

    private static void Check(IConfiguration config, string? name = StagingDatabaseTargetGuard.ServiceName,
        string environment = "Production") =>
        StagingDatabaseTargetGuard.Validate(config, name, environment,
            DatabaseConnectionConfiguration.Resolve(config));

    [Fact]
    public void ExactStagingIdentityAndVerifiedTls_PassWithoutOpeningDatabase() => Check(Configuration());

    [Theory]
    [InlineData("ep-delicate-salad-bb7xy92n.c-15.us-east-1.aws.neon.tech")]
    [InlineData("ep-calm-surf-bbm5guwf.c-15.us-east-1.aws.neon.tech")]
    [InlineData("ep-jolly-breeze-bbw4v48n-pooler.c-15.us-east-1.aws.neon.tech")]
    [InlineData("localhost")]
    public void OtherHosts_AreRejectedBeforeAnyConnection(string host) =>
        Assert.Throws<InvalidOperationException>(() => Check(Configuration(Connection(host: host))));

    [Fact]
    public void WrongDatabaseUserOrTls_AreRejected()
    {
        foreach (var connection in new[]
        {
            Connection(database: "other"), Connection(user: "other"),
            Connection(ssl: SslMode.Require), Connection(binding: ChannelBinding.Prefer),
            Connection(withPassword: false)
        })
            Assert.Throws<InvalidOperationException>(() => Check(Configuration(connection)));
    }

    [Fact]
    public void MissingMarkerWrongServiceOrEnvironment_AreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Check(Configuration(marker: null)));
        Assert.Throws<InvalidOperationException>(() => Check(Configuration(), "marketplaceerp-api"));
        Assert.Throws<InvalidOperationException>(() => Check(Configuration(), environment: "Development"));
    }

    [Fact]
    public void ConflictingConnectionSources_AreRejected() =>
        Assert.Throws<InvalidOperationException>(() =>
            Check(Configuration(otherUrl: "postgresql://example.invalid/other")));

    [Fact]
    public void ExistingProductionServiceWithoutStagingMarker_IsUnchanged() =>
        Check(Configuration(Connection(host: "other.invalid"), marker: null), "marketplaceerp-api");
}
