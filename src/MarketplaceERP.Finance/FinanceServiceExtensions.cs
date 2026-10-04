/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.Finance;

/// <summary>
/// Extensiones de inyección de dependencias para el módulo Finance (Equipo 3).
/// Llame desde Program.cs: builder.Services.AddFinanceServices(builder.Configuration)
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

        // ── Pasarela de pago (mock → reemplazar con integración real) ─────────
        services.AddScoped<IPaymentGateway, PagoServiceMock>();

        // ── Checkout ─────────────────────────────────────────────────────────
        services.AddScoped<CheckoutOrchestrator>();

        // ── Procurement / 3-Way Match ─────────────────────────────────────────
        services.AddScoped<ITresWayMatchingService, TresWayMatchingService>();

        // ── Contabilidad General (Libro Diario / Mayor / Balance) ─────────────
        services.AddScoped<IContabilidadService, ContabilidadService>();

        // ── Liquidaciones a Vendedores (Split 90/10) ──────────────────────────
        services.AddScoped<ILiquidacionService, LiquidacionService>();

        return services;
    }
}
