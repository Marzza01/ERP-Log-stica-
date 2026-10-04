/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Ítem individual dentro de una orden de venta (OMS).
/// Esquema DB: finance.items_orden
/// Nota: ProductoId es referencia cross-schema (Guid, sin FK entre esquemas).
/// </summary>
public class ItemOrden
{
    public Guid Id { get; set; }
    public Guid OrdenId { get; set; }

    /// <summary>Referencia al producto (cross-schema, sin FK en DB).</summary>
    public Guid ProductoId { get; set; }

    /// <summary>SKU desnormalizado para lectura rápida sin join cross-schema.</summary>
    public string SkuReferencia { get; set; } = string.Empty;

    /// <summary>Nombre del producto desnormalizado.</summary>
    public string NombreProducto { get; set; } = string.Empty;

    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal => Cantidad * PrecioUnitario;

    // Navegación
    public Orden Orden { get; set; } = null!;
}
