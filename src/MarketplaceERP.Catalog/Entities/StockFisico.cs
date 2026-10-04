/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Stock físico disponible de un producto en el almacén central de LOGIMKT.
/// Esquema DB: catalog.stock_fisico
/// Integra mapeo a WMS dinámico y control de alertas de reorden.
/// </summary>
public class StockFisico
{
    public Guid Id { get; set; }
    public Guid ProductoId { get; set; }

    /// <summary>
    /// Unidades totales en almacén (incluyendo las reservadas).
    /// </summary>
    public int CantidadTotal { get; set; }

    /// <summary>
    /// Unidades actualmente reservadas (bloqueadas por órdenes pendientes).
    /// </summary>
    public int CantidadReservada { get; set; }

    /// <summary>
    /// Unidades disponibles para nuevas ventas.
    /// </summary>
    public int CantidadDisponible => CantidadTotal - CantidadReservada;

    /// <summary>
    /// Punto de reorden / stock mínimo configurado
    /// </summary>
    public int PuntoReorden { get; set; } = 15;

    /// <summary>
    /// Alerta si el inventario actual cayó por debajo del punto de reorden
    /// </summary>
    public bool EsBajoMinimo => CantidadDisponible <= PuntoReorden;

    /// <summary>
    /// Ubicación física en el WMS
    /// </summary>
    public Guid? UbicacionAlmacenId { get; set; }
    public string UbicacionCodigo { get; set; } = "PAS-A-RCK-01-NIV-01-POS-01";

    public DateTime UltimaActualizacion { get; set; } = DateTime.UtcNow;

    // Navegación
    public Producto Producto { get; set; } = null!;
    public UbicacionAlmacen? UbicacionAlmacen { get; set; }
}
