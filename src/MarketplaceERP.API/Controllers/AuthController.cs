using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Request de login.
/// </summary>
public record LoginRequest(string Username, string Password);

/// <summary>
/// Response de login exitoso.
/// </summary>
public record LoginResponse(string Token, string Role, DateTime ExpiresAt);

/// <summary>
/// Controlador de autenticación JWT.
/// POST /api/auth/login → genera un JWT firmado con la clave de appsettings.json
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Usuarios de prueba hardcodeados en memoria (solo para desarrollo inicial).
    /// En producción, reemplazar por consulta a base de datos con hash de contraseñas.
    /// </summary>
    private static readonly Dictionary<string, (string Hash, string Role)> _usuarios = new()
    {
        ["admin"]      = ("admin123", "Admin"),
        ["vendedor1"]  = ("pass",     "Vendedor"),
        ["comprador1"] = ("pass",     "Comprador"),
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
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { mensaje = "Usuario y contraseña son obligatorios." });

        if (!_usuarios.TryGetValue(request.Username.ToLower(), out var userData) ||
            userData.Hash != request.Password)
        {
            _logger.LogWarning("Intento de login fallido para el usuario: {Username}", request.Username);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var token = GenerarToken(request.Username, userData.Role);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        _logger.LogInformation(
            "Login exitoso. Usuario={Username}, Rol={Role}", request.Username, userData.Role);

        return Ok(new LoginResponse(token, userData.Role, expiresAt));
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
