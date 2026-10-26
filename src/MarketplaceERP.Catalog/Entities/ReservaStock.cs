/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Estados posibles de una reserva de stock.
/// </summary>
public enum EstadoReserva
{
    Pendiente = 0,
    Confirmada = 1,
    ParcialmenteLiberada = 2,
    Liberada = 3,
    ParcialmenteConsumida = 4,
    Consumida = 5,
    Finalizada = 6,
    Expirada = 7,

    /// <summary>
    /// Estado legado conservado temporalmente para poder leer datos antiguos.
    /// La migración de Fase 4A lo convertirá a Consumida.
    /// </summary>
    Despachada = 8
}

/// <summary>
/// Reserva de unidades de un producto para una orden específica.
/// Esquema DB: catalog.reservas_stock
/// </summary>
public class ReservaStock
{
    public Guid Id { get; set; }
    public Guid ProductoId { get; set; }
    public Guid OrdenId { get; set; }

    /// <summary>Cantidad solicitada originalmente.</summary>
    public int CantidadOriginal { get; set; }

    /// <summary>Cantidad devuelta a disponibilidad sin ser despachada.</summary>
    public int CantidadLiberada { get; set; }

    /// <summary>Cantidad consumida por despacho físico.</summary>
    public int CantidadConsumida { get; set; }

    /// <summary>Cantidad todavía reservada y pendiente de resolver.</summary>
    public int CantidadPendiente => CantidadOriginal - CantidadLiberada - CantidadConsumida;

    public EstadoReserva Estado { get; set; } = EstadoReserva.Pendiente;
    public DateTime FechaReserva { get; set; } = DateTime.UtcNow;
    public DateTime? FechaExpiracion { get; set; }
    public DateTime? FechaConfirmacion { get; set; }
    public DateTime? FechaFinalizacion { get; set; }

    // Navegación
    public Producto Producto { get; set; } = null!;
    public ICollection<ReservaStockDetalle> Detalles { get; set; } = new List<ReservaStockDetalle>();
    public ICollection<MovimientoInventario> Movimientos { get; set; } = new List<MovimientoInventario>();
}
