using MarketplaceERP.API.Services;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class StagingDatabaseTargetGuardTests
{
    private const string IsolatedTestHost =
        "ep-isolated-test-example.c-15.us-east-1.aws.neon.tech";
    private const string SharedProductionHost =
        "ep-jolly-breeze-bbw4v48n.c-15.us-east-1.aws.neon.tech";

    private static IConfiguration Configuration(string? connection = null,
        string? marker = StagingDatabaseTargetGuard.TargetMarker,
        string? otherUrl = null, string? approvedHost = IsolatedTestHost) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CONNECTION_STRING"] = connection ?? Connection(),
            ["DATABASE_URL"] = otherUrl,
            ["Deployment:Target"] = marker,
            ["Deployment:ApprovedStagingHost"] = approvedHost,
            ["Payments:Provider"] = "Disabled"
        }).Build();

    private static string Connection(string? host = null, string? database = null, string? user = null,
        SslMode ssl = SslMode.VerifyFull, ChannelBinding binding = ChannelBinding.Require,
        bool withPassword = true) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host ?? IsolatedTestHost,
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
    [InlineData(SharedProductionHost)]
    [InlineData("ep-delicate-salad-bb7xy92n.c-15.us-east-1.aws.neon.tech")]
    [InlineData("ep-calm-surf-bbm5guwf.c-15.us-east-1.aws.neon.tech")]
    [InlineData("ep-jolly-breeze-bbw4v48n-pooler.c-15.us-east-1.aws.neon.tech")]
    [InlineData("localhost")]
    public void OtherHosts_AreRejectedBeforeAnyConnection(string host) =>
        Assert.Throws<InvalidOperationException>(() => Check(Configuration(Connection(host: host))));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("ep-isolated-test-example-pooler.c-15.us-east-1.aws.neon.tech")]
    [InlineData("ep-isolated-test-example.c-20.us-east-1.aws.neon.tech")]
    [InlineData(SharedProductionHost)]
    public void MissingUnsafeOrSharedApprovedHost_FailsClosed(string? host) =>
        Assert.Throws<InvalidOperationException>(() =>
            Check(Configuration(approvedHost: host)));

    [Fact]
    public void SharedProductionEndpointRejectedEvenWhenConfigurationApprovesIt()
    {
        var connection = Connection(host: SharedProductionHost);
        var config = Configuration(connection, approvedHost: SharedProductionHost);
        Assert.Throws<InvalidOperationException>(() => Check(config));
        Assert.Throws<InvalidOperationException>(() => CheckMaintenance(config));
    }

    [Fact]
    public void LegacyStagingMarkerRejectedInsteadOfSilentlyReusingProductionHost()
    {
        var legacy = Configuration(marker: "NeonTest");
        Assert.Throws<InvalidOperationException>(() => Check(legacy));
        Assert.Throws<InvalidOperationException>(() => CheckMaintenance(legacy));
    }

    [Fact]
    public void IndependentHostNeedsMatchingExplicitApprovedHost()
    {
        var connection = Connection();
        Assert.Throws<InvalidOperationException>(() =>
            Check(Configuration(connection, approvedHost:
                "ep-some-other-endpoint.c-15.us-east-1.aws.neon.tech")));
    }

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

    private static void CheckMaintenance(
        IConfiguration config, string? service = StagingDatabaseTargetGuard.ServiceName,
        string environment = "Production") =>
        StagingDatabaseTargetGuard.ValidateMaintenanceWriteTarget(
            config, service, environment, DatabaseConnectionConfiguration.Resolve(config));

    [Fact]
    public void Maintenance_ExactStagingIdentityAndTls_PassesWithoutDatabaseConnection() =>
        CheckMaintenance(Configuration());

    [Fact]
    public void Maintenance_LocalPostgresQaWithoutStagingIdentity_IsAllowed()
    {
        var local = Connection(host: "localhost", database: "gate65_qa_local", user: "postgres",
            ssl: SslMode.Disable, binding: ChannelBinding.Disable);
        CheckMaintenance(Configuration(local, marker: null), service: null, environment: "Development");
        CheckMaintenance(Configuration(local.Replace("localhost", "127.0.0.1"), marker: null),
            service: null, environment: "Development");
    }

    [Fact]
    public void Maintenance_ProductionRemoteHostIsRejectedWithoutStagingIdentity()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CheckMaintenance(Configuration(Connection(host: "production-db.invalid", database: "real_prod"),
                marker: null), service: "marketplaceerp-api"));
    }

    [Fact]
    public void Maintenance_StagingHostWithoutBothMarkersIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CheckMaintenance(Configuration(marker: null), service: null));
        Assert.Throws<InvalidOperationException>(() =>
            CheckMaintenance(Configuration(), service: null));
        Assert.Throws<InvalidOperationException>(() =>
            CheckMaintenance(Configuration(marker: null)));
    }

    [Fact]
    public void Maintenance_LocalhostWithStagingMarkersIsRejected()
    {
        var local = Connection(host: "localhost", database: "gate65_qa_local", user: "postgres");
        Assert.Throws<InvalidOperationException>(() =>
            CheckMaintenance(Configuration(local)));
        Assert.Throws<InvalidOperationException>(() =>
            CheckMaintenance(Configuration(local, marker: null)));
    }

    [Fact]
    public void Maintenance_StagingRequiresExactDatabaseRoleTlsAndPaymentMode()
    {
        foreach (var invalid in new[]
        {
            Connection(database: "wrong"),
            Connection(user: "wrong"),
            Connection(ssl: SslMode.Require),
            Connection(binding: ChannelBinding.Prefer)
        })
        {
            Assert.Throws<InvalidOperationException>(() =>
                CheckMaintenance(Configuration(invalid)));
        }
        Assert.Throws<InvalidOperationException>(() =>
            CheckMaintenance(Configuration(), environment: "Development"));
    }
}
