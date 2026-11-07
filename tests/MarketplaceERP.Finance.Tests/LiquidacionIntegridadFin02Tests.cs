using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class LiquidacionIntegridadFin02Tests
{
    private static FinanceDbContext Db() => new(new DbContextOptionsBuilder<FinanceDbContext>()
        .UseInMemoryDatabase("fin02-" + Guid.NewGuid().ToString("N")).Options);

    private static LiquidacionService Service(FinanceDbContext db, bool disabled = false) =>
        new(db, new ContabilidadService(db, NullLogger<ContabilidadService>.Instance),
            NullLogger<LiquidacionService>.Instance, new PaymentOperationsMode(disabled));

    private static SplitPago NuevoSplit(Guid sellerId, decimal vendorAmount, int ordinal)
    {
        var orderId = Guid.NewGuid();
        var order = new Orden
        {
            Id = orderId,
            NumeroOrden = "FIN02-" + Guid.NewGuid().ToString("N"),
            CompradorId = Guid.NewGuid(),
            VendedorId = sellerId,
            SubTotal = vendorAmount,
            MontoTotal = vendorAmount,
            Estado = EstadoOrden.Entregada,
            TipoCliente = TipoCliente.B2C
        };
        return new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = orderId,
            Orden = order,
            MontoTotal = vendorAmount,
            MontoVendedor = vendorAmount,
            MontoMarketplace = 0m,
            TransaccionId = "SIM-" + Guid.NewGuid().ToString("N"),
            FechaProcesado = DateTime.UtcNow.AddMinutes(ordinal)
        };
    }

    [Fact]
    public async Task DosSplits_GeneranDosLiquidacionesCadaUnaConSuImporte()
    {
        await using var db = Db();
        var seller = Guid.NewGuid();
        var a = NuevoSplit(seller, 35m, 1);
        var b = NuevoSplit(seller, 80m, 2);
        db.SplitsPago.AddRange(a, b);
        await db.SaveChangesAsync();

        var service = Service(db);
        var first = await service.GenerarLiquidacionAsync(seller);
        var second = await service.GenerarLiquidacionAsync(seller);

        Assert.Equal(a.Id, first.SplitPagoId);
        Assert.Equal(35m, first.MontoLiquidar);
        Assert.Equal(b.Id, second.SplitPagoId);
        Assert.Equal(80m, second.MontoLiquidar);
        Assert.NotEqual(first.SplitPagoId, second.SplitPagoId);
        Assert.Equal(2, await db.Liquidaciones.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerarLiquidacionAsync(seller));
    }

    [Fact]
    public async Task LiquidacionHistoricaQueSumaVariosSplits_NoSePaga()
    {
        await using var db = Db();
        var seller = Guid.NewGuid();
        var split = NuevoSplit(seller, 25m, 0);
        db.SplitsPago.Add(split);
        var historical = new Liquidacion
        {
            Id = Guid.NewGuid(), VendedorId = seller,
            SplitPagoId = split.Id, MontoLiquidar = 100m,
            Estado = EstadoLiquidacion.Pendiente
        };
        db.Liquidaciones.Add(historical);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).ProcesarPagoAsync(historical.Id));

        Assert.Equal(EstadoLiquidacion.Pendiente,
            (await db.Liquidaciones.SingleAsync()).Estado);
        Assert.Empty(await db.PartidasContables.ToListAsync());
    }

    [Fact]
    public async Task PagoRepetido_NoGeneraSegundoAsiento_YNumeracionEsUnica()
    {
        await using var db = Db();
        var seller = Guid.NewGuid();
        db.SplitsPago.AddRange(NuevoSplit(seller, 25m, 0), NuevoSplit(seller, 75m, 1));
        await db.SaveChangesAsync();

        var service = Service(db);
        var first = await service.GenerarLiquidacionAsync(seller);
        var second = await service.GenerarLiquidacionAsync(seller);

        Assert.Equal(EstadoLiquidacion.Completada,
            (await service.ProcesarPagoAsync(first.Id)).Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcesarPagoAsync(first.Id));
        Assert.Equal(EstadoLiquidacion.Completada,
            (await service.ProcesarPagoAsync(second.Id)).Estado);

        var ledger = await db.PartidasContables.Include(p => p.Lineas).ToListAsync();
        Assert.Equal(2, ledger.Count);
        Assert.Equal(2, ledger.Select(p => p.NumeroPartida).Distinct().Count());
        Assert.All(ledger, p =>
        {
            Assert.StartsWith("LIQ-", p.NumeroPartida);
            Assert.Equal(p.TotalDebe, p.TotalHaber);
            Assert.Equal(2, p.Lineas.Count);
        });
        Assert.Equal(100m, ledger.Sum(p => p.TotalDebe));
    }

    [Fact]
    public async Task ModoDeshabilitado_BloqueaGeneracionYPago()
    {
        await using var db = Db();
        var seller = Guid.NewGuid();
        var service = Service(db, disabled: true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerarLiquidacionAsync(seller));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcesarPagoAsync(Guid.NewGuid()));
        Assert.Empty(await db.Liquidaciones.ToListAsync());
    }

    [Fact]
    public async Task LiquidacionVendedorErroneo_NoSePuedeContabilizar()
    {
        await using var db = Db();
        var seller = Guid.NewGuid();
        var split = NuevoSplit(seller, 40m, 0);
        db.SplitsPago.Add(split);
        var wrong = new Liquidacion
        {
            Id = Guid.NewGuid(), VendedorId = Guid.NewGuid(),
            SplitPagoId = split.Id, MontoLiquidar = split.MontoVendedor
        };
        db.Liquidaciones.Add(wrong);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db).ProcesarPagoAsync(wrong.Id));
        Assert.Empty(await db.PartidasContables.ToListAsync());
    }
}
