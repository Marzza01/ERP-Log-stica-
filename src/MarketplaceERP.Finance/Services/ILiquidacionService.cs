/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

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
    /// Genera un registro interno pendiente de liquidación para un SplitPago del vendedor.
    /// No genera asiento ni ejecuta transferencia bancaria en esta etapa.
    /// </summary>
    Task<Liquidacion> GenerarLiquidacionAsync(Guid vendedorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna el historial de liquidaciones filtrable por vendedor.
    /// </summary>
    Task<IEnumerable<Liquidacion>> ObtenerHistorialAsync(Guid? vendedorId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marca como Completada la simulación de registro contable de una liquidación pendiente.
    /// No envía ni confirma transferencias bancarias; el flujo real no está implementado.
    /// </summary>
    Task<Liquidacion> ProcesarPagoAsync(Guid liquidacionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumen de KPIs por vendedor: total órdenes, fill rate y montos liquidados.
    /// </summary>
    Task<IEnumerable<KpiVendedor>> ObtenerKpisVendedoresAsync(CancellationToken cancellationToken = default);
}
