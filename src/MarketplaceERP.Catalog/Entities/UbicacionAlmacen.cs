/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Mapeo dinámico de bodega (WMS): Pasillos, Racks, Niveles y Posiciones de Almacenamiento.
/// Esquema DB: catalog.ubicaciones_almacen
/// Cumple con los requisitos de la Célula A (PDF 1 y PDF 2).
/// </summary>
public class UbicacionAlmacen
{
    public Guid Id { get; set; }

    /// <summary>
    /// Código compuesto unificado: ej. "PAS-A-RCK-01-NIV-02-POS-05"
    /// </summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>
    /// Pasillo de bodega (ej. "Pasillo A", "Pasillo B")
    /// </summary>
    public string Pasillo { get; set; } = string.Empty;

    /// <summary>
    /// Estantería o Rack (ej. "Rack 01", "Rack 02")
    /// </summary>
    public string Rack { get; set; } = string.Empty;

    /// <summary>
    /// Altura o nivel vertical en el rack (1 al 5)
    /// </summary>
    public int Nivel { get; set; }

    /// <summary>
    /// Posición o casilla específica dentro del nivel (ej. "Posición 01")
    /// </summary>
    public string Posicion { get; set; } = string.Empty;

    /// <summary>
    /// Capacidad máxima en unidades físicas para esta posición
    /// </summary>
    public int CapacidadMaxima { get; set; } = 1000;

    /// <summary>
    /// Indicador si la posición tiene productos almacenados
    /// </summary>
    public bool EstaOcupada { get; set; } = false;

    /// <summary>
    /// Zona de almacenamiento: "Recepcion", "Picking", "AlmacenamientoGeneral", "CrossDocking", "Cuarentena"
    /// </summary>
    public string Zona { get; set; } = "AlmacenamientoGeneral";

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    // Relación con stock físico almacenado
    public ICollection<StockFisico> StocksAlmacenados { get; set; } = new List<StockFisico>();
}
