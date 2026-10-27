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
    /// NOTA TRANSITORIA FASE 4B: el nombre legado ConfirmarDespachoAsync se conserva
    /// para no romper Checkout; este método NO descuenta stock físico. En Fase 4C
    /// se renombrará a ConfirmarReservaAsync.
    /// </summary>
    Task ConfirmarDespachoAsync(Guid reservaId);

    /// <summary>
    /// Libera una reserva de stock (compensación de la Saga).
    /// Debe llamarse cuando el pago falla DESPUÉS de haber reservado stock.
    /// </summary>
    Task LiberarStockAsync(Guid reservaId);
}
