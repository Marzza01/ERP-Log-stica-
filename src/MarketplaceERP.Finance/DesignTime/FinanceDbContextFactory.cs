using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MarketplaceERP.Finance.Data;

namespace MarketplaceERP.Finance.DesignTime;

/// <summary>
/// Builds FinanceDbContext for EF tooling without booting the production API,
/// reading JWT/payment secrets, or connecting to any database.
/// A connection is only opened if an operator explicitly runs database update.
/// </summary>
public sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MARKETPLACEERP_DESIGN_CONNECTION")
            ?? "Host=127.0.0.1;Port=5432;Database=marketplaceerp_design_time;Username=postgres;Password=not-used";

        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new FinanceDbContext(options);
    }
}
