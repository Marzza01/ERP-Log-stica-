using MarketplaceERP.API.Services;
using MarketplaceERP.Core;
using MarketplaceERP.Catalog;
using MarketplaceERP.Finance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class ArranqueSeguroU4Tests
{
    private const string SyntheticConnection = "Host=configuration-test.invalid;Port=65001;Database=u4_test;Username=u4_user";

    private static string[] Command(string action) => new[]
    {
        "--maintenance=" + action, "--expected-host=configuration-test.invalid", "--expected-port=65001",
        "--expected-database=u4_test", "--confirm-target=" + MaintenanceTarget.Describe(SyntheticConnection).Fingerprint
    };

    [Fact]
    public async Task HttpPath_NeverInvokesMaintenanceDelegate()
    {
        var calls = 0;
        var maintenance = await MaintenanceCommand.RunMaintenanceIfRequestedAsync(
            MaintenanceCommand.Parse(Array.Empty<string>()), () => { calls++; throw new Exception("Must not run"); });
        Assert.False(maintenance);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("bootstrap-admin")]
    [InlineData("seed-demo")]
    public async Task ExplicitCommand_ExecutesOnlyMaintenanceBranch(string action)
    {
        var command = MaintenanceCommand.Parse(Command(action));
        var writes = 0; var http = 0;
        if (!await MaintenanceCommand.RunMaintenanceIfRequestedAsync(command, () => { writes++; return Task.CompletedTask; }))
            http++;
        Assert.Equal(1, writes); Assert.Equal(0, http);
        MaintenanceTarget.Describe(SyntheticConnection).Confirm(command);
    }

    [Fact]
    public async Task MaintenanceFailure_CannotFallThroughToHttp()
    {
        var http = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (!await MaintenanceCommand.RunMaintenanceIfRequestedAsync(MaintenanceCommand.Parse(Command("migrate")),
                    () => throw new InvalidOperationException("synthetic failure"))) http++;
        });
        Assert.Equal(0, http);
    }

    [Theory]
    [InlineData("--migrate")]
    [InlineData("--bootstrap-admin")]
    [InlineData("--maintenance=migrate")]
    [InlineData("--maintenance=unknown")]
    [InlineData("--Maintenance=migrate")]
    [InlineData("--seed-demo")]
    [InlineData("--describe-maintenance-target=1")]
    [InlineData("--expected-database=u4_test")]
    public void IncompleteOrAmbiguousMaintenance_IsRejected(string value) =>
        Assert.Throws<ArgumentException>(() => MaintenanceCommand.Parse(new[] { value }));

    [Fact]
    public void MixedHttpAndMaintenanceArguments_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => MaintenanceCommand.Parse(Command("migrate").Concat(new[] { "--urls=http://127.0.0.1:65002" }).ToArray()));
        Assert.Equal(MaintenanceAction.Http, MaintenanceCommand.Parse(new[] { "--urls=http://127.0.0.1:65002" }).Action);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void NormalModuleRegistration_WithSeedFlags_DoesNotRegisterBackgroundMaintenance(string environment)
    {
        // Configuration only, no host/provider constructed and no DbContext resolved.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = environment, ["Seed:DemoData"] = "true",
            ["Startup:ApplyMigrations"] = "true", ["Maintenance:Enabled"] = "true",
            ["ConnectionStrings:DefaultConnection"] = SyntheticConnection,
            ["Payments:Provider"] = "Mock", ["Payments:EnableMock"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddCoreServices(configuration); services.AddCatalogServices(configuration);
        // Production is intentionally still blocked by Finance's payment guard; never bypass it.
        if (environment == "Production")
            Assert.Throws<InvalidOperationException>(() => services.AddFinanceServices(configuration, false));
        else services.AddFinanceServices(configuration, true);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        Assert.Equal(MaintenanceAction.Http, MaintenanceCommand.Parse(Array.Empty<string>()).Action);
    }

    [Fact]
    public void TargetConfirmation_RejectsEachMismatchedComponent()
    {
        var target = MaintenanceTarget.Describe(SyntheticConnection);
        var command = MaintenanceCommand.Parse(Command("migrate"));
        Assert.Throws<InvalidOperationException>(() => target.Confirm(command with { Host = "other.invalid" }));
        Assert.Throws<InvalidOperationException>(() => target.Confirm(command with { Port = 65003 }));
        Assert.Throws<InvalidOperationException>(() => target.Confirm(command with { Database = "other" }));
        Assert.Throws<InvalidOperationException>(() => target.Confirm(command with { Fingerprint = new string('0', 64) }));
        Assert.Throws<InvalidOperationException>(() => target.Confirm(command with { Fingerprint = "invalid" }));
        Assert.NotEqual(target.Fingerprint, MaintenanceTarget.Describe(SyntheticConnection.Replace("u4_user", "other_user")).Fingerprint);
    }

    [Fact]
    public void Description_IsOfflineAndDoesNotHashPassword()
    {
        Assert.Equal(MaintenanceAction.DescribeTarget, MaintenanceCommand.Parse(new[] { "--describe-maintenance-target" }).Action);
        var target = MaintenanceTarget.Describe(SyntheticConnection);
        Assert.Equal(target.Fingerprint, MaintenanceTarget.Describe(SyntheticConnection + ";Password=" + Guid.NewGuid().ToString("N")).Fingerprint);
        Assert.Throws<InvalidOperationException>(() => MaintenanceTarget.Describe(SyntheticConnection.Replace("configuration-test.invalid", "a.invalid,b.invalid")));
    }

    [Fact]
    public void ProductionSource_SelectsMaintenanceBeforeWebBuilder_AndHasNoHttpInitializer()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "MarketplaceERP.API", "Program.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "MarketplaceERP.API", "Program.cs"));
        Assert.True(source.IndexOf("RunMaintenanceIfRequestedAsync", StringComparison.Ordinal) <
                    source.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal));
        Assert.Contains("    return;", source);
        Assert.DoesNotContain("StartupInitializer", source);
        foreach (var forbidden in new[] { "MigrateAsync", "EnsureCreated", "SaveChanges", "SeedDemoData", "CrearAdminInicial" })
            Assert.DoesNotContain(forbidden, source);
        Assert.Contains("builder.Services.AddMarketplaceSecurity(jwtSettings);", source);
        Assert.Contains("app.UseAuthentication();", source);
        Assert.Contains("app.UseAuthorization();", source);
        // Source contract only; it is not a runtime HTTP or database test.
    }
}
