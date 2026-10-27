/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Núcleo de mutaciones físicas de inventario.
/// Fase 4E incorpora recepción Inbound real desde Compras hacia StockFisico.
/// </summary>
internal sealed class InventoryService : IInventoryService
{
    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(CatalogDbContext dbContext, ILogger<InventoryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<RecepcionInventarioResult> RecibirInboundAsync(
        string sku,
        int cantidad,
        Guid ubicacionAlmacenId,
        Guid ordenCompraId,
        Guid itemOrdenCompraId,
        Guid operacionId,
        string? notas = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return Fallo("SKU es obligatorio.");
        if (cantidad <= 0)
            return Fallo("La cantidad recibida debe ser mayor que cero.");
        if (ubicacionAlmacenId == Guid.Empty)
            return Fallo("La ubicación WMS es obligatoria.");
        if (ordenCompraId == Guid.Empty)
            return Fallo("OrdenCompraId es obligatorio.");
        if (itemOrdenCompraId == Guid.Empty)
            return Fallo("ItemOrdenCompraId es obligatorio.");
        if (operacionId == Guid.Empty)
            return Fallo("OperacionId es obligatorio.");

        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            // Bloquear primero el producto serializa recepciones concurrentes del mismo SKU
            // incluso si intentan usar ubicaciones diferentes.
            var producto = await GetProductoForUpdateBySkuAsync(sku, cancellationToken);
            if (producto is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo($"No existe un producto de catálogo con SKU '{sku}'.");
            }

            var ubicacion = await GetUbicacionForUpdateAsync(ubicacionAlmacenId, cancellationToken);
            if (ubicacion is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo($"La ubicación WMS {ubicacionAlmacenId} no existe.");
            }

            var referencia = ConstruirReferencia(operacionId, itemOrdenCompraId);

            // La ubicación queda bloqueada en PostgreSQL antes de comprobar idempotencia,
            // evitando que dos recepciones concurrentes con el mismo token dupliquen stock.
            var movimientoExistente = await _dbContext.MovimientosInventario
                .AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound &&
                    m.Referencia == referencia,
                    cancellationToken);

            if (movimientoExistente is not null)
            {
                if (movimientoExistente.ProductoId != producto.Id ||
                    movimientoExistente.Cantidad != cantidad ||
                    movimientoExistente.UbicacionDestinoId != ubicacionAlmacenId ||
                    movimientoExistente.OrdenId != ordenCompraId)
                {
                    await RollbackIfNeededAsync(transaction, cancellationToken);
                    return Fallo("OperacionId ya fue utilizado con datos diferentes.");
                }

                var stockExistente = await _dbContext.StockFisico
                    .AsNoTracking()
                    .SingleOrDefaultAsync(s =>
                        s.ProductoId == producto.Id &&
                        s.UbicacionAlmacenId == ubicacionAlmacenId,
                        cancellationToken);

                await CommitIfNeededAsync(transaction, cancellationToken);

                return new RecepcionInventarioResult(
                    true,
                    true,
                    producto.Id,
                    stockExistente?.Id ?? Guid.Empty,
                    stockExistente?.CantidadTotal ?? 0,
                    "La recepción ya había sido procesada; no se duplicó stock.");
            }

            var ocupacionActual = await _dbContext.StockFisico
                .Where(s => s.UbicacionAlmacenId == ubicacionAlmacenId)
                .SumAsync(s => (int?)s.CantidadTotal, cancellationToken) ?? 0;

            if (ocupacionActual + cantidad > ubicacion.CapacidadMaxima)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo(
                    $"La recepción excede la capacidad de {ubicacion.Codigo}. " +
                    $"Ocupación actual: {ocupacionActual}; capacidad: {ubicacion.CapacidadMaxima}; recibido: {cantidad}.");
            }

            var stock = await _dbContext.StockFisico
                .AsTracking()
                .SingleOrDefaultAsync(s =>
                    s.ProductoId == producto.Id &&
                    s.UbicacionAlmacenId == ubicacionAlmacenId,
                    cancellationToken);

