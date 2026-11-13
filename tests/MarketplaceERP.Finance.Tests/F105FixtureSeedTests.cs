using MarketplaceERP.Catalog;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Fixture exclusivo del E2E F105. Solo escribe en PostgreSQL loopback de GitHub Actions.
/// Los pedidos se crean después por la API real de evaluación y reservan inventario.
/// </summary>
public sealed class F105FixtureSeedTests
{
    public static readonly Guid SellerAId = Guid.Parse("f1050000-0000-4000-8000-000000000001");
    public static readonly Guid SellerBId = Guid.Parse("f1050000-0000-4000-8000-000000000002");
    public static readonly Guid BuyerId = Guid.Parse("f1050000-0000-4000-8000-000000000003");
    public static readonly Guid CategoryId = Guid.Parse("f1050000-0000-4000-8000-000000000004");
    public static readonly Guid ProductA1Id = Guid.Parse("f1050000-0000-4000-8000-000000000011");
    public static readonly Guid ProductA2Id = Guid.Parse("f1050000-0000-4000-8000-000000000012");
    public static readonly Guid ProductB1Id = Guid.Parse("f1050000-0000-4000-8000-000000000021");

    private const string BuyerEmail = "f105-buyer@erp.invalid";

    [Fact]
    public async Task SeedTwoSellerProductsAndStock_OnlyInDisposableGithubPostgres()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("F105_SEED_FIXTURE"), "true",
                StringComparison.OrdinalIgnoreCase))
            return;

        Assert.Equal("true", Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.Equal("true", Environment.GetEnvironmentVariable("CI"));

        var connectionString = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Contains(target.Host, new[] { "localhost", "127.0.0.1", "::1" });
        Assert.Equal(5432, target.Port);
        Assert.Equal("postgres", target.Database);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = target.ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCatalogServices(configuration);
        services.AddDbContext<CoreDbContext>(options => options.UseNpgsql(target.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "core")));
        services.AddDbContext<FinanceDbContext>(options => options.UseNpgsql(target.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        await core.Database.MigrateAsync();
        await catalog.Database.MigrateAsync();
        await finance.Database.MigrateAsync();

        Assert.Empty(await finance.Ordenes.AsNoTracking().ToListAsync());
        Assert.Empty(await catalog.ReservasStock.AsNoTracking().ToListAsync());
        Assert.False(await core.Usuarios.AnyAsync(user => user.Email == BuyerEmail));

        Assert.False(await core.Vendedores.AnyAsync(v => v.Id == SellerAId || v.Id == SellerBId));
        Assert.False(await core.Compradores.AnyAsync(b => b.Id == BuyerId || b.Email == BuyerEmail));

        core.Vendedores.AddRange(
            new Vendedor
            {
                Id = SellerAId,
                NombreEmpresa = "F105 Seller A",
                Email = "f105-seller-a-profile@erp.invalid",
                Telefono = "0000-0000",
                EstaActivo = true
            },
            new Vendedor
            {
                Id = SellerBId,
                NombreEmpresa = "F105 Seller B",
                Email = "f105-seller-b-profile@erp.invalid",
                Telefono = "0000-0000",
                EstaActivo = true
            });
        core.Compradores.Add(new Comprador
        {
            Id = BuyerId,
            NombreCompleto = "F105 Buyer",
            Email = BuyerEmail,
            DireccionEnvio = "Dirección sintética F105",
            EstaActivo = true,
            FechaRegistro = DateTime.UtcNow
        });
        await core.SaveChangesAsync();

        var products = new[]
        {
            new Producto
            {
                Id = ProductA1Id, Sku = "F105-A-001", Nombre = "Producto QA A1",
                Descripcion = "Fixture local F105", CategoriaNombre = "F105 QA",
                PrecioB2C = 10m, PrecioB2B = 8m, Moneda = "USD",
                StockInicial = 0, StockMinimo = 0, ProveedorNombre = "F105 QA",
                PaisOrigen = "SV", PesoKg = 1m, DimensionesCm = "10x10x10",
                UnidadMedida = "Unidad", EstadoProducto = "Activo",
                VendedorId = SellerAId, CategoriaId = CategoryId, EstaPublicado = true
            },
            new Producto
            {
                Id = ProductA2Id, Sku = "F105-A-002", Nombre = "Producto QA A2",
                Descripcion = "Fixture local F105", CategoriaNombre = "F105 QA",
                PrecioB2C = 12m, PrecioB2B = 9m, Moneda = "USD",
                StockInicial = 0, StockMinimo = 0, ProveedorNombre = "F105 QA",
                PaisOrigen = "SV", PesoKg = 1m, DimensionesCm = "10x10x10",
                UnidadMedida = "Unidad", EstadoProducto = "Activo",
                VendedorId = SellerAId, CategoriaId = CategoryId, EstaPublicado = true
            },
            new Producto
            {
                Id = ProductB1Id, Sku = "F105-B-001", Nombre = "Producto QA B1",
                Descripcion = "Fixture local F105", CategoriaNombre = "F105 QA",
                PrecioB2C = 20m, PrecioB2B = 15m, Moneda = "USD",
                StockInicial = 0, StockMinimo = 0, ProveedorNombre = "F105 QA",
                PaisOrigen = "SV", PesoKg = 1m, DimensionesCm = "10x10x10",
                UnidadMedida = "Unidad", EstadoProducto = "Activo",
                VendedorId = SellerBId, CategoriaId = CategoryId, EstaPublicado = true
            }
        };

        catalog.Productos.AddRange(products);
        foreach (var product in products)
        {
            catalog.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(),
                ProductoId = product.Id,
                CantidadTotal = 20
            });
        }
        await catalog.SaveChangesAsync();

        Assert.Equal(2, await core.Vendedores.CountAsync(v => v.Id == SellerAId || v.Id == SellerBId));
        Assert.True(await core.Compradores.AnyAsync(b => b.Id == BuyerId && b.EstaActivo));
        Assert.Equal(3, await catalog.Productos.CountAsync(p => p.Id == ProductA1Id || p.Id == ProductA2Id || p.Id == ProductB1Id));
        Assert.Equal(3, await catalog.StockFisico.CountAsync(s =>
            s.ProductoId == ProductA1Id || s.ProductoId == ProductA2Id || s.ProductoId == ProductB1Id));
    }
}
