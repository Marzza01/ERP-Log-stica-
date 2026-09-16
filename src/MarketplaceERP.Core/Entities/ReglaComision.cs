namespace MarketplaceERP.Core.Entities;

/// <summary>
/// Regla de comisión configurada por un administrador para un vendedor o categoría.
/// Esquema DB: core.reglas_comision
/// </summary>
public class ReglaComision
{
    public Guid Id { get; set; }
    public Guid VendedorId { get; set; }

    /// <summary>
    /// Porcentaje de comisión (0-100). Ejemplo: 10.00 = 10%.
    /// </summary>
    public decimal PorcentajeComision { get; set; }

    public bool EsActiva { get; set; } = true;
    public DateTime VigenteDesde { get; set; } = DateTime.UtcNow;
    public DateTime? VigenteHasta { get; set; }

    // Navegación
    public Vendedor Vendedor { get; set; } = null!;
}