            var ahora = DateTime.UtcNow;
            if (stock is null)
            {
                stock = new StockFisico
                {
                    Id = Guid.NewGuid(),
                    ProductoId = producto.Id,
                    CantidadTotal = 0,
                    CantidadReservada = 0,
                    CantidadBloqueada = 0,
                    PuntoReorden = producto.StockMinimo,
                    UbicacionAlmacenId = ubicacion.Id,
                    UbicacionCodigo = ubicacion.Codigo,
                    UltimaActualizacion = ahora
                };
                _dbContext.StockFisico.Add(stock);
            }

            stock.CantidadTotal += cantidad;
            stock.UbicacionCodigo = ubicacion.Codigo;
            stock.UltimaActualizacion = ahora;
            ubicacion.EstaOcupada = true;

            _dbContext.MovimientosInventario.Add(new MovimientoInventario
            {
                Id = Guid.NewGuid(),
                ProductoId = producto.Id,
                TipoMovimiento = TipoMovimientoInventario.RecepcionInbound,
                Cantidad = cantidad,
                UbicacionDestinoId = ubicacion.Id,
                OrdenId = ordenCompraId,
                Referencia = referencia,
                Observaciones = string.IsNullOrWhiteSpace(notas)
                    ? $"Recepción Inbound de PO {ordenCompraId:N}."
                    : notas.Trim(),
                FechaMovimiento = ahora
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfNeededAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "Recepción Inbound aplicada. SKU={Sku}, ProductoId={ProductoId}, Cantidad={Cantidad}, Ubicacion={Ubicacion}, PO={PO}, ItemPO={ItemPO}",
                sku, producto.Id, cantidad, ubicacion.Codigo, ordenCompraId, itemOrdenCompraId);

            return new RecepcionInventarioResult(
                true,
                false,
                producto.Id,
                stock.Id,
                stock.CantidadTotal,
                "Recepción Inbound registrada correctamente.");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<RecepcionInventarioResult> RecibirRmaRestockAsync(
        Guid rmaId,
        Guid productoId,
        int cantidad,
        Guid ubicacionAlmacenId,
        Guid operacionId,
        string? notas = null,
        CancellationToken cancellationToken = default)
    {
        if (rmaId == Guid.Empty)
            return Fallo("RmaId es obligatorio.");
        if (productoId == Guid.Empty)
            return Fallo("ProductoId es obligatorio.");
        if (cantidad <= 0)
            return Fallo("La cantidad a reingresar debe ser mayor que cero.");
        if (ubicacionAlmacenId == Guid.Empty)
            return Fallo("La ubicación WMS es obligatoria para ReStock.");
        if (operacionId == Guid.Empty)
            return Fallo("OperacionId es obligatorio.");

        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            var producto = await GetProductoForUpdateByIdAsync(productoId, cancellationToken);
            if (producto is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo($"El producto {productoId} no existe en catálogo.");
            }

            var ubicacion = await GetUbicacionForUpdateAsync(ubicacionAlmacenId, cancellationToken);
            if (ubicacion is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo($"La ubicación WMS {ubicacionAlmacenId} no existe.");
            }

            // Idempotencia fuerte por RMA: aunque el cliente regenere OperacionId,
            // un mismo RMA nunca puede incrementar stock dos veces.
            var movimientoExistente = await _dbContext.MovimientosInventario
                .AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.TipoMovimiento == TipoMovimientoInventario.RmaRestock &&
                    m.RmaId == rmaId,
                    cancellationToken);

            if (movimientoExistente is not null)
            {
                if (movimientoExistente.ProductoId != productoId ||
                    movimientoExistente.Cantidad != cantidad ||
                    movimientoExistente.UbicacionDestinoId != ubicacionAlmacenId)
                {
                    await RollbackIfNeededAsync(transaction, cancellationToken);
                    return Fallo("El RMA ya fue reingresado con datos diferentes.");
                }

                var stockExistente = await _dbContext.StockFisico
                    .AsNoTracking()
                    .SingleOrDefaultAsync(s =>
                        s.ProductoId == productoId &&
                        s.UbicacionAlmacenId == ubicacionAlmacenId,
                        cancellationToken);

                await CommitIfNeededAsync(transaction, cancellationToken);
                return new RecepcionInventarioResult(
                    true,
                    true,
                    productoId,
                    stockExistente?.Id ?? Guid.Empty,
                    stockExistente?.CantidadTotal ?? 0,
                    "El RMA ya había sido reingresado; no se duplicó stock.");
            }

            var ocupacionActual = await _dbContext.StockFisico
                .Where(s => s.UbicacionAlmacenId == ubicacionAlmacenId)
                .SumAsync(s => (int?)s.CantidadTotal, cancellationToken) ?? 0;

            if (ocupacionActual + cantidad > ubicacion.CapacidadMaxima)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo(
                    $"El ReStock excede la capacidad de {ubicacion.Codigo}. " +
                    $"Ocupación actual: {ocupacionActual}; capacidad: {ubicacion.CapacidadMaxima}; reingreso: {cantidad}.");
            }

