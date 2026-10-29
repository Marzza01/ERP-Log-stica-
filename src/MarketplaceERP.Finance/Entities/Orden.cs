/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Estados del ciclo de vida del pedido en el OMS (PDF 1 y PDF 2).
/// Pendiente → Aprobada → EnProceso → Enviada → Entregada → Cancelada
/// </summary>
public enum EstadoOrden
{
    Pendiente    = 0,
    Aprobada     = 1,
    EnProceso    = 2,
    Enviada      = 3,
    Entregada    = 4,
    Cancelada    = 5,
}

/// <summary>
/// Canal de venta / tipo de cliente
/// </summary>
public enum TipoCliente
{
    B2C = 0, // Consumidor final (desde $5, pago inmediato)
    B2B = 1  // Empresas/Distribuidores (mínimo $500, Crédito Fiscal CCF, crédito)
}

/// <summary>
/// Orden de compra / pedido gestionado por el OMS.
/// Esquema DB: finance.ordenes
/// </summary>
public class Orden
{
    public Guid Id { get; set; }

    /// <summary>Número de orden autogenerado, e.g. ORD-20240101-12345</summary>
    public string NumeroOrden { get; set; } = string.Empty;

    public Guid CompradorId { get; set; }
    public Guid VendedorId { get; set; }

    // Identidad de la operación, única por comprador en PostgreSQL.
    // Nullable para no alterar órdenes existentes ni los flujos ajenos a Checkout.
    public string? IdempotencyKey { get; set; }
    public string? CheckoutPayloadHash { get; set; }

    public decimal SubTotal { get; set; }
    public decimal ImpuestoIva { get; set; } // IVA 13% en El Salvador
    public decimal CostoEnvio { get; set; }
    public decimal MontoTotal { get; set; }
    public string Moneda { get; set; } = "USD";

    public EstadoOrden Estado { get; set; } = EstadoOrden.Pendiente;
    public TipoCliente TipoCliente { get; set; } = TipoCliente.B2C;

    // Facturación y Tributación salvadoreña
    public string TipoComprobante { get; set; } = "FacturaConsumidorFinal"; // "FacturaConsumidorFinal", "CreditoFiscalCCF"
    public string NumeroComprobanteFiscal { get; set; } = string.Empty;
    public string CondicionPago { get; set; } = "Inmediato"; // "Inmediato", "Credito30", "Credito60"

    // Logística y Last-Mile
    public string Transportista { get; set; } = "LOGIMKT Express";
    public string GuiaEnvio { get; set; } = string.Empty;
    public string ZonaEnvio { get; set; } = "San Salvador y AMSS";
    public string DireccionEntrega { get; set; } = string.Empty;

    // Trazabilidad de tiempos para KPIs de Lead Time y OTIF
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaAprobacion { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public DateTime? FechaEntregaEstimada { get; set; }
    public DateTime? FechaEntregaReal { get; set; }
    public DateTime? FechaCancelacion { get; set; }
    public DateTime? FechaCompletada => FechaEntregaReal;

    /// <summary>Indicador si la orden cumplió On-Time In-Full (OTIF)</summary>
    public bool? CumplioOTIF { get; set; }

    // Navegación
    public ICollection<ItemOrden> Items { get; set; } = new List<ItemOrden>();
    public SplitPago? SplitPago { get; set; }
}
