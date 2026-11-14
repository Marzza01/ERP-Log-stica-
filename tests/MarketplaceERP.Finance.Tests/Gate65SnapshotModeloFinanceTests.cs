using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Comprueba que la metadata EF de producto coincide con el snapshot,
/// sin consultar ni abrir base de datos: no se usa Neon.
/// </summary>
public sealed class Gate65SnapshotModeloFinanceTests
{
    [Fact]
    public void ModeloDeProducto_CoincideConSnapshot_SinOperacionesEnBd()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(
                "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=NO_USAR",
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
            .Options;
        using var context = new FinanceDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges(),
            "Snapshot Finance no coincide con el modelo EF actual: impedir publicar.");
    }
}
