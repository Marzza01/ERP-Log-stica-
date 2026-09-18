namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Ítem de detalle de una Orden de Compra a proveedor.
/// Esquema DB: finance.items_orden_compra
/// </summary>
public class ItemOrdenCompra
{
    public Guid Id { get; set; }
    public Guid OrdenCompraId { get; set; }

    public Guid ProductoId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NombreProducto { get; set; } = string.Empty;

    public int CantidadSolicitada { get; set; }
    public int CantidadRecibida { get; set; } = 0;
    public decimal PrecioUnitarioAcordado { get; set; }
    public decimal TotalLinea => CantidadSolicitada * PrecioUnitarioAcordado;

    // Navegación
    public OrdenCompra OrdenCompra { get; set; } = null!;
}
