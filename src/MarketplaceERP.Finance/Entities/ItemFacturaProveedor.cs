namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Línea declarada en factura proveedor (cantidad y precio según documento
/// recibido); NO inferirlos de la PO ni de la recepción WMS.
/// </summary>
public sealed class ItemFacturaProveedor
{
    public Guid Id { get; set; }
    public Guid FacturaProveedorId { get; set; }
    public Guid ItemOrdenCompraId { get; set; }
    public int CantidadFacturada { get; set; }
    public decimal PrecioUnitarioFacturado { get; set; }

    public FacturaProveedor FacturaProveedor { get; set; } = null!;
    public ItemOrdenCompra ItemOrdenCompra { get; set; } = null!;
}
