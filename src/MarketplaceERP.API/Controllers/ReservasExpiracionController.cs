using Microsoft.Extensions.Hosting;
using System.Security.Claims;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// I3: vista administrativa y liberación conservadora de reservas vencidas.
/// No existe proceso automático; escritura exclusivamente en QA Development opt-in.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/reservas/expiracion")]
public sealed class ReservasExpiracionController : ControllerBase
{
    private readonly ReservasExpiracionService _servicio;
    private readonly CatalogDbContext _catalog;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _entorno;

    public ReservasExpiracionController(ReservasExpiracionService servicio,
        CatalogDbContext catalog, IConfiguration config, IHostEnvironment entorno)
    {
        _servicio = servicio;
        _catalog = catalog;
        _config = config;
        _entorno = entorno;
    }

    private bool AdminValido => User.Identity?.IsAuthenticated == true &&
        User.IsInRole("Admin") &&
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty;

    private bool EscrituraQA =>
        _entorno.IsDevelopment() &&
        _config.GetValue<bool>("Reservas:EnableExpirationQA") &&
        _catalog.Database.IsNpgsql() &&
        string.Equals(_catalog.Database.GetDbConnection().Database,
            "marketplace_erp_i2a_qa", StringComparison.Ordinal);

    [HttpGet("pendientes")]
    public async Task<IActionResult> Pendientes([FromQuery] int limite = 50,
        CancellationToken ct = default)
    {
        if (!AdminValido) return Forbid();
        if (limite is < 1 or > 100) return BadRequest(new { tipo = "LimiteInvalido" });
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(new { escrituraQA = EscrituraQA,
            reservas = await _servicio.ConsultarAsync(limite, ct) });
    }

    [HttpPost("ejecutar")]
    public async Task<IActionResult> Ejecutar([FromQuery] int limite = 20,
        [FromHeader(Name = "X-Confirmacion-Expiracion")] string? confirmacion = null,
        CancellationToken ct = default)
    {
        if (!AdminValido) return Forbid();
        if (limite is < 1 or > 20) return BadRequest(new { tipo = "LimiteInvalido" });
        if (!EscrituraQA) return StatusCode(503, new { tipo = "ExpiracionQANoHabilitada" });
        if (!string.Equals(confirmacion, "EXPIRAR-RESERVAS-QA", StringComparison.Ordinal))
            return BadRequest(new { tipo = "ConfirmacionAdministrativaRequerida" });
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await _servicio.EjecutarAsync(limite, ct));
    }
}
