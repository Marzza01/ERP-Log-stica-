using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
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
/// P0 guardrails sobre PostgreSQL 15 migrado, con una base efímera por test.
/// Nunca utiliza Neon, Render, Vercel, pagos ni datos reales.
/// </summary>
public sealed class ConsolidacionPostgresGuardsTests : IAsyncLifetime
{
    private readonly string _database = "ci_consolidacion_" + Guid.NewGuid().ToString("N");
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
                throw new InvalidOperationException("Se requiere PostgreSQL local del runner en CI.");
            return;
        }

        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                cs.Host, StringComparer.OrdinalIgnoreCase) ||
            cs.Port != 5432 || cs.Database != "postgres" ||
            cs.Username != "postgres" || string.IsNullOrWhiteSpace(cs.Password))
            throw new InvalidOperationException(
                "Esta prueba SOLO admite postgres@localhost:5432/postgres con credenciales CI.");

        _adminConnection = cs.ConnectionString;
        cs.Database = _database;
        _testConnection = cs.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_database}\"";
            await cmd.ExecuteNonQueryAsync();
            _created = true;
        }

        await using (var core = NewCore()) await core.Database.MigrateAsync();
        await using (var catalog = NewCatalog()) await catalog.Database.MigrateAsync();
        await using (var finance = NewFinance()) await finance.Database.MigrateAsync();
    }

    private CoreDbContext NewCore() => new(
        new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(_testConnection!, x =>
                x.MigrationsHistoryTable("__EFMigrationsHistory", "core")).Options);

    private CatalogDbContext NewCatalog() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_testConnection!, x =>
                x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")).Options);

    private FinanceDbContext NewFinance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnection!, x =>
                x.MigrationsHistoryTable("__EFMigrationsHistory", "finance")).Options);

    private static Orden Order(EstadoOrden state)
    {
        var id = Guid.NewGuid();
        return new Orden
        {
            Id = id, NumeroOrden = "ORD-CONS-" + id.ToString("N"),
            Estado = state, CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid()
        };
    }

    [Fact]
    public async Task EstadoCancelado_NoPuedeGenerarPicklist_EnPostgres()
    {
        if (!_created) return;
        await using var db = NewFinance();
        var order = Order(EstadoOrden.Cancelada);
        db.Ordenes.Add(order);
        await db.SaveChangesAsync();
        var mock = new Mock<IFulfillmentService>(MockBehavior.Strict);
        var controller = new FulfillmentController(mock.Object, db);

        var result = await controller.GenerarPickList(order.Id, "Operador", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(EstadoOrden.Cancelada,
            (await db.Ordenes.AsNoTracking().SingleAsync()).Estado);
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmpaqueConOrdenAjena_NoEscribeGuiaNiPaquetes_EnPostgres()
    {
        if (!_created) return;
        await using var finance = NewFinance();
        await using var catalog = NewCatalog();
        var order = Order(EstadoOrden.EnProceso);
        finance.Ordenes.Add(order);
        await finance.SaveChangesAsync();
        var mock = new Mock<IFulfillmentService>(MockBehavior.Strict);
        var controller = new FulfillmentController(mock.Object, finance);
        var request = new EmpacarDespachoRequest(
            Guid.NewGuid(), order.NumeroOrden, 1m, 10m, 10m, 10m,
            "Transportista", "San Salvador", "Centro", "Dirección",
            "Cliente", "7000-0000");

        var result = await controller.EmpacarYRotular(order.Id, request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("", (await finance.Ordenes.AsNoTracking().SingleAsync()).GuiaEnvio);
        Assert.Equal(0, await catalog.PaquetesDespacho.CountAsync());
        mock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AsientoVenta_ReintentoSecuencialDistintosContexts_NoDuplica()
    {
        if (!_created) return;
        var orderId = Guid.NewGuid();
        Guid firstId;
        await using (var db = NewFinance())
        {
            var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
            firstId = (await service.GenerarAsientoVentaAsync(
                orderId, 100m, 13m, "FacturaConsumidorFinal")).Id;
        }
        await using (var db = NewFinance())
        {
            var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
            var retry = await service.GenerarAsientoVentaAsync(
                orderId, 100m, 13m, "FacturaConsumidorFinal");
            Assert.Equal(firstId, retry.Id);
        }
        await using (var db = NewFinance())
        {
            Assert.Equal(1, await db.PartidasContables.AsNoTracking()
                .CountAsync(x => x.ReferenciaOrigenId == orderId &&
                                 x.TipoOperacion == "Venta"));
            Assert.Equal(3, await db.LineasPartidaContable.AsNoTracking()
                .CountAsync(x => x.PartidaContableId == firstId));
        }
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null ||
            !_database.StartsWith("ci_consolidacion_", StringComparison.Ordinal)) return;

        if (_testConnection is not null)
        {
            using var pooled = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(pooled);
        }

        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
        _created = false;
    }
}
