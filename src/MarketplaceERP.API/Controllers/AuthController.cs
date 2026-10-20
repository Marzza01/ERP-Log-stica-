/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Cryptography;
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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Request de login solo por Email.
/// </summary>
public record LoginRequest(string? Email, string? Password, string? Username = null);

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
    private static readonly Usuario _dummyUsuario = new() { Email = "dummy@security.internal" };
    private static readonly string _dummyHash = new PasswordHasher<Usuario>().HashPassword(_dummyUsuario, "DummySecurityHashValue2026!");

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
    /// Autentica un usuario solo por Email normalizado en minúsculas y retorna un JWT Bearer token.
    /// Rate limit aplicado: 5 intentos por minuto por IP (retorna 429).
    /// Prevención de timing attack: si el usuario no existe, se ejecuta VerifyHashedPassword contra un dummy hash.
    /// Registro en logs: se usa email enmascarado para evitar fugar inputs sensibles.
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
        var rawEmail = !string.IsNullOrWhiteSpace(request.Email)
            ? request.Email
            : request.Username;

        if (string.IsNullOrWhiteSpace(rawEmail) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { mensaje = "El correo electrónico y la contraseña son obligatorios." });

        var normalizedEmail = rawEmail.Trim().ToLowerInvariant();
        var maskedEmail = MaskEmail(normalizedEmail);

        // Buscar usuario estrictamente por Email normalizado en minúsculas
        var usuario = await _coreDb.Usuarios
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (usuario is null || !usuario.Activo)
        {
            // Ejecutar dummy check para mitigar timing attack (tiempos constantes)
            _passwordHasher.VerifyHashedPassword(_dummyUsuario, _dummyHash, request.Password);
            _logger.LogWarning("Intento de login fallido para cuenta: {MaskedEmail}", maskedEmail);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(usuario, usuario.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Intento de login fallido para cuenta: {MaskedEmail}", maskedEmail);
            return Unauthorized(new { mensaje = "Credenciales inválidas." });
        }

        var token = GenerarToken(usuario);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);

        _logger.LogInformation("Login exitoso. Usuario={UsuarioId}, Rol={Role}", usuario.Id, usuario.Rol);

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

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]))
        {
            return "***@***";
        }
        var name = parts[0];
        var domain = parts[1];
        var maskedName = name.Length <= 2 ? name[0] + "***" : name[..2] + new string('*', Math.Max(1, name.Length - 2));
        return $"{maskedName}@{domain}";
    }
}
