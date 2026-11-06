using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class AsientoManualFin01Tests
{
    private static FinanceDbContext NuevoDb()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("fin01-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new FinanceDbContext(options);
    }

    private static ContabilidadService Servicio(FinanceDbContext db) =>
        new(db, NullLogger<ContabilidadService>.Instance);

    [Fact]
    public async Task TresLineas_ConservaCuentasYMontosExactos()
    {
        await using var db = NuevoDb();
        var partida = await Servicio(db).GenerarAsientoManualAsync(
            "Regularización", "Ajuste", new[]
            {
                new LineaAsientoInput("1101", 113m, 0m, "Cargo caja"),
                new LineaAsientoInput("4101", 0m, 100m, "Abono ingresos"),
                new LineaAsientoInput("2105", 0m, 13m, "Abono IVA")
            });

        Assert.Equal("Regularización", partida.Concepto);
        Assert.Equal("Ajuste", partida.TipoOperacion);
        Assert.Equal(113m, partida.TotalDebe);
        Assert.Equal(113m, partida.TotalHaber);
        Assert.Equal(3, partida.Lineas.Count);
        Assert.StartsWith("AST-", partida.NumeroPartida);
        Assert.Null(partida.ReferenciaOrigenId);
        Assert.DoesNotContain(partida.Lineas, l => l.CodigoCuenta == "1105");
        Assert.Contains(partida.Lineas, l => l.CodigoCuenta == "1101" && l.Debe == 113m);
        Assert.Contains(partida.Lineas, l => l.CodigoCuenta == "4101" && l.Haber == 100m);
        Assert.Contains(partida.Lineas, l => l.CodigoCuenta == "2105" && l.Haber == 13m);
        Assert.Equal(1, await db.PartidasContables.CountAsync());
        Assert.Equal(3, await db.LineasPartidaContable.CountAsync());
    }

    [Theory]
    [InlineData("no-cuadra")]
    [InlineData("negativo")]
    [InlineData("doble-signo")]
    [InlineData("cero")]
    [InlineData("decimales")]
    [InlineData("cuenta-invalida")]
    [InlineData("una-linea")]
    public async Task AsientoInvalido_NoGuardaNada(string caso)
    {
        await using var db = NuevoDb();
        var lineas = caso switch
        {
            "no-cuadra" => new[] { new LineaAsientoInput("1101", 100m, 0m), new LineaAsientoInput("4101", 0m, 99.99m) },
            "negativo" => new[] { new LineaAsientoInput("1101", -1m, 0m), new LineaAsientoInput("4101", 0m, 1m) },
            "doble-signo" => new[] { new LineaAsientoInput("1101", 1m, 1m), new LineaAsientoInput("4101", 0m, 1m) },
            "cero" => new[] { new LineaAsientoInput("1101", 0m, 0m), new LineaAsientoInput("4101", 0m, 1m) },
            "decimales" => new[] { new LineaAsientoInput("1101", 1.001m, 0m), new LineaAsientoInput("4101", 0m, 1.001m) },
            "cuenta-invalida" => new[] { new LineaAsientoInput("9999", 100m, 0m), new LineaAsientoInput("4101", 0m, 100m) },
            _ => new[] { new LineaAsientoInput("1101", 100m, 0m) }
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Servicio(db).GenerarAsientoManualAsync("Ajuste", "Ajuste", lineas));
        Assert.Empty(await db.PartidasContables.ToListAsync());
        Assert.Empty(await db.LineasPartidaContable.ToListAsync());
    }

    [Fact]
    public async Task TipoManualNoPuedeDisfrazarseDeVenta()
    {
        await using var db = NuevoDb();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Servicio(db).GenerarAsientoManualAsync("Falso", "Venta", new[]
            {
                new LineaAsientoInput("1101", 10m, 0m),
                new LineaAsientoInput("4101", 0m, 10m)
            }));
        Assert.Empty(await db.PartidasContables.ToListAsync());
    }

    [Fact]
    public async Task NumerosManualesDistintos_SinCountMasUno()
    {
        await using var db = NuevoDb();
        var lineas = new[]
        {
            new LineaAsientoInput("1101", 10m, 0m),
            new LineaAsientoInput("4101", 0m, 10m)
        };
        var svc = Servicio(db);
        var a = await svc.GenerarAsientoManualAsync("A", "Ajuste", lineas);
        var b = await svc.GenerarAsientoManualAsync("B", "Ajuste", lineas);
        Assert.NotEqual(a.NumeroPartida, b.NumeroPartida);
        Assert.Equal(2, await db.PartidasContables.CountAsync());
    }

    [Fact]
    public void EndpointPermaneceBloqueadoPorGuardaOperativa()
    {
        var metodo = typeof(ContabilidadController).GetMethod(nameof(ContabilidadController.AsientoManual));
        Assert.NotNull(metodo);
        Assert.Single(metodo!.GetCustomAttributes(typeof(PaymentOperationsGuardAttribute), true));
    }
}
