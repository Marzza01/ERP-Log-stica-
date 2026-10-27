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
    /// Confirma que el stock reservado fue despachado exitosamente.
    /// Debe llamarse SOLO después de que el pago fue procesado con éxito.
    /// </summary>
    Task ConfirmarDespachoAsync(Guid reservaId);

    /// <summary>
    /// Pasa todas las reservas pendientes asociadas a la orden dada al estado Comprometida
    /// y elimina su fecha de expiración para evitar que el job periódico las purgue.
    /// Se invoca tras completar con éxito el commit de la orden pagada en el OMS/Checkout.
    /// </summary>
    Task ComprometerReservasOrdenAsync(Guid ordenId);

    /// <summary>
    /// Libera una reserva de stock (compensación de la Saga).
    /// Debe llamarse cuando el pago falla DESPUÉS de haber reservado stock.
    /// </summary>
    Task LiberarStockAsync(Guid reservaId);
}
