/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Resultado de una recepción física de inventario.
/// </summary>
public sealed record RecepcionInventarioResult(
    bool Exitoso,
    bool YaProcesada,
    Guid ProductoId,
    Guid StockFisicoId,
    int CantidadTotalUbicacion,
    string Mensaje);

/// <summary>
/// Operaciones físicas de inventario independientes de Checkout y Finance.
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Registra una recepción Inbound en una ubicación WMS concreta.
    /// La operación es idempotente por OperacionId + ItemOrdenCompraId.
    /// </summary>
    Task<RecepcionInventarioResult> RecibirInboundAsync(
        string sku,
        int cantidad,
        Guid ubicacionAlmacenId,
        Guid ordenCompraId,
        Guid itemOrdenCompraId,
        Guid operacionId,
        string? notas = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reingresa inventario disponible proveniente de un RMA clasificado como ReStock.
    /// Un mismo RMA solo puede generar un reingreso físico; reintentos son idempotentes.
    /// </summary>
    Task<RecepcionInventarioResult> RecibirRmaRestockAsync(
        Guid rmaId,
        Guid productoId,
        int cantidad,
        Guid ubicacionAlmacenId,
        Guid operacionId,
        string? notas = null,
        CancellationToken cancellationToken = default);
}
