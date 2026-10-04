/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Distribución del pago entre el vendedor y el marketplace (comisión).
/// Esquema DB: finance.splits_pago
/// Relación 1:1 con Orden.
/// </summary>
public class SplitPago
{
    public Guid Id { get; set; }
    public Guid OrdenId { get; set; }

    public decimal MontoTotal { get; set; }

    /// <summary>
    /// Porcentaje destinado al vendedor (ej. 90.00 = 90%).
    /// </summary>
    public decimal PorcentajeVendedor { get; set; } = 90m;

    /// <summary>
    /// Monto calculado para el vendedor.
    /// </summary>
    public decimal MontoVendedor { get; set; }

    /// <summary>
    /// Porcentaje retenido por el marketplace como comisión (ej. 10.00 = 10%).
    /// </summary>
    public decimal PorcentajeMarketplace { get; set; } = 10m;

    /// <summary>
    /// Monto de la comisión retenida por el marketplace.
    /// </summary>
    public decimal MontoMarketplace { get; set; }

    public string TransaccionId { get; set; } = string.Empty;
    public DateTime FechaProcesado { get; set; } = DateTime.UtcNow;

    // Navegación
    public Orden Orden { get; set; } = null!;
    public Liquidacion? Liquidacion { get; set; }
}
