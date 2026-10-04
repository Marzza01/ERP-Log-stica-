/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Core.Interfaces;

/// <summary>
/// Contrato para el cálculo de comisiones del marketplace.
/// Implementado por el Equipo 1 (Core) en CommissionServiceMock / CommissionService.
/// Consumido por el Equipo 3 (Finance) dentro del CheckoutOrchestrator.
/// </summary>
public interface ICommissionService
{
    /// <summary>
    /// Calcula la comisión que el marketplace retiene de una venta.
    /// </summary>
    /// <param name="vendedorId">Identificador del vendedor.</param>
    /// <param name="montoTotal">Monto total de la venta antes de la comisión.</param>
    /// <returns>Monto de la comisión a retener (siempre positivo).</returns>
    Task<decimal> CalcularComisionAsync(Guid vendedorId, decimal montoTotal);
}
