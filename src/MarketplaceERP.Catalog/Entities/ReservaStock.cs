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
    Liberada = 2,
    Despachada = 3,
    Comprometida = 4
}

/// <summary>
/// Reserva temporal de unidades de un producto para una orden específica.
/// Esquema DB: catalog.reservas_stock
/// </summary>
public class ReservaStock
{
    public Guid Id { get; set; }
    public Guid ProductoId { get; set; }
    public Guid OrdenId { get; set; }
    public int Cantidad { get; set; }
    public EstadoReserva Estado { get; set; } = EstadoReserva.Pendiente;
    public DateTime FechaReserva { get; set; } = DateTime.UtcNow;
    public DateTime? FechaExpiracion { get; set; }
    public DateTime? FechaConfirmacion { get; set; }

    // Navegación
    public Producto Producto { get; set; } = null!;
}
