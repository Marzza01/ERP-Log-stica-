/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Asiento contable de partida doble para el Libro Diario (PDF 1 - Grupo B).
/// Esquema DB: finance.partidas_contables
/// Garantiza la regla de oro contable: TotalDebe == TotalHaber.
/// </summary>
public class PartidaContable
{
    public Guid Id { get; set; }
    public string NumeroPartida { get; set; } = string.Empty;

    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public string Concepto { get; set; } = string.Empty;
    public string TipoOperacion { get; set; } = "Venta"; // "Venta", "Compra", "Liquidacion", "Ajuste"

    public Guid? ReferenciaOrigenId { get; set; }
    public string DocumentoReferencia { get; set; } = string.Empty;

    public decimal TotalDebe { get; set; }
    public decimal TotalHaber { get; set; }

    public bool EstaCuadrada => Math.Abs(TotalDebe - TotalHaber) < 0.01m;

    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;

    // Navegación
    public ICollection<LineaPartidaContable> Lineas { get; set; } = new List<LineaPartidaContable>();
}
