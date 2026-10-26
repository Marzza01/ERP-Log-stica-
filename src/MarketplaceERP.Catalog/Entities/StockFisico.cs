/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Saldo físico de un producto en una ubicación WMS concreta.
/// Esquema DB: catalog.stock_fisico
/// A partir de Fase 4A un producto puede tener varios saldos, uno por ubicación.
/// </summary>
public class StockFisico
{
    public Guid Id { get; set; }
    public Guid ProductoId { get; set; }

    /// <summary>
    /// Unidades totales físicamente presentes en esta ubicación.
    /// Incluye unidades reservadas y bloqueadas.
    /// </summary>
    public int CantidadTotal { get; set; }

    /// <summary>
    /// Unidades asignadas a órdenes pendientes de despacho.
    /// Reservar no reduce CantidadTotal.
    /// </summary>
    public int CantidadReservada { get; set; }

    /// <summary>
    /// Unidades físicamente presentes pero no vendibles temporalmente
    /// (daño, cuarentena, inspección u otra restricción operativa).
    /// </summary>
    public int CantidadBloqueada { get; set; }

    /// <summary>
    /// Unidades disponibles para nuevas operaciones.
    /// </summary>
    public int CantidadDisponible => CantidadTotal - CantidadReservada - CantidadBloqueada;

    /// <summary>
    /// Campo heredado de compatibilidad. La regla global de reorden utiliza Producto.StockMinimo.
    /// </summary>
    public int PuntoReorden { get; set; } = 15;

    /// <summary>
    /// Indicador local de compatibilidad para el saldo de esta ubicación.
    /// </summary>
    public bool EsBajoMinimo => CantidadDisponible <= PuntoReorden;

    /// <summary>
    /// Ubicación física en el WMS.
    /// </summary>
    public Guid? UbicacionAlmacenId { get; set; }
    public string UbicacionCodigo { get; set; } = "PAS-A-RCK-01-NIV-01-POS-01";

    public DateTime UltimaActualizacion { get; set; } = DateTime.UtcNow;

    // Navegación
    public Producto Producto { get; set; } = null!;
    public UbicacionAlmacen? UbicacionAlmacen { get; set; }
    public ICollection<ReservaStockDetalle> DetallesReserva { get; set; } = new List<ReservaStockDetalle>();
}
