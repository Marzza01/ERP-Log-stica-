/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Core.Entities;

/// <summary>
/// Representa a un comprador registrado en el marketplace.
/// Esquema DB: core.compradores
/// </summary>
public class Comprador
{
    public Guid Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DireccionEnvio { get; set; } = string.Empty;
    public bool EstaActivo { get; set; } = true;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}
