/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Filters;

/// <summary>
/// Filtro global de excepciones que captura DbUpdateConcurrencyException y retorna HTTP 409 Conflict.
/// Protege las operaciones concurrentes evitando que fallas de concurrencia optimista devuelvan 500
/// y sin exponer detalles internos de la base de datos.
/// </summary>
public class ConcurrencyExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ConcurrencyExceptionFilter> _logger;

    public ConcurrencyExceptionFilter(ILogger<ConcurrencyExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is DbUpdateConcurrencyException concurrencyEx)
        {
            _logger.LogWarning(concurrencyEx, "Conflicto de concurrencia optimista detectado en {Action}",
                context.ActionDescriptor.DisplayName);

            var problemDetails = new
            {
                tipo = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                titulo = "Conflicto de Concurrencia Optimista",
                status = StatusCodes.Status409Conflict,
                detalle = "El registro fue modificado o procesado simultáneamente por otra transacción. Por favor, recargue y vuelva a intentarlo."
            };

            context.Result = new ConflictObjectResult(problemDetails);
            context.ExceptionHandled = true;
        }
    }
}

