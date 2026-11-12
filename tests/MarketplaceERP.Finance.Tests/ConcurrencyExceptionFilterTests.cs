using System.Text.Json;
using System.Net;
using MarketplaceERP.API.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Recuperación histórica selectiva: no se exponen errores SQL ni se silencian
/// excepciones ajenas a la concurrencia. Solo pruebas de filtro, no E2E real.
/// </summary>
public sealed class ConcurrencyExceptionFilterTests
{
    private static ExceptionContext Contexto(Exception ex) =>
        new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>()) { Exception = ex };

    [Fact]
    public void ConflictoDeConcurrencia_Devuelve409_SinExponerDetallesInternos()
    {
        const string secreto = "sql-postgres-SECRET_INTERNAL_CONNECTION";
        var context = Contexto(new DbUpdateConcurrencyException(secreto));
        var filtro = new ConcurrencyExceptionFilter(NullLogger<ConcurrencyExceptionFilter>.Instance);

        filtro.OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.DoesNotContain(secreto, JsonSerializer.Serialize(problem), StringComparison.Ordinal);
        Assert.DoesNotContain("postgres", JsonSerializer.Serialize(problem), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OtraExcepcion_NoSeMarcaComoResuelta()
    {
        var context = Contexto(new InvalidOperationException("pago incierto"));
        new ConcurrencyExceptionFilter(NullLogger<ConcurrencyExceptionFilter>.Instance)
            .OnException(context);
        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task HttpRealDeTestServer_ConflictoEFResponde409SinDetalleSql()
    {
        var host = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddControllers(options =>
                        options.Filters.Add<ConcurrencyExceptionFilter>())
                    .AddApplicationPart(typeof(QaConcurrencyProbeController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        using var server = new TestServer(host);
        using var client = server.CreateClient();
        using var result = await client.GetAsync("/api/qa-concurrency/throw");
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        var response = await result.Content.ReadAsStringAsync();
        Assert.DoesNotContain("internal-secret", response, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("409", response);
    }

    [Fact]
    public void ConflictoPreviamenteAtendido_NoSeReescribe()
    {
        var context = Contexto(new DbUpdateConcurrencyException("internal"));
        var anterior = new StatusCodeResult(StatusCodes.Status418ImATeapot);
        context.Result = anterior;
        context.ExceptionHandled = true;
        new ConcurrencyExceptionFilter(NullLogger<ConcurrencyExceptionFilter>.Instance)
            .OnException(context);
        Assert.Same(anterior, context.Result);
    }
}

[ApiController]
[Route("api/qa-concurrency")]
public sealed class QaConcurrencyProbeController : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult ThrowConcurrency()
        => throw new DbUpdateConcurrencyException("internal-secret-sql-connection");
}
