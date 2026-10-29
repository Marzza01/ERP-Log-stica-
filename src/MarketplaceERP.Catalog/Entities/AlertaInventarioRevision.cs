/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

public enum TipoAlertaInventario
{
    LowStock,
    CriticalStock,
    OutOfStock
}

/// <summary>
/// Persistencia mínima del reconocimiento de una alerta derivada.
/// La alerta no se almacena como hecho independiente: se recalcula desde el stock real.
/// Cuando la condición cambia o se resuelve, esta revisión deja de aplicar y se elimina.
/// </summary>
public class AlertaInventarioRevision
{
    public Guid Id { get; set; }
    public Guid ProductoId { get; set; }
    public TipoAlertaInventario Tipo { get; set; }
    public DateTime ReconocidaEn { get; set; } = DateTime.UtcNow;
    public string ReconocidaPor { get; set; } = string.Empty;

    public Producto Producto { get; set; } = null!;
}
