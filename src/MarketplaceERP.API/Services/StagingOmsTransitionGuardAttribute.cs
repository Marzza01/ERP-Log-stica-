using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Permite transiciones OMS administrativas de QA exclusivamente en el staging
/// Neon aislado. Nunca habilita una pasarela, checkout ni liquidaciones.
/// Con pagos deshabilitados, el valor predeterminado sigue siendo bloqueo HTTP 503.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StagingOmsTransitionGuardAttribute : ActionFilterAttribute
{
    public const string EnabledKey = "Oms:StagingTransitions:Enabled";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var services = context.HttpContext.RequestServices;
        var mode = services.GetRequiredService<PaymentOperationsMode>();
        if (!mode.IsDisabled)
            return; // Preserva el comportamiento anterior si la pasarela está habilitada.

        var configuration = services.GetRequiredService<IConfiguration>();
        if (AllowsIsolatedStagingTransitions(configuration, mode))
            return;

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(new
        {
            tipo = "OmsStagingNoAutorizado",
            mensaje = "Los cambios de estado OMS requieren habilitación explícita en el staging aislado. Los pagos externos permanecen deshabilitados."
        }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
    }

    public static bool AllowsIsolatedStagingTransitions(
        IConfiguration configuration, PaymentOperationsMode paymentMode)
    {
        if (!paymentMode.IsDisabled ||
            !bool.TryParse(configuration[EnabledKey], out var enabled) || !enabled ||
            !string.Equals(configuration["RENDER_SERVICE_NAME"],
                StagingDatabaseTargetGuard.ServiceName, StringComparison.Ordinal) ||
            !string.Equals(configuration["Deployment:Target"],
                StagingDatabaseTargetGuard.TargetMarker, StringComparison.Ordinal) ||
            !string.Equals(configuration["Payments:Provider"],
                "Disabled", StringComparison.OrdinalIgnoreCase))
            return false;

        // El marcador y el nombre del servicio no bastan: validar el host Neon,
        // usuario, base de datos, puerto y TLS contra la conexión real autorizada.
        try
        {
            StagingDatabaseTargetGuard.ValidateRuntime(
                configuration,
                configuration["RENDER_SERVICE_NAME"],
                "Production",
                configuration.GetConnectionString("DefaultConnection"));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
