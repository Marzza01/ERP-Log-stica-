using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class OrdenEvaluacionPostgresTests
{
    // El único destino permitido para este ensayo es una BD nueva y local.
    // Se conserva para inspección; no se borra ningún dato ni recurso al terminar.
    [Fact]
    public async Task OrdenYReservas_Atomicas_Idempotentes_Concurrentes_Compensables_SinPagos()
    {
        var root = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TEST_PG_CONNECTION"));
        Assert.Contains(root.Host, new[] { "localhost", "127.0.0.1", "::1" });
        Assert.Equal("postgres", root.Database);
        var name = "ci_oms_evaluation_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(root.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        root.Database = name;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = root.ConnectionString,
            ["Orders:StagingEvaluation:Enabled"] = "true", ["Payments:Provider"] = "Disabled",
            ["Orders:StagingEvaluation:ExpiresAtUtc"] = DateTimeOffset.UtcNow.AddHours(2).ToString("O")
        }).Build();
        var env = new Mock<IWebHostEnvironment>(); env.SetupGet(e => e.EnvironmentName).Returns("Development");
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IConfiguration>(config); services.AddSingleton(env.Object);
        services.AddCatalogServices(config);
        services.AddDbContext<CoreDbContext>(o => o.UseNpgsql(root.ConnectionString,
            p => p.MigrationsHistoryTable("__EFMigrationsHistory", "core")));
        services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(root.ConnectionString,
            p => p.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
        services.AddScoped<OrdenEvaluacionService>();
        using var provider = services.BuildServiceProvider();
        var buyer = Guid.NewGuid(); var seller = Guid.NewGuid();
        var products = new[] { Guid.NewGuid(), Guid.NewGuid() }.OrderBy(x => x).ToArray();
        using (var setup = provider.CreateScope())
        {
            var core = setup.ServiceProvider.GetRequiredService<CoreDbContext>(); await core.Database.MigrateAsync();
            var catalog = setup.ServiceProvider.GetRequiredService<CatalogDbContext>(); await catalog.Database.MigrateAsync();
            var finance = setup.ServiceProvider.GetRequiredService<FinanceDbContext>(); await finance.Database.MigrateAsync();
            core.Compradores.Add(new Comprador { Id = buyer, Email = "buyer@example.invalid", NombreCompleto = "QA" });
            core.Vendedores.Add(new Vendedor { Id = seller, Email = "seller@example.invalid", NombreEmpresa = "QA" });
            await core.SaveChangesAsync();
            foreach (var id in products)
            {
                catalog.Productos.Add(new Producto { Id = id, Sku = "QA-" + id.ToString("N"), Nombre = "QA",
                    PrecioB2C = 25m, PrecioB2B = 250m, VendedorId = seller, CategoriaId = Guid.NewGuid() });
                catalog.StockFisico.Add(new StockFisico { Id = Guid.NewGuid(), ProductoId = id, CantidadTotal = 20 });
            }
            await catalog.SaveChangesAsync();
        }
        var actor = Guid.NewGuid();
        CrearOrdenRequest Request(int quantity = 1, TipoCliente channel = TipoCliente.B2C) => new(
            buyer, seller, channel, null, null, null, null, "Entrega sintética", 5m,
            products.Select(id => new ItemOrdenRequest(id, "PRECIO-FALSO", "NO-CONFIAR", quantity, 0m)).ToList());
        async Task<Orden> Create(CrearOrdenRequest request, string key)
        {
            using var scope = provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<OrdenEvaluacionService>()
                .CreateAsync(request, actor, key, default);
        }
        async Task Cancel(Guid id)
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<OrdenEvaluacionService>().CancelAsync(id, default);
        }

        var firstKey = Guid.NewGuid().ToString();
        var first = await Create(Request(), firstKey);
        Assert.Equal(61.50m, first.MontoTotal); Assert.All(first.Items, i => Assert.Equal(25m, i.PrecioUnitario));
        Assert.Equal(first.Id, (await Create(Request(), firstKey)).Id);
        Assert.Equal(409, (await Assert.ThrowsAsync<EvaluacionOrdenException>(() => Create(Request(2), firstKey))).StatusCode);
        var sameKey = Guid.NewGuid().ToString();
        var same = await Task.WhenAll(Create(Request(), sameKey), Create(Request(), sameKey));
        Assert.Equal(same[0].Id, same[1].Id);
        var b2b = await Create(Request(1, TipoCliente.B2B), Guid.NewGuid().ToString());
        Assert.Equal(570m, b2b.MontoTotal); Assert.Equal("CreditoFiscalCCF", b2b.TipoComprobante);
        // El segundo SKU falla después de reservar el primero: no debe quedar NINGÚN cambio.
        var noStock = Request(); noStock.Items[1] = noStock.Items[1] with { Cantidad = 10000 };
        Assert.Equal(409, (await Assert.ThrowsAsync<EvaluacionOrdenException>(() => Create(noStock, Guid.NewGuid().ToString()))).StatusCode);
        using (var verify = provider.CreateScope())
        {
            var catalog = verify.ServiceProvider.GetRequiredService<CatalogDbContext>();
            Assert.All(await catalog.StockFisico.AsNoTracking().ToListAsync(), s => Assert.Equal(3, s.CantidadReservada));
            Assert.Equal(6, await catalog.ReservasStock.CountAsync());
            Assert.Equal(3, await verify.ServiceProvider.GetRequiredService<FinanceDbContext>().Ordenes.CountAsync());
        }
        // Un trigger local falla durante el INSERT de la segunda reserva, después
        // de la primera escritura de StockService. No se modifica ningún servicio.
        await using (var inject = new NpgsqlConnection(root.ConnectionString))
        {
            await inject.OpenAsync();
            var sql = $"""
                CREATE FUNCTION catalog.qa_oms_fail_second() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'QA fallo segunda reserva'; END $$;
                CREATE TRIGGER qa_oms_fail_second BEFORE INSERT ON catalog.reservas_stock
                FOR EACH ROW WHEN (NEW."CantidadOriginal" = 2 AND NEW."ProductoId" = '{products[1]}')
                EXECUTE FUNCTION catalog.qa_oms_fail_second();
                """;
            await using var trigger = new NpgsqlCommand(sql, inject);
            await trigger.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => Create(Request(2), Guid.NewGuid().ToString()));
        // Compensación conjunta e idempotente; repetir creación no reactiva una cancelada.
        await Cancel(first.Id); await Cancel(first.Id);
        Assert.Equal(EstadoOrden.Cancelada, (await Create(Request(), firstKey)).Estado);
        await Cancel(same[0].Id); await Cancel(b2b.Id);
        // Dos órdenes compiten por las últimas unidades; solo una puede ganar.
        async Task<Orden?> Compete() { try { return await Create(Request(20), Guid.NewGuid().ToString()); }
            catch (EvaluacionOrdenException ex) when (ex.StatusCode == 409) { return null; } }
        var race = await Task.WhenAll(Compete(), Compete());
        var winner = Assert.Single(race, o => o is not null)!;
        await Cancel(winner.Id);
        using var final = provider.CreateScope();
        var finalCatalog = final.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.All(await finalCatalog.StockFisico.AsNoTracking().ToListAsync(), s =>
        { Assert.Equal(20, s.CantidadTotal); Assert.Equal(0, s.CantidadReservada); });
        var finalFinance = final.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(4, await finalFinance.Ordenes.CountAsync());
        Assert.Empty(await finalFinance.IntentosPago.ToListAsync());
        Assert.Empty(await finalFinance.SplitsPago.ToListAsync());
        Assert.Empty(await finalFinance.ReembolsosPago.ToListAsync());
        config["Payments:Provider"] = "Real";
        Assert.Equal(503, (await Assert.ThrowsAsync<EvaluacionOrdenException>(() =>
            Create(Request(), Guid.NewGuid().ToString()))).StatusCode);
        config["Payments:Provider"] = "Disabled";
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        Assert.Equal(503, (await Assert.ThrowsAsync<EvaluacionOrdenException>(() =>
            Create(Request(), Guid.NewGuid().ToString()))).StatusCode);
    }

}
