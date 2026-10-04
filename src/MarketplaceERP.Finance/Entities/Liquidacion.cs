/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Estados posibles de una liquidación a un vendedor.
/// </summary>
public enum EstadoLiquidacion
{
    Pendiente = 0,
    EnProceso = 1,
    Completada = 2,
    Fallida = 3
}

/// <summary>
/// Liquidación periódica de fondos hacia un vendedor.
/// Esquema DB: finance.liquidaciones
/// Nota: VendedorId es referencia cross-schema (Guid, sin FK en DB).
/// </summary>
public class Liquidacion
{
    public Guid Id { get; set; }

    /// <summary>
    /// Referencia al vendedor a liquidar (cross-schema, sin FK en DB).
    /// </summary>
    public Guid VendedorId { get; set; }

    public Guid SplitPagoId { get; set; }
    public decimal MontoLiquidar { get; set; }
    public string Moneda { get; set; } = "USD";
    public EstadoLiquidacion Estado { get; set; } = EstadoLiquidacion.Pendiente;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaLiquidacion { get; set; }

    // Navegación
    public SplitPago SplitPago { get; set; } = null!;
}
