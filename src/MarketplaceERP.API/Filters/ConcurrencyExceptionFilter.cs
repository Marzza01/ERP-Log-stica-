using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Filters;

/// <summary>
/// Recuperación selectiva del filtro histórico: responde HTTP 409 ante conflictos
/// EF de concurrencia optimista sin filtrar mensajes SQL ni stacktraces.
/// No modifica pagos y deja pasar todas las demás excepciones.
/// </summary>
public sealed class ConcurrencyExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ConcurrencyExceptionFilter> _logger;

    public ConcurrencyExceptionFilter(ILogger<ConcurrencyExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled ||
            context.Exception is not DbUpdateConcurrencyException)
            return;

        // Evitar que logs o respuestas expongan SQL, datos personales o tokens.
        _logger.LogWarning("Conflicto de concurrencia optimista; petición devuelta con HTTP 409.");
        context.Result = new ObjectResult(new ProblemDetails
        {
            Type = "https://httpstatuses.com/409",
            Title = "Conflicto de concurrencia",
            Status = StatusCodes.Status409Conflict,
            Detail = "El registro cambió durante la operación. Consulta su estado antes de reintentar."
        })
        {
            StatusCode = StatusCodes.Status409Conflict
        };
        context.ExceptionHandled = true;
    }
}
