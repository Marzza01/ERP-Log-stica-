using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.Finance;

/// <summary>
/// Extensiones de inyección de dependencias para el módulo Finance (Equipo 3).
/// Llamar desde Program.cs: builder.Services.AddFinanceServices(builder.Configuration)
/// </summary>
public static class FinanceServiceExtensions
{
    public static IServiceCollection AddFinanceServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));

        // ── Servicios del módulo Finance ──────────────────────────────────────
        // TODO: Reemplazar PagoServiceMock por integración real con pasarela
        services.AddScoped<IPaymentGateway, PagoServiceMock>();

        // El orquestador del checkout se registra como Scoped
        services.AddScoped<CheckoutOrchestrator>();

        return services;
    }
}
