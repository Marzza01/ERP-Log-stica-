/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.ComponentModel.DataAnnotations;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// DTO para la creación de usuarios administrativos, vendedores o compradores.
/// </summary>
public record CrearUsuarioRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(10, ErrorMessage = "La contraseña debe tener al menos 10 caracteres.")] string Password,
    [Required] string Rol,
    [Required] string Nombre,
    Guid? VendedorId = null
);

/// <summary>
/// DTO de respuesta seguro de usuario (nunca expone PasswordHash).
/// </summary>
public record UsuarioResponse(
    Guid Id,
    string Email,
    string Nombre,
    string Rol,
    bool Activo,
    Guid? VendedorId,
    DateTime FechaRegistro
);

/// <summary>
/// Controlador de administración de usuarios (solo accesible por Administradores).
/// </summary>
[ApiController]
[Route("api/usuarios")]
[Authorize(Roles = "Admin")]
public class UsuariosController : ControllerBase
{
    private readonly CoreDbContext _coreDb;
    private readonly ILogger<UsuariosController> _logger;
    private readonly PasswordHasher<Usuario> _passwordHasher;

    private static readonly HashSet<string> _rolesValidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "Vendedor",
        "Comprador"
    };

    public UsuariosController(CoreDbContext coreDb, ILogger<UsuariosController> logger)
    {
        _coreDb = coreDb;
        _logger = logger;
        _passwordHasher = new PasswordHasher<Usuario>();
    }

    /// <summary>
    /// Crea un nuevo usuario en el sistema.
    /// Solo rol Admin tiene acceso.
    /// Valida: contraseña mínimo 10 caracteres, email único (409 si existe),
    /// roles permitidos (Admin, Vendedor, Comprador), y si rol es Vendedor exige VendedorId existente.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UsuarioResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear([FromBody] CrearUsuarioRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { mensaje = "El correo electrónico y la contraseña son obligatorios." });

        if (request.Password.Length < 10)
            return BadRequest(new { mensaje = "La contraseña debe tener al menos 10 caracteres." });

        if (!_rolesValidos.Contains(request.Rol))
            return BadRequest(new { mensaje = "El rol especificado es inválido. Roles permitidos: Admin, Vendedor, Comprador." });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Validar unicidad de email (409 Conflict si ya existe)
        var existeEmail = await _coreDb.Usuarios.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);
        if (existeEmail)
        {
            return Conflict(new { mensaje = $"El correo '{normalizedEmail}' ya está registrado en el sistema." });
        }

        // Si el rol es Vendedor, VendedorId es obligatorio y debe existir en core.vendedores
        Guid? vendedorIdAsignado = null;
        if (request.Rol.Equals("Vendedor", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.VendedorId.HasValue || request.VendedorId.Value == Guid.Empty)
            {
                return BadRequest(new { mensaje = "Para usuarios con rol 'Vendedor', el campo 'VendedorId' es obligatorio." });
            }

            var existeVendedor = await _coreDb.Vendedores.AnyAsync(v => v.Id == request.VendedorId.Value, cancellationToken);
            if (!existeVendedor)
            {
                return BadRequest(new { mensaje = $"El VendedorId '{request.VendedorId.Value}' especificado no existe en el sistema." });
            }

            vendedorIdAsignado = request.VendedorId.Value;
        }

        var nuevoUsuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            Nombre = request.Nombre.Trim(),
            Rol = request.Rol.Trim(),
            Activo = true,
            VendedorId = vendedorIdAsignado,
            FechaRegistro = DateTime.UtcNow
        };

        nuevoUsuario.PasswordHash = _passwordHasher.HashPassword(nuevoUsuario, request.Password);

        _coreDb.Usuarios.Add(nuevoUsuario);
        await _coreDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Usuario creado con éxito. Id={UsuarioId}, Email={Email}, Rol={Rol}",
            nuevoUsuario.Id, nuevoUsuario.Email, nuevoUsuario.Rol);

        var response = new UsuarioResponse(
            nuevoUsuario.Id,
            nuevoUsuario.Email,
            nuevoUsuario.Nombre,
            nuevoUsuario.Rol,
            nuevoUsuario.Activo,
            nuevoUsuario.VendedorId,
            nuevoUsuario.FechaRegistro
        );

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
