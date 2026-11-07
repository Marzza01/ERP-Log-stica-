using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// FIN-06 ejecuta solicitudes simultáneas con DbContexts separados en PostgreSQL
/// 15 real, migrado, dentro de un DB efímero del runner. No usa Neon.
/// </summary>
public sealed class ConcurrenciaComprasPostgresFin06Tests : IAsyncLifetime
{
    private readonly string _database = "ci_fin06_" + Guid.NewGuid().ToString("N");
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
                throw new InvalidOperationException("FIN-06 exige PostgreSQL local en CI.");
            return;
        }

        var connection = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                connection.Host, StringComparer.OrdinalIgnoreCase) ||
            connection.Port != 5432 ||
            !string.Equals(connection.Database, "postgres", StringComparison.Ordinal) ||
            !string.Equals(connection.Username, "postgres", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(connection.Password))
            throw new InvalidOperationException(
                "FIN-06 admite únicamente postgres@localhost:5432/postgres del runner.");

        _adminConnection = connection.ConnectionString;
        connection.Database = _database;
        _testConnection = connection.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_database}\"";
            await cmd.ExecuteNonQueryAsync();
            _created = true;
        }

        await using var db = NewContext();
        await db.Database.MigrateAsync();
        Assert.Contains("20261107010000_FIN06IntegridadCompras",
            await db.Database.GetAppliedMigrationsAsync());
    }

    private FinanceDbContext NewContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!, options =>
                options.MigrationsHistoryTable("__EFMigrationsHistory", "finance")).Options);

    private static ComprasController Controller(FinanceDbContext db,
        IContabilidadService? ledger = null)
    {
        var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("fin06-core-" + Guid.NewGuid().ToString("N")).Options);
        return new ComprasController(
            db,
            new TresWayMatchingService(db, NullLogger<TresWayMatchingService>.Instance),
            ledger ?? new ContabilidadService(db, NullLogger<ContabilidadService>.Instance),
            Mock.Of<IInventoryService>(),
            core,
            NullLogger<ComprasController>.Instance);
    }

    private async Task<(Guid PoId, Guid FacturaId, Guid ProveedorId)> SeedAsync(
        Guid? proveedorId = null, bool withInvoice = true)
    {
        var poId = Guid.NewGuid();
        var proveedor = proveedorId ?? Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        await using var db = NewContext();
        var po = new OrdenCompra
        {
            Id = poId, ProveedorId = proveedor,
            NumeroPO = "PO-FIN06-" + Guid.NewGuid().ToString("N"),
            ProveedorNombre = "Proveedor local CI",
            SubTotal = 100m, ImpuestoIva = 13m, MontoTotal = 113m,
            Estado = EstadoOrdenCompra.RecibidaCompleta,
            Items = new List<ItemOrdenCompra>
            {
                new()
                {
                    Id = Guid.NewGuid(), Sku = "SKU-TEST",
                    NombreProducto = "Producto de prueba",
                    CantidadSolicitada = 10, CantidadRecibida = 10,
                    PrecioUnitarioAcordado = 10m
                }
            }
        };
        db.OrdenesCompra.Add(po);
        if (withInvoice)
        {
            db.FacturasProveedor.Add(new FacturaProveedor
            {
                Id = invoiceId, OrdenCompraId = poId, ProveedorId = proveedor,
                NumeroFactura = "CCF-" + Guid.NewGuid().ToString("N"),
                MontoSubTotal = 100m, MontoIva = 13m, MontoTotal = 113m,
                FechaEmision = DateTime.UtcNow,
                FechaVencimiento = DateTime.UtcNow.AddDays(30),
                Items = new List<ItemFacturaProveedor>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ItemOrdenCompraId = po.Items.Single().Id,
                        CantidadFacturada = 10,
                        PrecioUnitarioFacturado = 10m
                    }
                }
            });
        }
        await db.SaveChangesAsync();
        return (poId, invoiceId, proveedor);
    }

    [Fact]
    public async Task DosMatchesConcurrentes_SoloUnaAutorizacionYUnAsiento()
    {
        if (!_created) return;
        var (poId, invoiceId, _) = await SeedAsync();

        async Task<IActionResult> IntentoAsync()
        {
            await using var db = NewContext();
            return await Controller(db).EjecutarMatch(new(poId, invoiceId, 10),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(IntentoAsync(), IntentoAsync());
        Assert.Single(results, result => result is OkObjectResult);
        Assert.Single(results, result =>
            result is ConflictObjectResult or BadRequestObjectResult);

        await using var verify = NewContext();
        var authorized = await verify.MatchesTridireccionales.AsNoTracking()
            .Where(m => m.OrdenCompraId == poId && m.AutorizadoParaPago)
            .ToListAsync();
        Assert.Single(authorized);
        var journal = await verify.PartidasContables.AsNoTracking()
            .Include(p => p.Lineas)
            .Where(p => p.TipoOperacion == "Compra" && p.ReferenciaOrigenId == poId)
            .ToListAsync();
        var asiento = Assert.Single(journal);
        Assert.Equal(113m, asiento.TotalDebe);
        Assert.Equal(113m, asiento.TotalHaber);
        Assert.Equal(3, asiento.Lineas.Count);
        Assert.True((await verify.FacturasProveedor.SingleAsync(x => x.Id == invoiceId))
            .AprobadaParaPago);
    }

    [Fact]
    public async Task ContabilidadFalla_RollbackDeshaceMatchYAprobacion()
    {
        if (!_created) return;
        var (poId, invoiceId, _) = await SeedAsync();
        var accounting = new Mock<IContabilidadService>();
        accounting.Setup(x => x.GenerarAsientoCompraAsync(
                It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fallo simulado contabilidad"));

        await using (var db = NewContext())
        {
            var response = await Controller(db, accounting.Object)
                .EjecutarMatch(new(poId, invoiceId, 10), CancellationToken.None);
            Assert.IsType<BadRequestObjectResult>(response);
        }

        await using var verify = NewContext();
        Assert.Empty(await verify.MatchesTridireccionales
            .Where(x => x.OrdenCompraId == poId).ToListAsync());
        Assert.Empty(await verify.PartidasContables
            .Where(x => x.ReferenciaOrigenId == poId).ToListAsync());
        Assert.False((await verify.FacturasProveedor
            .SingleAsync(x => x.Id == invoiceId)).AprobadaParaPago);
    }

    [Fact]
    public async Task FacturaSimultaneaMismoProveedorYNumero_DistintaPO_SoloUnaPersiste()
    {
        if (!_created) return;
        var supplier = Guid.NewGuid();
        var (poA, _, _) = await SeedAsync(supplier, withInvoice: false);
        var (poB, _, _) = await SeedAsync(supplier, withInvoice: false);

        async Task<IActionResult> RegistrarAsync(Guid poId, string numero)
        {
            await using var db = NewContext();
            return await Controller(db).RegistrarFactura(
                new RegistrarFacturaRequest(poId, numero, 100m, 113m, null, null),
                CancellationToken.None);
        }

        var responses = await Task.WhenAll(
            RegistrarAsync(poA, "  CCF-FIN06-001  "),
            RegistrarAsync(poB, "ccf-fin06-001"));

        Assert.Single(responses, x => x is CreatedAtActionResult);
        Assert.Single(responses, x => x is ConflictObjectResult);

        await using var verify = NewContext();
        var invoices = await verify.FacturasProveedor.AsNoTracking()
            .Where(x => x.ProveedorId == supplier)
            .ToListAsync();
        Assert.Single(invoices);
        Assert.Equal(113m, invoices.Single().MontoTotal);
    }

    [Fact]
    public async Task PostgreSql_IndiceNoAdmiteSegundoAsientoCompraDeLaMismaPo()
    {
        if (!_created) return;
        var (poId, _, _) = await SeedAsync();

        await using (var db = NewContext())
            await new ContabilidadService(db, NullLogger<ContabilidadService>.Instance)
                .GenerarAsientoCompraAsync(poId, 100m, 13m);

        await using (var db = NewContext())
        {
            var accounting = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                accounting.GenerarAsientoCompraAsync(poId, 100m, 13m));
        }

        await using var verify = NewContext();
        Assert.Equal(1, await verify.PartidasContables.CountAsync(x =>
            x.TipoOperacion == "Compra" && x.ReferenciaOrigenId == poId));
    }

    [Fact]
    public async Task FIN07_PostgreSQL_DetalleFacturadoPersisteYUnicidadSeCumple()
    {
        if (!_created) return;
        var (poId, invoiceId, _) = await SeedAsync();

        await using var db = NewContext();
        var factura = await db.FacturasProveedor.AsNoTracking()
            .Include(f => f.Items)
            .SingleAsync(f => f.Id == invoiceId);
        var linea = Assert.Single(factura.Items);
        Assert.Equal(10, linea.CantidadFacturada);
        Assert.Equal(10m, linea.PrecioUnitarioFacturado);
        Assert.Equal(100m, factura.MontoSubTotal);

        db.ItemsFacturaProveedor.Add(new ItemFacturaProveedor
        {
            Id = Guid.NewGuid(),
            FacturaProveedorId = invoiceId,
            ItemOrdenCompraId = linea.ItemOrdenCompraId,
            CantidadFacturada = 1,
            PrecioUnitarioFacturado = 10m
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var verify = NewContext();
        Assert.Equal(1, await verify.ItemsFacturaProveedor.CountAsync(
            x => x.FacturaProveedorId == invoiceId));
        Assert.Equal(1, await verify.OrdenesCompra.CountAsync(x => x.Id == poId));
    }

    [Fact]
    public async Task FIN07_PostgreSQL_FacturaSinDetalleNoSeAutorizaNiCreaAsiento()
    {
        if (!_created) return;
        var (poId, _, _) = await SeedAsync(withInvoice: false);
        var invoiceId = Guid.NewGuid();

        await using (var db = NewContext())
        {
            var po = await db.OrdenesCompra.SingleAsync(x => x.Id == poId);
            db.FacturasProveedor.Add(new FacturaProveedor
            {
                Id = invoiceId, OrdenCompraId = poId, ProveedorId = po.ProveedorId,
                NumeroFactura = "CCF-LEGACY-" + Guid.NewGuid().ToString("N"),
                MontoSubTotal = 100m, MontoIva = 13m, MontoTotal = 113m,
                FechaEmision = DateTime.UtcNow,
                FechaVencimiento = DateTime.UtcNow.AddDays(30)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var result = await Controller(db).EjecutarMatch(
                new(poId, invoiceId, 10), CancellationToken.None);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        await using var verify = NewContext();
        Assert.False((await verify.FacturasProveedor.SingleAsync(
            x => x.Id == invoiceId)).AprobadaParaPago);
        Assert.Empty(await verify.MatchesTridireccionales
            .Where(m => m.OrdenCompraId == poId).ToListAsync());
        Assert.Empty(await verify.PartidasContables
            .Where(p => p.TipoOperacion == "Compra" && p.ReferenciaOrigenId == poId)
            .ToListAsync());
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null ||
            !_database.StartsWith("ci_fin06_", StringComparison.Ordinal)) return;

        if (_testConnection is not null)
        {
            using var pool = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(pool);
        }
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
        _created = false;
    }
}