            var stock = await _dbContext.StockFisico
                .AsTracking()
                .SingleOrDefaultAsync(s =>
                    s.ProductoId == productoId &&
                    s.UbicacionAlmacenId == ubicacionAlmacenId,
                    cancellationToken);

            var ahora = DateTime.UtcNow;
            if (stock is null)
            {
                stock = new StockFisico
                {
                    Id = Guid.NewGuid(),
                    ProductoId = productoId,
                    CantidadTotal = 0,
                    CantidadReservada = 0,
                    CantidadBloqueada = 0,
                    PuntoReorden = producto.StockMinimo,
                    UbicacionAlmacenId = ubicacion.Id,
                    UbicacionCodigo = ubicacion.Codigo,
                    UltimaActualizacion = ahora
                };
                _dbContext.StockFisico.Add(stock);
            }

            stock.CantidadTotal += cantidad;
            stock.UbicacionCodigo = ubicacion.Codigo;
            stock.UltimaActualizacion = ahora;
            ubicacion.EstaOcupada = true;

            _dbContext.MovimientosInventario.Add(new MovimientoInventario
            {
                Id = Guid.NewGuid(),
                ProductoId = productoId,
                TipoMovimiento = TipoMovimientoInventario.RmaRestock,
                Cantidad = cantidad,
                UbicacionDestinoId = ubicacion.Id,
                RmaId = rmaId,
                Referencia = $"RMA-{rmaId:N}-OP-{operacionId:N}",
                Observaciones = string.IsNullOrWhiteSpace(notas)
                    ? $"ReStock autorizado por RMA {rmaId:N}."
                    : notas.Trim(),
                FechaMovimiento = ahora
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfNeededAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "RMA ReStock aplicado. RMA={RmaId}, ProductoId={ProductoId}, Cantidad={Cantidad}, Ubicacion={Ubicacion}",
                rmaId, productoId, cantidad, ubicacion.Codigo);

            return new RecepcionInventarioResult(
                true,
                false,
                productoId,
                stock.Id,
                stock.CantidadTotal,
                "ReStock de RMA registrado correctamente.");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task<Producto?> GetProductoForUpdateBySkuAsync(
        string sku,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.Productos
                .AsTracking()
                .SingleOrDefaultAsync(p => p.Sku == sku, cancellationToken);
        }

        return await _dbContext.Productos
            .FromSqlInterpolated($"SELECT * FROM catalog.productos WHERE \"Sku\" = {sku} FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<Producto?> GetProductoForUpdateByIdAsync(
        Guid productoId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.Productos
                .AsTracking()
                .SingleOrDefaultAsync(p => p.Id == productoId, cancellationToken);
        }

        return await _dbContext.Productos
            .FromSqlInterpolated($"SELECT * FROM catalog.productos WHERE \"Id\" = {productoId} FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<UbicacionAlmacen?> GetUbicacionForUpdateAsync(
        Guid ubicacionId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.UbicacionesAlmacen
                .AsTracking()
                .SingleOrDefaultAsync(u => u.Id == ubicacionId, cancellationToken);
        }

        return await _dbContext.UbicacionesAlmacen
            .FromSqlInterpolated($"SELECT * FROM catalog.ubicaciones_almacen WHERE \"Id\" = {ubicacionId} FOR UPDATE")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync(
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
            return null;

        return await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    private static async Task CommitIfNeededAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RollbackIfNeededAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(cancellationToken);
    }

    private static string ConstruirReferencia(Guid operacionId, Guid itemOrdenCompraId) =>
        $"INB-{operacionId:N}-ITEM-{itemOrdenCompraId:N}";

    private static RecepcionInventarioResult Fallo(string mensaje) =>
        new(false, false, Guid.Empty, Guid.Empty, 0, mensaje);
}
