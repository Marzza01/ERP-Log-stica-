/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
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
/// Llame desde Program.cs: builder.Services.AddFinanceServices(builder.Configuration, builder.Environment.IsDevelopment())
/// </summary>
public static class FinanceServiceExtensions
{
    public static IServiceCollection AddFinanceServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        // I2B1: rechazo por defecto. El mock jamás debe operar implícitamente.
        // Aún no se dispone de un proveedor real; por ello cualquier configuración
        // diferente de Mock habilitado EXPLÍCITAMENTE en Development bloquea
        // el arranque, antes de abrir conexiones o procesar pagos.
        var provider = configuration["Payments:Provider"]?.Trim();
        var allowMock = string.Equals(configuration["Payments:EnableMock"], "true", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase) ||
            !isDevelopment || !allowMock)
        {
            throw new InvalidOperationException(
                "Pasarela de pagos no habilitada: actualmente solo se permite " +
                "Payments:Provider=Mock y Payments:EnableMock=true en Development. " +
                "No hay proveedor de cobros reales implementado; Production queda bloqueado.");
        }

        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));

        // ── Mock de pagos: únicamente Development y con consentimiento explícito ──
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
