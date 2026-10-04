/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

public enum EstadoOrdenCompra
{
    Borrador         = 0,
    Enviada          = 1,
    RecibidaParcial  = 2,
    RecibidaCompleta = 3,
    Facturada        = 4,
    Pagada           = 5,
    Cancelada        = 6
}

/// <summary>
/// Orden de Compra (PO - Purchase Order) para abastecimiento y aprovisionamiento (Célula B - PDF 1 y PDF 2).
/// Esquema DB: finance.ordenes_compra
/// </summary>
public class OrdenCompra
{
    public Guid Id { get; set; }
    public string NumeroPO { get; set; } = string.Empty;

    public Guid ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = string.Empty;

    public EstadoOrdenCompra Estado { get; set; } = EstadoOrdenCompra.Borrador;
    public decimal SubTotal { get; set; }
    public decimal ImpuestoIva { get; set; } // 13% en El Salvador
    public decimal MontoTotal { get; set; }
    public string Moneda { get; set; } = "USD";

    public string TerminosPago { get; set; } = "Crédito 30 días";
    public string Observaciones { get; set; } = string.Empty;

    public DateTime FechaEmision { get; set; } = DateTime.UtcNow;
    public DateTime? FechaEntregaEsperada { get; set; }
    public DateTime? FechaEnvioProveedor { get; set; }
    public DateTime? FechaRecepcionReal { get; set; }

    // Navegación
    public ICollection<ItemOrdenCompra> Items { get; set; } = new List<ItemOrdenCompra>();
    public ICollection<FacturaProveedor> Facturas { get; set; } = new List<FacturaProveedor>();
    public ICollection<MatchTridireccional> Matches3Way { get; set; } = new List<MatchTridireccional>();
}
