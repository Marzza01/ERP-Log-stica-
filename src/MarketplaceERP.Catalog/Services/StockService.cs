/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Implementación real de reservas de inventario sobre PostgreSQL.
///
/// Reglas de Fase 4B:
/// - reservar aumenta CantidadReservada, nunca CantidadTotal;
/// - stock bloqueado no está disponible para nuevas reservas;
/// - una reserva puede distribuirse entre múltiples ubicaciones WMS;
/// - liberar devuelve la cantidad pendiente a disponibilidad;
/// - ConfirmarDespachoAsync conserva su nombre legado por compatibilidad con
///   Checkout, pero en esta fase SOLO confirma la reserva. El descuento físico
///   ocurrirá posteriormente en Fulfillment/Outbound.
/// </summary>
internal sealed class StockService : IStockService
{
    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<StockService> _logger;

    public StockService(CatalogDbContext dbContext, ILogger<StockService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReservaResult> ReservarStockAsync(Guid productoId, int cantidad, Guid ordenId)
    {
        if (productoId == Guid.Empty)
            return Fallo("ProductoId es obligatorio.");

        if (ordenId == Guid.Empty)
            return Fallo("OrdenId es obligatorio.");

        if (cantidad <= 0)
            return Fallo("La cantidad a reservar debe ser mayor que cero.");

        await using var transaction = await BeginTransactionIfRelationalAsync();

        try
        {
            // Bloquear los saldos del producto serializa las reservas concurrentes
            // del mismo SKU en PostgreSQL y evita sobreventa.
            var stocks = await GetStocksForUpdateAsync(productoId);

            // Revalidar idempotencia DESPUÉS de tomar el lock del producto.
            var reservaExistente = await _dbContext.ReservasStock
                .AsTracking()
                .FirstOrDefaultAsync(r =>
                    r.ProductoId == productoId &&
                    r.OrdenId == ordenId &&
                    r.Estado != EstadoReserva.Liberada &&
                    r.Estado != EstadoReserva.Expirada &&
                    r.Estado != EstadoReserva.Consumida &&
                    r.Estado != EstadoReserva.Finalizada);

            if (reservaExistente is not null)
            {
                if (reservaExistente.CantidadOriginal == cantidad)
                {
                    await CommitIfNeededAsync(transaction);
                    _logger.LogInformation(
                        "Reserva idempotente reutilizada. ReservaId={ReservaId}, OrdenId={OrdenId}, ProductoId={ProductoId}",
                        reservaExistente.Id, ordenId, productoId);

                    return Exito(reservaExistente.Id);
                }

                await RollbackIfNeededAsync(transaction);
                return Fallo(
                    $"La orden {ordenId} ya tiene una reserva activa de {reservaExistente.CantidadOriginal} unidades para este producto.");
            }

            var disponibles = stocks
                .Where(s => s.CantidadDisponible > 0)
                .OrderBy(s => s.UbicacionCodigo)
                .ThenBy(s => s.Id)
                .ToList();

            var disponibleGlobal = disponibles.Sum(s => s.CantidadDisponible);
            if (disponibleGlobal < cantidad)
            {
                await RollbackIfNeededAsync(transaction);
                _logger.LogWarning(
                    "Stock insuficiente. ProductoId={ProductoId}, Solicitado={Solicitado}, Disponible={Disponible}",
                    productoId, cantidad, disponibleGlobal);

                return Fallo(
                    $"Stock insuficiente. Solicitado: {cantidad}; disponible: {disponibleGlobal}.");
            }

            var ahora = DateTime.UtcNow;
            var reserva = new ReservaStock
            {
                Id = Guid.NewGuid(),
                ProductoId = productoId,
                OrdenId = ordenId,
                CantidadOriginal = cantidad,
                CantidadLiberada = 0,
                CantidadConsumida = 0,
                Estado = EstadoReserva.Pendiente,
                FechaReserva = ahora
            };

            _dbContext.ReservasStock.Add(reserva);

            var restante = cantidad;
            var ubicacionesAsignadas = 0;
            foreach (var stock in disponibles)
            {
                if (restante == 0)
                    break;

                var asignar = Math.Min(restante, stock.CantidadDisponible);
                if (asignar <= 0)
                    continue;

                stock.CantidadReservada += asignar;
                stock.UltimaActualizacion = ahora;

                var detalle = new ReservaStockDetalle
                {
                    Id = Guid.NewGuid(),
                    ReservaStockId = reserva.Id,
                    StockFisicoId = stock.Id,
                    CantidadAsignada = asignar,
                    CantidadLiberada = 0,
                    CantidadConsumida = 0
                };

                var movimiento = new MovimientoInventario
                {
                    Id = Guid.NewGuid(),
                    ProductoId = productoId,
                    TipoMovimiento = TipoMovimientoInventario.Reserva,
                    Cantidad = asignar,
                    UbicacionOrigenId = stock.UbicacionAlmacenId,
                    ReservaStockId = reserva.Id,
                    OrdenId = ordenId,
                    Referencia = $"ORD-{ordenId:N}",
                    Observaciones = "Reserva de stock para orden.",
                    FechaMovimiento = ahora
                };

                _dbContext.ReservasStockDetalle.Add(detalle);
                _dbContext.MovimientosInventario.Add(movimiento);
                ubicacionesAsignadas++;
                restante -= asignar;
            }

            await _dbContext.SaveChangesAsync();
            await CommitIfNeededAsync(transaction);

            _logger.LogInformation(
                "Stock reservado. ReservaId={ReservaId}, ProductoId={ProductoId}, Cantidad={Cantidad}, OrdenId={OrdenId}, Ubicaciones={Ubicaciones}",
                reserva.Id, productoId, cantidad, ordenId, ubicacionesAsignadas);

            return Exito(reserva.Id);
        }
        catch
        {
            await RollbackIfNeededAsync(transaction);
            throw;
        }
    }

    /// <summary>
    /// Método legado del contrato de Fase 3. Durante Fase 4B confirma la reserva
    /// después del pago, pero NO descuenta CantidadTotal ni CantidadReservada.
    /// Fase 4C lo renombrará a ConfirmarReservaAsync.
    /// </summary>
    public async Task ConfirmarDespachoAsync(Guid reservaId)
    {
        if (reservaId == Guid.Empty)
            throw new ArgumentException("ReservaId es obligatorio.", nameof(reservaId));

        await using var transaction = await BeginTransactionIfRelationalAsync();

        try
        {
            var reserva = await GetReservaForUpdateAsync(reservaId);
            if (reserva is null)
                throw new InvalidOperationException($"No existe la reserva {reservaId}.");

            if (reserva.Estado == EstadoReserva.Confirmada)
            {
                await CommitIfNeededAsync(transaction);
                return;
            }

            if (reserva.Estado != EstadoReserva.Pendiente)
            {
                throw new InvalidOperationException(
                    $"La reserva {reservaId} no puede confirmarse desde el estado {reserva.Estado}.");
            }

            reserva.Estado = EstadoReserva.Confirmada;
            reserva.FechaConfirmacion = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            await CommitIfNeededAsync(transaction);

            _logger.LogInformation(
                "Reserva confirmada después del pago. ReservaId={ReservaId}. El físico aún no fue descontado.",
                reservaId);
        }
        catch
        {
            await RollbackIfNeededAsync(transaction);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task LiberarStockAsync(Guid reservaId)
    {
        if (reservaId == Guid.Empty)
            return;

        await using var transaction = await BeginTransactionIfRelationalAsync();

        try
        {
            var reserva = await GetReservaForUpdateAsync(reservaId);
            if (reserva is null)
            {
                _logger.LogWarning(
                    "Liberación idempotente ignorada: reserva inexistente. ReservaId={ReservaId}",
                    reservaId);
                await CommitIfNeededAsync(transaction);
                return;
            }

            if (reserva.CantidadPendiente <= 0)
            {
                await CommitIfNeededAsync(transaction);
                return;
            }

            var stocks = await GetStocksForUpdateAsync(reserva.ProductoId);
            var stockPorId = stocks.ToDictionary(s => s.Id);

            var detalles = await _dbContext.ReservasStockDetalle
                .AsTracking()
                .Where(d => d.ReservaStockId == reservaId)
                .OrderBy(d => d.Id)
                .ToListAsync();

            var ahora = DateTime.UtcNow;
            var totalLiberado = 0;

            foreach (var detalle in detalles)
            {
                var pendienteDetalle = detalle.CantidadPendiente;
                if (pendienteDetalle <= 0)
                    continue;

                if (!stockPorId.TryGetValue(detalle.StockFisicoId, out var stock))
                {
                    throw new InvalidOperationException(
                        $"La reserva {reservaId} referencia un saldo de stock inexistente ({detalle.StockFisicoId}).");
                }

                if (stock.CantidadReservada < pendienteDetalle)
                {
                    throw new InvalidOperationException(
                        $"Inconsistencia de inventario: el saldo {stock.Id} tiene {stock.CantidadReservada} reservadas " +
                        $"pero la reserva requiere liberar {pendienteDetalle}.");
                }

                stock.CantidadReservada -= pendienteDetalle;
                stock.UltimaActualizacion = ahora;
                detalle.CantidadLiberada += pendienteDetalle;
                totalLiberado += pendienteDetalle;

                _dbContext.MovimientosInventario.Add(new MovimientoInventario
                {
                    Id = Guid.NewGuid(),
                    ProductoId = reserva.ProductoId,
                    TipoMovimiento = TipoMovimientoInventario.LiberacionReserva,
                    Cantidad = pendienteDetalle,
                    UbicacionOrigenId = stock.UbicacionAlmacenId,
                    ReservaStockId = reserva.Id,
                    OrdenId = reserva.OrdenId,
                    Referencia = $"ORD-{reserva.OrdenId:N}",
                    Observaciones = "Liberación de stock reservado.",
                    FechaMovimiento = ahora
                });
            }

            reserva.CantidadLiberada += totalLiberado;
            ActualizarEstadoReserva(reserva, ahora);

            await _dbContext.SaveChangesAsync();
            await CommitIfNeededAsync(transaction);

            _logger.LogInformation(
                "Reserva liberada. ReservaId={ReservaId}, CantidadLiberada={CantidadLiberada}, Estado={Estado}",
                reservaId, totalLiberado, reserva.Estado);
        }
        catch
        {
            await RollbackIfNeededAsync(transaction);
            throw;
        }
    }

    private async Task<List<StockFisico>> GetStocksForUpdateAsync(Guid productoId)
    {
        if (IsPostgreSql())
        {
            return await _dbContext.StockFisico
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM catalog.stock_fisico
                    WHERE "ProductoId" = {productoId}
                    ORDER BY "UbicacionCodigo", "Id"
                    FOR UPDATE
                    """)
                .AsTracking()
                .ToListAsync();
        }

        return await _dbContext.StockFisico
            .AsTracking()
            .Where(s => s.ProductoId == productoId)
            .OrderBy(s => s.UbicacionCodigo)
            .ThenBy(s => s.Id)
            .ToListAsync();
    }

    private async Task<ReservaStock?> GetReservaForUpdateAsync(Guid reservaId)
    {
        if (IsPostgreSql())
        {
            return await _dbContext.ReservasStock
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM catalog.reservas_stock
                    WHERE "Id" = {reservaId}
                    FOR UPDATE
                    """)
                .AsTracking()
                .SingleOrDefaultAsync();
        }

        return await _dbContext.ReservasStock
            .AsTracking()
            .SingleOrDefaultAsync(r => r.Id == reservaId);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync()
    {
        if (!_dbContext.Database.IsRelational())
            return null;

        return await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    }

    private static async Task CommitIfNeededAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
            await transaction.CommitAsync();
    }

    private static async Task RollbackIfNeededAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
            await transaction.RollbackAsync();
    }

    private bool IsPostgreSql() =>
        _dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static void ActualizarEstadoReserva(ReservaStock reserva, DateTime ahora)
    {
        var pendiente = reserva.CantidadPendiente;

        if (pendiente > 0)
        {
            if (reserva.CantidadLiberada > 0)
                reserva.Estado = EstadoReserva.ParcialmenteLiberada;
            else if (reserva.CantidadConsumida > 0)
                reserva.Estado = EstadoReserva.ParcialmenteConsumida;

            return;
        }

        if (reserva.CantidadConsumida == reserva.CantidadOriginal)
            reserva.Estado = EstadoReserva.Consumida;
        else if (reserva.CantidadLiberada == reserva.CantidadOriginal)
            reserva.Estado = EstadoReserva.Liberada;
        else if (reserva.CantidadConsumida > 0 && reserva.CantidadLiberada > 0)
            reserva.Estado = EstadoReserva.Finalizada;

        reserva.FechaFinalizacion = ahora;
    }

    private static ReservaResult Exito(Guid reservaId) =>
        new(reservaId, Exitoso: true, MensajeError: null);

    private static ReservaResult Fallo(string mensaje) =>
        new(Guid.Empty, Exitoso: false, MensajeError: mensaje);
}
