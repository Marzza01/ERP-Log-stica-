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
    public async Task DosSplits_GeneranUnaLiquidacionAgrupadaSinReclamarDosVeces()
    {
        await using var db = Db();
        var seller = Guid.NewGuid();
        var a = NuevoSplit(seller, 35m, 1);
        var b = NuevoSplit(seller, 80m, 2);
        db.SplitsPago.AddRange(a, b);
        await db.SaveChangesAsync();

        var service = Service(db);
        var group = await service.GenerarLiquidacionAsync(seller);

        Assert.Equal(a.Id, group.SplitPagoId);
        Assert.Equal(115m, group.MontoLiquidar);
        Assert.Equal(group.Id, a.LiquidacionId);
        Assert.Equal(group.Id, b.LiquidacionId);
        Assert.Equal(1, await db.Liquidaciones.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerarLiquidacionAsync(seller));
    }

    // F151: historical checkout contract — no seller liquidation before delivery.
    [Fact]
    public async Task OrdenNoEntregada_NoGeneraLiquidacion_HastaConfirmarEntrega()
    {
        await using var db = Db();
        var seller = Guid.NewGuid();
        var split = NuevoSplit(seller, 90m, 1);
        split.Orden.Estado = EstadoOrden.Aprobada;
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();

        var service = Service(db);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerarLiquidacionAsync(seller));

        Assert.Empty(await db.Liquidaciones.ToListAsync());
        Assert.Null(split.LiquidacionId);

        split.Orden.Estado = EstadoOrden.Entregada;
        await db.SaveChangesAsync();

        var liquidation = await service.GenerarLiquidacionAsync(seller);
        Assert.Equal(90m, liquidation.MontoLiquidar);
        Assert.Equal(split.Id, liquidation.SplitPagoId);
        Assert.Equal(liquidation.Id, split.LiquidacionId);
        Assert.Single(await db.Liquidaciones.ToListAsync());
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
        var group = await service.GenerarLiquidacionAsync(seller);
        Assert.Equal(100m, group.MontoLiquidar);
        Assert.Equal(EstadoLiquidacion.Completada,
            (await service.ProcesarPagoAsync(group.Id)).Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcesarPagoAsync(group.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerarLiquidacionAsync(seller));

        var ledger = await db.PartidasContables.Include(p => p.Lineas).ToListAsync();
        var entry = Assert.Single(ledger);
        Assert.StartsWith("LIQ-", entry.NumeroPartida);
        Assert.Equal(entry.TotalDebe, entry.TotalHaber);
        Assert.Equal(2, entry.Lineas.Count);
        Assert.Equal(100m, entry.TotalDebe);
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
