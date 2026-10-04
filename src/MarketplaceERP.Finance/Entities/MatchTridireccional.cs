/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Finance.Entities;

public enum Estado3WayMatch
{
    Pendiente = 0,
    Conforme = 1,              // Coincidencia exacta (PO == Recepción == Factura)
    DiscrepanciaCantidad = 2,  // La cantidad recibida en WMS no coincide con la factura
    DiscrepanciaPrecio = 3,    // El precio facturado no coincide con la PO
    DiscrepanciaTotal = 4,     // Diferencias en ambos rubros
    Rechazado = 5
}

/// <summary>
/// Registro de auditoría del Match Tridireccional (3-Way Matching).
/// Esquema DB: finance.matches_tridireccionales
/// </summary>
public class MatchTridireccional
{
    public Guid Id { get; set; }
    public string CodigoAuditoria { get; set; } = string.Empty;

    public Guid OrdenCompraId { get; set; }
    public Guid? FacturaProveedorId { get; set; }

    // Cantidades comparadas
    public int CantidadOrdenadaPO { get; set; }
    public int CantidadRecibidaBodega { get; set; }
    public int CantidadFacturada { get; set; }

    // Montos comparados
    public decimal MontoTotalPO { get; set; }
    public decimal MontoTotalFactura { get; set; }
    public decimal DiferenciaMonto => Math.Abs(MontoTotalPO - MontoTotalFactura);

    public Estado3WayMatch Resultado { get; set; } = Estado3WayMatch.Pendiente;
    public bool AutorizadoParaPago { get; set; } = false;

    public string DetalleDiscrepancias { get; set; } = string.Empty;
    public string Auditor { get; set; } = "Sistema Automatizado 3-Way Match";
    public DateTime FechaAuditoria { get; set; } = DateTime.UtcNow;

    // Navegación
    public OrdenCompra? OrdenCompra { get; set; }
    public FacturaProveedor? FacturaProveedor { get; set; }
}
