/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Core.Entities;

/// <summary>
/// Representa a un usuario del sistema (Admin, Vendedor, Comprador).
/// Esquema DB: core.usuarios
/// </summary>
public class Usuario
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public string Nombre { get; set; } = string.Empty;
    public Guid? VendedorId { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Navegación opcional
    public Vendedor? Vendedor { get; set; }
}
