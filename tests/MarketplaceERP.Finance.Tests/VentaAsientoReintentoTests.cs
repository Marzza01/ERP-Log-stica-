using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class VentaAsientoReintentoTests
{
    private static FinanceDbContext Db() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("venta-journal-" + Guid.NewGuid()).Options);

    [Fact]
    public async Task MismoPedidoMismosMontos_RetornaElMismoAsientoSinDuplicar()
    {
        await using var db = Db();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var order = Guid.NewGuid();

        var first = await service.GenerarAsientoVentaAsync(order, 100m, 13m, "FacturaConsumidorFinal");
        var retry = await service.GenerarAsientoVentaAsync(order, 100m, 13m, "FacturaConsumidorFinal");

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await db.PartidasContables.CountAsync());
        Assert.Equal(3, await db.LineasPartidaContable.CountAsync());
    }

    [Fact]
    public async Task MismoPedidoMontosDistintos_RechazaSinCrearOtroAsiento()
    {
        await using var db = Db();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var order = Guid.NewGuid();

        await service.GenerarAsientoVentaAsync(order, 100m, 13m, "FacturaConsumidorFinal");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerarAsientoVentaAsync(order, 120m, 15.6m, "FacturaConsumidorFinal"));

        Assert.Equal(1, await db.PartidasContables.CountAsync());
    }
}
