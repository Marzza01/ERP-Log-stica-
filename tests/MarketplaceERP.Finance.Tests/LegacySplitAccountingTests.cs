using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

// F150: restore the persisted seller-liability split without copying the
// historical CheckoutOrchestrator fixture that mixes subtotal and grand total.
public sealed class LegacySplitAccountingTests
{
    [Fact]
    public Task G1_Subtotal100_Iva13_NoShipping_Seller90_Commission10() =>
        VerifyValidSplitAsync(100m, 13m, 0m, 113m, 90m, 10m);

    [Fact]
    public Task G2_Subtotal200_Iva26_Shipping5_Seller180_Commission20() =>
        VerifyValidSplitAsync(200m, 26m, 5m, 231m, 180m, 20m);

    private static async Task VerifyValidSplitAsync(
        decimal subtotal, decimal iva, decimal shipping, decimal grandTotal,
        decimal sellerAmount, decimal marketplaceCommission)
    {
        await using var db = new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase("f150-split-" + Guid.NewGuid().ToString("N")).Options);

        var order = new Orden
        {
            Id = Guid.NewGuid(),
            NumeroOrden = "F150-" + Guid.NewGuid().ToString("N"),
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = subtotal,
            ImpuestoIva = iva,
            CostoEnvio = shipping,
            MontoTotal = grandTotal,
            Estado = EstadoOrden.Aprobada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        var split = new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = order.Id,
            MontoTotal = subtotal,
            MontoVendedor = sellerAmount,
            MontoMarketplace = marketplaceCommission,
            PorcentajeVendedor = Math.Round(sellerAmount / subtotal * 100m, 2),
            PorcentajeMarketplace = Math.Round(marketplaceCommission / subtotal * 100m, 2),
            TransaccionId = "F150-" + Guid.NewGuid().ToString("N"),
            FechaProcesado = DateTime.UtcNow
        };
        order.SplitPago = split;
        db.Ordenes.Add(order);
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();

        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var entry = await service.GenerarAsientoVentaAsync(
            order.Id, order.SubTotal, order.ImpuestoIva, order.TipoComprobante);

        Assert.Equal(grandTotal, entry.TotalDebe);
        Assert.Equal(grandTotal, entry.TotalHaber);
        Assert.Equal(sellerAmount,
            entry.Lineas.Single(line => line.CodigoCuenta == "2108").Haber);
        Assert.Equal(marketplaceCommission + shipping,
            entry.Lineas.Single(line => line.CodigoCuenta == "4101").Haber);
        Assert.Equal(iva,
            entry.Lineas.Single(line => line.CodigoCuenta == "2105").Haber);

        var retry = await service.GenerarAsientoVentaAsync(
            order.Id, order.SubTotal, order.ImpuestoIva, order.TipoComprobante);
        Assert.Equal(entry.Id, retry.Id);
        Assert.Equal(1, await db.PartidasContables.CountAsync());

        // A retry with a changed seller/commission split must not silently reuse
        // the original journal entry.
        split.MontoVendedor += 1m;
        split.MontoMarketplace -= 1m;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerarAsientoVentaAsync(
                order.Id, order.SubTotal, order.ImpuestoIva, order.TipoComprobante));
        Assert.Equal(1, await db.PartidasContables.CountAsync());
    }

    [Fact]
    public async Task G3_InconsistentSplit_FailsClosedWithoutCreatingJournalEntry()
    {
        await using var db = new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase("f150-invalid-split-" + Guid.NewGuid().ToString("N")).Options);

        var order = new Orden
        {
            Id = Guid.NewGuid(),
            NumeroOrden = "F150-" + Guid.NewGuid().ToString("N"),
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = 100m,
            ImpuestoIva = 13m,
            CostoEnvio = 0m,
            MontoTotal = 113m,
            Estado = EstadoOrden.Aprobada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        var split = new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = order.Id,
            MontoTotal = 100m,
            MontoVendedor = 105m,
            MontoMarketplace = 0m,
            PorcentajeVendedor = 105m,
            PorcentajeMarketplace = 0m,
            TransaccionId = "F150-INVALID",
            FechaProcesado = DateTime.UtcNow
        };
        order.SplitPago = split;
        db.Ordenes.Add(order);
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();

        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerarAsientoVentaAsync(
                order.Id, order.SubTotal, order.ImpuestoIva, order.TipoComprobante));

        Assert.Empty(await db.PartidasContables.ToListAsync());
    }
}
