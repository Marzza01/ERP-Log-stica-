using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MarketplaceERP.API.Services;

/// <summary>Impide efectos financieros antes de entrar en la acción HTTP.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PaymentOperationsGuardAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var mode = context.HttpContext.RequestServices.GetRequiredService<PaymentOperationsMode>();
        if (!mode.IsDisabled) return;

        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(new
        {
            tipo = "PagosDeshabilitados",
            mensaje = "La operación financiera no está disponible en este entorno."
        }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
    }
}
