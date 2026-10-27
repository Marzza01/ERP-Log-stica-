/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Shared.DTOs;

namespace MarketplaceERP.Core.Interfaces;

/// <summary>
/// Contrato para el servicio de gestión de stock físico y reservas.
/// Implementado por el Equipo 2 (Catalog) en StockServiceMock / StockService.
/// Consumido por el Equipo 3 (Finance) a través del CheckoutOrchestrator.
/// </summary>
public interface IStockService
{
    /// <summary>
    /// Reserva la cantidad especificada de un producto para una orden.
    /// </summary>
    Task<ReservaResult> ReservarStockAsync(Guid productoId, int cantidad, Guid ordenId);

    /// <summary>
    /// Confirma la reserva después de un pago exitoso.
    /// Esta operación NO descuenta stock físico ni consume la reserva; únicamente
    /// cambia su estado a Confirmada para dejarla lista para Fulfillment.
    /// </summary>
    Task ConfirmarReservaAsync(Guid reservaId);

    /// <summary>
    /// Libera una reserva de stock (compensación de la Saga).
    /// Debe llamarse cuando el pago falla DESPUÉS de haber reservado stock.
    /// </summary>
    Task LiberarStockAsync(Guid reservaId);
}
