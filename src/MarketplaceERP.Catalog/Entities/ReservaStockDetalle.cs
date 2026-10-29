/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Distribución de una reserva entre saldos/ubicaciones WMS concretas.
/// Esquema DB: catalog.reservas_stock_detalle
/// </summary>
public class ReservaStockDetalle
{
    public Guid Id { get; set; }
    public Guid ReservaStockId { get; set; }
    public Guid StockFisicoId { get; set; }

    public int CantidadAsignada { get; set; }
    public int CantidadLiberada { get; set; }
    public int CantidadConsumida { get; set; }

    public int CantidadPendiente => CantidadAsignada - CantidadLiberada - CantidadConsumida;

    public ReservaStock ReservaStock { get; set; } = null!;
    public StockFisico StockFisico { get; set; } = null!;
}
