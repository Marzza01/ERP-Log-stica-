/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Request de login compatible con Username o Email.
/// </summary>
public record LoginRequest(string? Username, string? Password, string? Email = null);

/// <summary>
/// Response de login exitoso con soporte de campos duales para frontend.
/// </summary>
public record LoginResponse(string Token, string Role, DateTime ExpiresAt, string Usuario, string Rol);

/// <summary>
/// Controlador de autenticación JWT.
/// POST /api/auth/login → valida contra core.usuarios mediante PasswordHasher y retorna JWT firmado.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly CoreDbContext _coreDb;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthController> _logger;
    private readonly PasswordHasher<Usuario> _passwordHasher;

    public AuthController(
        CoreDbContext coreDb,
        IOptions<JwtSettings> jwtOptions,
        ILogger<AuthController> logger)
    {
        _coreDb = coreDb;
        _jwtSettings = jwtOptions.Value;
        _logger = logger;
        _passwordHasher = new PasswordHasher<Usuario>();
    }

    /// <summary>
    /// Autentica un usuario y retorna un JWT Bearer token con claims sub=Id, NameIdentifier=Id, Role y VendedorId opcional.
    /// Rate limit aplicado: 5 intentos por minuto por IP (retorna 429).
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("LoginLimiter")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var loginId = !string.IsNullOrWhiteSpace(request.Username)
            ? request.Username
            : request.Email;

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { mensaje = "Usuario/correo y contraseña son obligatorios." });

        var normalizedLoginId = loginId.Trim().ToLower();

        // Buscar usuario por Email o por coincidencia en Nombre/Email en la base de datos
        var usuario = await _coreDb.Usuarios
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedLoginId || u.Nombre.ToLower() == normalizedLoginId, cancellationToken);

        if (usuario is null || !usuario.Activo)
        {
            _logger.LogWarning("Intento de login fallido para: {LoginId}", loginId);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(usuario, usuario.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Intento de login fallido para: {LoginId}", loginId);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var token = GenerarToken(usuario);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        _logger.LogInformation("Login exitoso. Usuario={UsuarioId}, Email={Email}, Rol={Role}", usuario.Id, usuario.Email, usuario.Rol);

        return Ok(new LoginResponse(token, usuario.Rol, expiresAt, usuario.Nombre, usuario.Rol));
    }

    private string GenerarToken(Usuario usuario)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, usuario.Email),
            new(ClaimTypes.Role, usuario.Rol),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        if (usuario.VendedorId.HasValue && usuario.Rol.Equals("Vendedor", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("VendedorId", usuario.VendedorId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
