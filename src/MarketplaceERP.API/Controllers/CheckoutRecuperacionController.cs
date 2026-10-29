using System.Security.Claims;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Acciones administrativas de recuperación, exclusivamente en Development QA.
/// Bloqueadas por defecto y sin pasarela simulada registrada para recuperación.
/// Jamás se habilitan por una opción en Production.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/checkout/recuperacion")]
public sealed class CheckoutRecuperacionController : ControllerBase
{
    private readonly FinanceDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _environment;
    private readonly IServiceProvider _services;
    private readonly ILogger<CheckoutRecuperacionController> _logger;

    public CheckoutRecuperacionController(
        FinanceDbContext db,
        IConfiguration config,
        IHostEnvironment environment,
        IServiceProvider services,
        ILogger<CheckoutRecuperacionController> logger)
    {
        _db = db;
        _config = config;
        _environment = environment;
        _services = services;
        _logger = logger;
    }

    private bool ConciliacionHabilitada =>
        _environment.IsDevelopment() && _config.GetValue<bool>("Payments:EnableRecoveryQA");

    private bool ReembolsosHabilitados =>
        ConciliacionHabilitada && _config.GetValue<bool>("Payments:EnableRefundsQA");

    // Consultar las capacidades no habilita las acciones ni revela credenciales.
    [HttpGet("capacidades")]
    public IActionResult Capacidades()
    {
        if (!EsAdministradorAutenticado()) return Forbid();
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(new
        {
            conciliacionQA = ConciliacionHabilitada,
            reembolsosQA = ReembolsosHabilitados,
            proveedorRecuperacionDisponible = _services.GetService<IPaymentRecoveryGateway>() is not null,
            advertencia = "Acciones restringidas a Development QA. No se habilitan pagos reales."
        });
    }

    /// <summary>
    /// Consulta al proveedor de recuperación y registra la decisión. NO ejecuta
    /// cobro, reembolso, confirmación de pedido ni movimiento de inventario.
    /// </summary>
    [HttpPost("ordenes/{ordenId:guid}/conciliar")]
    public async Task<IActionResult> Conciliar(Guid ordenId, CancellationToken ct = default)
    {
        if (!EsAdministradorAutenticado()) return Forbid();
        if (ordenId == Guid.Empty) return BadRequest(new { tipo = "OrdenInvalida" });
        if (!ConciliacionHabilitada) return Bloqueado("ConciliacionQAInhabilitada");
        if (_services.GetService<IPaymentRecoveryGateway>() is null)
            return Bloqueado("PasarelaRecuperacionNoDisponible");

        if (!await _db.Ordenes.AsNoTracking().AnyAsync(o => o.Id == ordenId, ct))
            return NotFound();

        // La resolución del proveedor solo existe cuando se registró explícitamente
        // IPaymentRecoveryGateway para QA. Nunca usar PagoServiceMock como recuperación.
        var servicio = _services.GetRequiredService<RecuperacionFinancieraService>();
        var resultado = await servicio.ConciliarYRegistrarAsync(ordenId, ct);
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(new
        {
            ordenId = resultado.OrdenId,
            decision = resultado.Decision.ToString(),
            motivo = resultado.Motivo,
            transaccionId = resultado.TransaccionId
        });
    }

    /// <summary>
    /// Reembolso INTEGRO de una operación elegible. Solo QA Development, con
    /// bandera adicional y confirmación administrativa explícita por solicitud.
    /// No existe implementación concreta de proveedor en la aplicación actual.
    /// </summary>
    [HttpPost("ordenes/{ordenId:guid}/reembolsar")]
    public async Task<IActionResult> Reembolsar(
        Guid ordenId,
        [FromHeader(Name = "X-Confirmacion-Reembolso")] string? confirmacion,
        CancellationToken ct = default)
    {
        if (!EsAdministradorAutenticado()) return Forbid();
        if (ordenId == Guid.Empty) return BadRequest(new { tipo = "OrdenInvalida" });
        if (!ReembolsosHabilitados) return Bloqueado("ReembolsosQAInhabilitados");
        if (_services.GetService<IPaymentRecoveryGateway>() is null)
            return Bloqueado("PasarelaRecuperacionNoDisponible");
        if (!string.Equals(confirmacion, $"REEMBOLSAR-{ordenId:N}", StringComparison.Ordinal))
            return BadRequest(new { tipo = "ConfirmacionAdministrativaRequerida" });

        if (!await _db.Ordenes.AsNoTracking().AnyAsync(o => o.Id == ordenId, ct))
            return NotFound();

        try
        {
            var servicio = _services.GetRequiredService<RecuperacionFinancieraService>();
            var resultado = await servicio.ReembolsarSeguroAsync(ordenId, ct);
            Response.Headers["Cache-Control"] = "no-store";
            var cuerpo = new
            {
                ordenId,
                estado = resultado.Estado.ToString(),
                referenciaProveedor = resultado.ReembolsoId
            };
            return resultado.Estado switch
            {
                EstadoReembolsoProveedor.Completado => Ok(cuerpo),
                EstadoReembolsoProveedor.Rechazado => Conflict(cuerpo),
                _ => Accepted(value: cuerpo)
            };
        }
        catch (InvalidOperationException ex)
        {
            // No filtrar estados, IDs externos ni errores del proveedor al cliente.
            _logger.LogWarning(ex, "Operacion de reembolso no elegible. OrdenId={OrdenId}", ordenId);
            return Conflict(new
            {
                tipo = "ReembolsoNoElegible",
                mensaje = "El estado actual impide el reembolso automatico. Requiere revision administrativa."
            });
        }
    }

    private bool EsAdministradorAutenticado() =>
        User.Identity?.IsAuthenticated == true &&
        User.IsInRole("Admin") &&
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
        id != Guid.Empty;

    private ObjectResult Bloqueado(string tipo) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            tipo,
            mensaje = "Recuperacion financiera deshabilitada o sin proveedor autorizado."
        });
}
