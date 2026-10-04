/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Mvc;
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
/// POST /api/auth/login → genera un JWT firmado con la clave de appsettings.json
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Usuarios de prueba en memoria para desarrollo.
    /// </summary>
    private static readonly Dictionary<string, (string Hash, string Role, string Nombre)> _usuarios = new()
    {
        ["admin"]             = ("admin1234", "Admin", "Administrador"),
        ["admin123"]          = ("admin123",  "Admin", "Administrador"),
        ["admin@logimkt.com"] = ("admin1234", "Admin", "Gerson Melgar (Admin)"),
        ["vendedor1"]         = ("pass",      "Vendedor", "Vendedor Principal"),
        ["comprador1"]        = ("pass",      "Comprador", "Comprador Demo"),
    };

    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IOptions<JwtSettings> jwtOptions,
        ILogger<AuthController> logger)
    {
        _jwtSettings = jwtOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Autentica un usuario y retorna un JWT Bearer token.
    /// </summary>
    /// <param name="request">Credenciales del usuario.</param>
    /// <returns>Token JWT con el rol del usuario.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var loginId = !string.IsNullOrWhiteSpace(request.Username)
            ? request.Username
            : request.Email;

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { mensaje = "Usuario/correo y contraseña son obligatorios." });

        if (!_usuarios.TryGetValue(loginId.ToLower().Trim(), out var userData) ||
            userData.Hash != request.Password)
        {
            _logger.LogWarning("Intento de login fallido para: {LoginId}", loginId);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var token = GenerarToken(loginId, userData.Role);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        _logger.LogInformation(
            "Login exitoso. Usuario={LoginId}, Rol={Role}", loginId, userData.Role);

        return Ok(new LoginResponse(token, userData.Role, expiresAt, userData.Nombre, userData.Role));
    }

    private string GenerarToken(string username, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
