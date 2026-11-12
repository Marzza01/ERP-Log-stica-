using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

// Reproduction of legacy G4: shipping is part of the persisted order total.
// InMemory isolates arithmetic; this does not certify PostgreSQL behavior.
public sealed class LegacyShippingAccountingTests
{
    [Fact]
    public async Task PersistedOrderWithShipping_SaleEntryIncludesTheFullOrderTotal()
    {
        await using var db = new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase("f149-shipping-" + Guid.NewGuid().ToString("N")).Options);
        var order = new Orden
        {
            Id = Guid.NewGuid(),
            NumeroOrden = "F149-" + Guid.NewGuid().ToString("N"),
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = 100m,
            ImpuestoIva = 13m,
            CostoEnvio = 7m,
            MontoTotal = 120m,
            Estado = EstadoOrden.Aprobada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        db.Ordenes.Add(order);
        await db.SaveChangesAsync();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var entry = await service.GenerarAsientoVentaAsync(
            order.Id, order.SubTotal, order.ImpuestoIva, order.TipoComprobante);
        Assert.Equal(order.MontoTotal, entry.TotalDebe);
        Assert.Equal(order.MontoTotal, entry.TotalHaber);
        Assert.Equal(107m, entry.Lineas.Single(line => line.CodigoCuenta == "4101").Haber);
        var retry = await service.GenerarAsientoVentaAsync(
            order.Id, order.SubTotal, order.ImpuestoIva, order.TipoComprobante);
        Assert.Equal(entry.Id, retry.Id);
        Assert.Equal(1, await db.PartidasContables.CountAsync());
        order.CostoEnvio = 8m;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerarAsientoVentaAsync(
            order.Id, order.SubTotal, order.ImpuestoIva, order.TipoComprobante));
    }
}
