/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.Catalog;

/// <summary>
/// Extensiones de inyección de dependencias para el módulo Catalog (Célula A).
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
        services.AddScoped<IStockService, StockService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IAjusteInventarioService, AjusteInventarioService>();
        services.AddScoped<ICatalogImportService, CatalogImportService>();
        services.AddScoped<IWmsService, WmsService>();
        services.AddScoped<IRmaService, RmaService>();
        services.AddScoped<IFulfillmentService, FulfillmentService>();
        services.AddScoped<IReabastecimientoService, ReabastecimientoService>();

        return services;
    }
}
