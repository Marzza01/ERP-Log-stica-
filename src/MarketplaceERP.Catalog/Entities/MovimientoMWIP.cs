/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Control de Insumos y Materia Prima en Tránsito o Procesamiento (MWIP).
/// Esquema DB: catalog.movimientos_mwip
/// Cumple con los requerimientos de la Célula A (PDF 1).
/// </summary>
public class MovimientoMWIP
{
    public Guid Id { get; set; }
    public Guid ProductoId { get; set; }
    public int Cantidad { get; set; }

    /// <summary>
    /// Tipo de movimiento: "EnTransito", "EnProcesamiento", "Acondicionamiento", "InspeccionCalidad"
    /// </summary>
    public string TipoEtapa { get; set; } = "EnTransito";

    public string NumeroLote { get; set; } = string.Empty;
    public string UbicacionOrigen { get; set; } = string.Empty;
    public string UbicacionDestino { get; set; } = string.Empty;

    public string Estado { get; set; } = "EnProgreso"; // "EnProgreso", "Finalizado", "Cancelado"
    public string Observaciones { get; set; } = string.Empty;

    public DateTime FechaInicio { get; set; } = DateTime.UtcNow;
    public DateTime? FechaFinalizacion { get; set; }

    // Navegación
    public Producto? Producto { get; set; }
}
