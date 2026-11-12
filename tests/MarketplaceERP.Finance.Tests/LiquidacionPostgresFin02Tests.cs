using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Demuestra en PostgreSQL real la unicidad 1:1 y la exclusión de desembolsos
/// concurrentes. Crea y elimina SU propia base local; nunca toca Neon.
/// </summary>
public sealed class LiquidacionPostgresFin02Tests : IAsyncLifetime
{
    private readonly string _database = "ci_fin02_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _testConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FIN-02 requiere PostgreSQL local en CI.");
            return;
        }

        var connection = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                connection.Host, StringComparer.OrdinalIgnoreCase) ||
            connection.Port != 5432 ||
            !string.Equals(connection.Database, "postgres", StringComparison.Ordinal) ||
            !string.Equals(connection.Username, "postgres", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(connection.Password))
            throw new InvalidOperationException("FIN-02 admite solo postgres@localhost:5432/postgres.");

        _adminConnection = connection.ConnectionString;
        connection.Database = _database;
        _testConnection = connection.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{_database}\"";
            await command.ExecuteNonQueryAsync();
            _created = true;
        }

        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    private FinanceDbContext NewContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!).Options);

    private static LiquidacionService Service(FinanceDbContext db) =>
        new(db, new ContabilidadService(db, NullLogger<ContabilidadService>.Instance),
            NullLogger<LiquidacionService>.Instance, new PaymentOperationsMode(false));

    private async Task<(Guid SellerId, Guid SplitId)> SeedAsync(
        Guid? vendedorExistente = null, decimal montoVendedor = 90m)
    {
        var seller = vendedorExistente ?? Guid.NewGuid();
        var montoTotal = montoVendedor / 0.9m;
        var orderId = Guid.NewGuid();
        await using var db = NewContext();
        var order = new Orden
        {
            Id = orderId,
            NumeroOrden = "FIN02-" + Guid.NewGuid().ToString("N"),
            VendedorId = seller,
            CompradorId = Guid.NewGuid(),
            MontoTotal = montoTotal,
            SubTotal = montoTotal,
            Moneda = "USD",
            Estado = EstadoOrden.Entregada,
            TipoCliente = TipoCliente.B2C
        };
        var split = new SplitPago
        {
            Id = Guid.NewGuid(),
            Orden = order,
            OrdenId = orderId,
            MontoTotal = montoTotal,
            MontoVendedor = montoVendedor,
            MontoMarketplace = montoTotal - montoVendedor,
            TransaccionId = "SIM-" + Guid.NewGuid().ToString("N")
        };
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();
        return (seller, split.Id);
    }

    [Fact]
    public async Task PostgreSQL_DosGeneracionesDelMismoSplit_SoloUnaPersiste()
    {
        if (!_created) return;
        var (seller, splitId) = await SeedAsync();

        async Task<bool> Intentar()
        {
            await using var db = NewContext();
            try
            {
                var l = await Service(db).GenerarLiquidacionAsync(seller);
                Assert.Equal(splitId, l.SplitPagoId);
                Assert.Equal(90m, l.MontoLiquidar);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        var resultados = await Task.WhenAll(Intentar(), Intentar());
        Assert.Single(resultados, x => x);
        await using var verify = NewContext();
        var liquidacion = await verify.Liquidaciones.SingleAsync();
        Assert.Equal(splitId, liquidacion.SplitPagoId);
        Assert.Equal(90m, liquidacion.MontoLiquidar);
    }

    [Fact]
    public async Task PostgreSQL_DosProcesamientosDelMismoId_SoloUnAsientoSeConfirma()
    {
        if (!_created) return;
        var (seller, splitId) = await SeedAsync();
        Guid liqId;
        await using (var db = NewContext())
        {
            var liquidacion = await Service(db).GenerarLiquidacionAsync(seller);
            liqId = liquidacion.Id;
            Assert.Equal(splitId, liquidacion.SplitPagoId);
        }

        async Task<bool> Procesar()
        {
            await using var db = NewContext();
            try
            {
                var result = await Service(db).ProcesarPagoAsync(liqId);
                Assert.Equal(EstadoLiquidacion.Completada, result.Estado);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        var resultados = await Task.WhenAll(Procesar(), Procesar());
        Assert.Single(resultados, x => x);

        await using var verify = NewContext();
        var completed = await verify.Liquidaciones.SingleAsync(l => l.Id == liqId);
        Assert.Equal(EstadoLiquidacion.Completada, completed.Estado);
        var journal = await verify.PartidasContables.Include(p => p.Lineas)
            .Where(p => p.TipoOperacion == "Liquidacion")
            .ToListAsync();
        var part = Assert.Single(journal);
        Assert.Equal(liqId, part.ReferenciaOrigenId);
        Assert.Equal(90m, part.TotalDebe);
        Assert.Equal(part.TotalDebe, part.TotalHaber);
        Assert.Equal(2, part.Lineas.Count);
    }

    [Fact]
    public async Task PostgreSQL_TresSplitsUnaLiquidacion_ConcurrenciaYAsientoUnico()
    {
        if (!_created) return;
        var vendedor = Guid.NewGuid();
        var a = await SeedAsync(vendedor, 90m);
        var b = await SeedAsync(vendedor, 180m);
        var c = await SeedAsync(vendedor, 45m);
        var otro = await SeedAsync(null, 27m);

        async Task<Liquidacion?> Generar()
        {
            await using var db = NewContext();
            try { return await Service(db).GenerarLiquidacionAsync(vendedor); }
            catch (InvalidOperationException) { return null; }
        }

        var resultados = await Task.WhenAll(Generar(), Generar());
        var lote = Assert.Single(resultados, x => x is not null)!;
        Assert.Equal(315m, lote.MontoLiquidar);

        await using (var db = NewContext())
        {
            var liquidadas = await db.SplitsPago
                .Where(s => s.LiquidacionId == lote.Id)
                .Select(s => s.Id)
                .ToListAsync();
            Assert.Equal(3, liquidadas.Count);
            Assert.Contains(a.SplitId, liquidadas);
            Assert.Contains(b.SplitId, liquidadas);
            Assert.Contains(c.SplitId, liquidadas);
            Assert.Null((await db.SplitsPago.SingleAsync(s => s.Id == otro.SplitId)).LiquidacionId);
            Assert.Single(await db.Liquidaciones.Where(l => l.VendedorId == vendedor).ToListAsync());
            var historial = await Service(db).ObtenerHistorialAsync(vendedor);
            var historico = Assert.Single(historial);
            Assert.Equal(3, historico.SplitsPago.Count);
            Assert.Equal(315m, historico.SplitsPago.Sum(s => s.MontoVendedor));
            Assert.All(historico.SplitsPago, s =>
            {
                Assert.Equal(lote.Id, s.LiquidacionId);
                Assert.NotNull(s.Orden);
                Assert.Equal(vendedor, s.Orden.VendedorId);
            });
        }

        async Task<bool> Procesar()
        {
            await using var db = NewContext();
            try
            {
                await Service(db).ProcesarPagoAsync(lote.Id);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        var pagos = await Task.WhenAll(Procesar(), Procesar());
        Assert.Single(pagos, ok => ok);
        await using var final = NewContext();
        Assert.Equal(EstadoLiquidacion.Completada,
            (await final.Liquidaciones.SingleAsync(l => l.Id == lote.Id)).Estado);
        var asiento = Assert.Single(await final.PartidasContables.Include(p => p.Lineas)
            .Where(p => p.TipoOperacion == "Liquidacion" && p.ReferenciaOrigenId == lote.Id)
            .ToListAsync());
        Assert.Equal(315m, asiento.TotalDebe);
        Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        Assert.Equal(2, asiento.Lineas.Count);
    }

    [Fact]
    public async Task PostgreSQL_Down1NConLoteMultiple_FallaSinEliminarVinculos()
    {
        if (!_created) return;
        var vendedor = Guid.NewGuid();
        await SeedAsync(vendedor, 90m);
        await SeedAsync(vendedor, 45m);
        Guid liquidacionId;
        await using (var db = NewContext())
            liquidacionId = (await Service(db).GenerarLiquidacionAsync(vendedor)).Id;

        var downgrade = await Record.ExceptionAsync(async () =>
        {
            await using var db = NewContext();
            await db.GetService<IMigrator>().MigrateAsync(
                "20261110024500_FIN08UnicidadAsientosVentaLiquidacion");
        });
        Assert.NotNull(downgrade);
        Assert.Contains("FIN09", downgrade!.ToString());

        await using var verify = NewContext();
        Assert.Equal(2, await verify.SplitsPago.CountAsync(s => s.LiquidacionId == liquidacionId));
        var liquidacion = await verify.Liquidaciones.SingleAsync(l => l.Id == liquidacionId);
        Assert.Equal(135m, liquidacion.MontoLiquidar);
        Assert.Equal(EstadoLiquidacion.Pendiente, liquidacion.Estado);
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null ||
            !_database.StartsWith("ci_fin02_", StringComparison.Ordinal))
            return;
        if (_testConnection is not null)
        {
            using var ephemeral = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(ephemeral);
        }
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        _created = false;
    }
}
