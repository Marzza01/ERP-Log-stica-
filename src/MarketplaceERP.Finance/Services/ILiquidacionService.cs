using MarketplaceERP.Finance.Entities;

namespace MarketplaceERP.Finance.Services;

public record LiquidacionResumen(
    Guid LiquidacionId,
    Guid VendedorId,
    decimal MontoLiquidado,
    string Estado,
    DateTime FechaLiquidacion);

public record KpiVendedor(
    Guid VendedorId,
    int TotalOrdenes,
    int OrdenesEntregadas,
    decimal TotalLiquidado,
    double FillRate);

public interface ILiquidacionService
{
    /// <summary>
    /// Genera la liquidación periódica de un vendedor a partir de sus SplitPagos pendientes.
    /// Registra el asiento contable correspondiente (Débito CxP Vendedores / Crédito Caja).
    /// </summary>
    Task<Liquidacion> GenerarLiquidacionAsync(Guid vendedorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna el historial de liquidaciones filtrable por vendedor.
    /// </summary>
    Task<IEnumerable<Liquidacion>> ObtenerHistorialAsync(Guid? vendedorId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Procesa el pago de una liquidación pendiente y actualiza su estado a Completada.
    /// </summary>
    Task<Liquidacion> ProcesarPagoAsync(Guid liquidacionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumen de KPIs por vendedor: total órdenes, fill rate y montos liquidados.
    /// </summary>
    Task<IEnumerable<KpiVendedor>> ObtenerKpisVendedoresAsync(CancellationToken cancellationToken = default);
}
