/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Core.Entities;

/// <summary>
/// Maestro de Proveedores salvadoreños y regionales (PDF 1 - Grupo B & PDF 2 - Sección 2).
/// Esquema DB: core.proveedores
/// </summary>
public class Proveedor
{
    public Guid Id { get; set; }
    public string NombreComercial { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string Nit { get; set; } = string.Empty;
    public string Nrc { get; set; } = string.Empty;

    public string ContactoPrincipal { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Direccion { get; set; } = "San Salvador, El Salvador";
    public string CategoriaSuministro { get; set; } = string.Empty;

    /// <summary>
    /// Plazo comercial en días (ej. Contado, 15, 30, 45, 60 días)
    /// </summary>
    public int PlazoCreditoDias { get; set; } = 30;

    /// <summary>
    /// Calificación y métrica OTIF histórica del proveedor (ej. 98.5%)
    /// </summary>
    public decimal OtifHistorico { get; set; } = 95.0m;

    public bool Activo { get; set; } = true;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
}
