/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Mock del servicio de stock para el arranque inicial del proyecto.
/// Simula operaciones de reserva, confirmación y liberación de stock en memoria.
/// REEMPLAZAR por StockService real cuando Equipo 2 implemente la lógica de negocio.
/// </summary>
internal sealed class StockServiceMock : IStockService
{
    private readonly ILogger<StockServiceMock> _logger;

    // Almacén en memoria para simular reservas activas
    private static readonly Dictionary<Guid, (Guid ProductoId, int Cantidad, Guid OrdenId)> _reservasActivas = new();
    private static readonly object _lock = new();

    public StockServiceMock(ILogger<StockServiceMock> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ReservaResult> ReservarStockAsync(Guid productoId, int cantidad, Guid ordenId)
    {
        _logger.LogInformation(
            "[MOCK] ReservarStock: ProductoId={ProductoId}, Cantidad={Cantidad}, OrdenId={OrdenId}",
            productoId, cantidad, ordenId);

        // Simula siempre éxito para que el flujo de checkout pueda completarse en desarrollo
        var reservaId = Guid.NewGuid();

        lock (_lock)
        {
            _reservasActivas[reservaId] = (productoId, cantidad, ordenId);
        }

        return Task.FromResult(new ReservaResult(
            ReservaId: reservaId,
            Exitoso: true,
            MensajeError: null
        ));
    }

    /// <inheritdoc />
    public Task ConfirmarDespachoAsync(Guid reservaId)
    {
        _logger.LogInformation("[MOCK] ConfirmarDespacho: ReservaId={ReservaId}", reservaId);

        lock (_lock)
        {
            _reservasActivas.Remove(reservaId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LiberarStockAsync(Guid reservaId)
    {
        _logger.LogWarning(
            "[MOCK] LiberarStock (compensación Saga): ReservaId={ReservaId}", reservaId);

        lock (_lock)
        {
            _reservasActivas.Remove(reservaId);
        }

        return Task.CompletedTask;
    }
}
