namespace MarketplaceERP.Core.Entities;

/// <summary>
/// Representa a un vendedor registrado en el marketplace.
/// Esquema DB: core.vendedores
/// </summary>
public class Vendedor
{
    public Guid Id { get; set; }
    public string NombreEmpresa { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public bool EstaActivo { get; set; } = true;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Navegación
    public ICollection<ReglaComision> ReglasComision { get; set; } = new List<ReglaComision>();
}
