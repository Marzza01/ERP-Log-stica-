namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Stock físico disponible de un producto en el almacén.
/// Esquema DB: catalog.stock_fisico
/// Relación 1:1 con Producto.
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

    public DateTime UltimaActualizacion { get; set; } = DateTime.UtcNow;

    // Navegación
    public Producto Producto { get; set; } = null!;
}
