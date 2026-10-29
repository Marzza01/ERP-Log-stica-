/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Mock legado del servicio de stock, conservado únicamente para pruebas aisladas.
/// La aplicación registra StockService real mediante DI.
/// Simula reserva, confirmación y liberación respetando la semántica actual:
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
    public Task ConfirmarReservaAsync(Guid reservaId)
    {
        _logger.LogInformation("[MOCK] ConfirmarReserva: ReservaId={ReservaId}", reservaId);

        // Confirmar NO consume ni libera la reserva; permanece comprometida
        // hasta que Fulfillment la consuma o una compensación la libere.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ConsumirReservasOrdenAsync(Guid ordenId)
    {
        _logger.LogInformation("[MOCK] ConsumirReservasOrden: OrdenId={OrdenId}", ordenId);

        lock (_lock)
        {
            var ids = _reservasActivas
                .Where(kv => kv.Value.OrdenId == ordenId)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var id in ids)
                _reservasActivas.Remove(id);
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
