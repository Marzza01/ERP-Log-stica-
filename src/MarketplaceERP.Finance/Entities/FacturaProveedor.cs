/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Factura / Comprobante de Crédito Fiscal emitido por el proveedor para el 3-Way Match.
/// Esquema DB: finance.facturas_proveedor
/// </summary>
public class FacturaProveedor
{
    public Guid Id { get; set; }
    public string NumeroFactura { get; set; } = string.Empty;

    public Guid OrdenCompraId { get; set; }
    public Guid ProveedorId { get; set; }

    public decimal MontoSubTotal { get; set; }
    public decimal MontoIva { get; set; }
    public decimal MontoTotal { get; set; }

    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
    public DateTime FechaVencimiento { get; set; }

    public bool AprobadaParaPago { get; set; } = false;
    public bool Pagada { get; set; } = false;
    public DateTime? FechaPago { get; set; }

    // Navegación
    public OrdenCompra OrdenCompra { get; set; } = null!;
}
