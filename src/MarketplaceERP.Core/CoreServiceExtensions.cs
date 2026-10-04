/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.Core;

/// <summary>
/// Extensiones de inyección de dependencias para el módulo Core (Equipo 1).
/// Llamar desde Program.cs: builder.Services.AddCoreServices(builder.Configuration)
/// </summary>
public static class CoreServiceExtensions
{
    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<CoreDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "core")));

        // ── Servicios del módulo Core ─────────────────────────────────────────
        // TODO: Reemplazar CommissionServiceMock por CommissionService real
        services.AddScoped<ICommissionService, CommissionServiceMock>();

        return services;
    }
}
