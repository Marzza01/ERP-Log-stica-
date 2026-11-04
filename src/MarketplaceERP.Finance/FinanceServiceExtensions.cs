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
        // El proveedor debe elegirse explícitamente. Disabled permite consultas
        // en Production, pero ninguna operación que requiera mover dinero.
        var provider = configuration["Payments:Provider"]?.Trim();
        var allowMock = string.Equals(configuration["Payments:EnableMock"], "true", StringComparison.OrdinalIgnoreCase);
        var disabled = string.Equals(provider, "Disabled", StringComparison.OrdinalIgnoreCase);
        var mock = string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase) &&
                   isDevelopment && allowMock;
        if (!disabled && !mock)
        {
            throw new InvalidOperationException(
                "Pasarela de pagos no configurada: use Payments:Provider=Disabled, o " +
                "Payments:Provider=Mock con Payments:EnableMock=true solo en Development.");
        }

        // ── DbContext ─────────────────────────────────────────────────────────
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));

        services.AddSingleton(new PaymentOperationsMode(disabled));
        if (disabled)
            services.AddScoped<IPaymentGateway, PagoServiceDisabled>();
        else
            services.AddScoped<IPaymentGateway, PagoServiceMock>();

        // ── Checkout ─────────────────────────────────────────────────────────
        services.AddScoped<CheckoutOrchestrator>();

        // ── Recuperación administrada, bloqueada por defecto en HTTP ──────────
        // No se registra IPaymentRecoveryGateway: no existe proveedor de
        // recuperación real ni simulador habilitado automáticamente.
        services.AddScoped<EvaluadorConciliacionPago>(sp => ActivatorUtilities.CreateInstance<EvaluadorConciliacionPago>(sp));
        services.AddScoped<RecuperacionFinancieraService>(sp => ActivatorUtilities.CreateInstance<RecuperacionFinancieraService>(sp));

        // ── Procurement / 3-Way Match ─────────────────────────────────────────
        services.AddScoped<ITresWayMatchingService, TresWayMatchingService>();

        // ── Contabilidad General (Libro Diario / Mayor / Balance) ─────────────
        services.AddScoped<IContabilidadService, ContabilidadService>();

        // ── Liquidaciones a Vendedores (Split 90/10) ──────────────────────────
        services.AddScoped<ILiquidacionService, LiquidacionService>();

        return services;
    }
}
