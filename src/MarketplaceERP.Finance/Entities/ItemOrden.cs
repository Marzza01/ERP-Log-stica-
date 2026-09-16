namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Ítem individual dentro de una orden de compra.
/// Esquema DB: finance.items_orden
/// Nota: ProductoId es referencia cross-schema (Guid, sin FK entre esquemas).
/// </summary>
public class ItemOrden
{
    public Guid Id { get; set; }
    public Guid OrdenId { get; set; }

    /// <summary>
    /// Referencia al producto (cross-schema, sin FK en DB).
    /// </summary>
    public Guid ProductoId { get; set; }

    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal => Cantidad * PrecioUnitario;

    // Navegación
    public Orden Orden { get; set; } = null!;
}
