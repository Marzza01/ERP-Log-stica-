using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.Catalog;

/// <summary>
/// Extensiones de inyección de dependencias para el módulo Catalog (Equipo 2).
/// Llamar desde Program.cs: builder.Services.AddCatalogServices(builder.Configuration)
/// </summary>
public static class CatalogServiceExtensions
{
    public static IServiceCollection AddCatalogServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));

        // ── Servicios del módulo Catalog ──────────────────────────────────────
        // TODO: Reemplazar StockServiceMock por StockService real
        services.AddScoped<IStockService, StockServiceMock>();

        return services;
    }
}
