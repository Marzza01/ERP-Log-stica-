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


    public async Task<TransferenciaInventarioResult> TransferirStockAsync(
        Guid stockOrigenId,
        Guid ubicacionDestinoId,
        int cantidad,
        Guid operacionId,
        string? notas = null,
        CancellationToken cancellationToken = default)
    {
        if (stockOrigenId == Guid.Empty)
            return FalloTransferencia("StockOrigenId es obligatorio.");
        if (ubicacionDestinoId == Guid.Empty)
            return FalloTransferencia("UbicacionDestinoId es obligatorio.");
        if (cantidad <= 0)
            return FalloTransferencia("La cantidad a transferir debe ser mayor que cero.");
        if (operacionId == Guid.Empty)
            return FalloTransferencia("OperacionId es obligatorio.");

        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            // Leer primero la ubicación de origen sin bloquear para poder adquirir las ubicaciones
            // en un orden global estable. Después se bloquea y revalida el saldo.
            var stockSnapshot = await _dbContext.StockFisico
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == stockOrigenId, cancellationToken);

            if (stockSnapshot is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia($"El saldo de stock {stockOrigenId} no existe.");
            }

            if (!stockSnapshot.UbicacionAlmacenId.HasValue)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia("El stock de origen no tiene una ubicación WMS asignada.");
            }

            var ubicacionOrigenId = stockSnapshot.UbicacionAlmacenId.Value;
            if (ubicacionOrigenId == ubicacionDestinoId)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia("La ubicación de destino debe ser diferente a la ubicación de origen.");
            }

            // Bloquear ambas ubicaciones en orden estable evita carreras por capacidad
            // y evita deadlocks en transferencias opuestas A→B / B→A.
            var ubicacionIds = new[] { ubicacionOrigenId, ubicacionDestinoId }
                .OrderBy(id => id)
                .ToArray();

            UbicacionAlmacen? primera = null;
            UbicacionAlmacen? segunda = null;
            foreach (var ubicacionId in ubicacionIds)
            {
                var bloqueada = await GetUbicacionForUpdateAsync(ubicacionId, cancellationToken);
                if (primera is null)
                    primera = bloqueada;
                else
                    segunda = bloqueada;
            }

            var ubicacionOrigen = primera?.Id == ubicacionOrigenId ? primera : segunda;
            var ubicacionDestino = primera?.Id == ubicacionDestinoId ? primera : segunda;

            if (ubicacionOrigen is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia($"La ubicación de origen {ubicacionOrigenId} no existe.");
            }

            if (ubicacionDestino is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia($"La ubicación de destino {ubicacionDestinoId} no existe.");
            }

            var stockOrigen = await GetStockForUpdateAsync(stockOrigenId, cancellationToken);
            if (stockOrigen is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia($"El saldo de stock {stockOrigenId} ya no existe.");
            }

            if (stockOrigen.UbicacionAlmacenId != ubicacionOrigenId)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia(
                    "El stock de origen cambió de ubicación mientras se preparaba la transferencia. Actualice WMS e intente de nuevo.");
            }

            var referencia = $"TRF-{operacionId:N}";

            // La salida es la marca de idempotencia de toda la transferencia.
            var movimientoExistente = await _dbContext.MovimientosInventario
                .AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.TipoMovimiento == TipoMovimientoInventario.TransferenciaSalida &&
                    m.Referencia == referencia,
                    cancellationToken);

            if (movimientoExistente is not null)
            {
                if (movimientoExistente.ProductoId != stockOrigen.ProductoId ||
                    movimientoExistente.Cantidad != cantidad ||
                    movimientoExistente.UbicacionOrigenId != ubicacionOrigenId ||
                    movimientoExistente.UbicacionDestinoId != ubicacionDestinoId)
                {
                    await RollbackIfNeededAsync(transaction, cancellationToken);
                    return FalloTransferencia("OperacionId ya fue utilizado con datos diferentes.");
                }

                var stockDestinoExistente = await _dbContext.StockFisico
                    .AsNoTracking()
                    .SingleOrDefaultAsync(s =>
                        s.ProductoId == stockOrigen.ProductoId &&
                        s.UbicacionAlmacenId == ubicacionDestinoId,
                        cancellationToken);

                await CommitIfNeededAsync(transaction, cancellationToken);

                return new TransferenciaInventarioResult(
                    true,
                    true,
                    stockOrigen.ProductoId,
                    stockOrigen.Id,
                    stockDestinoExistente?.Id ?? Guid.Empty,
                    cantidad,
                    stockOrigen.CantidadTotal,
                    stockDestinoExistente?.CantidadTotal ?? 0,
                    "La transferencia ya había sido procesada; no se duplicó el movimiento.");
            }

            if (stockOrigen.CantidadDisponible < cantidad)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia(
                    $"Stock disponible insuficiente para transferir. " +
                    $"Disponible: {stockOrigen.CantidadDisponible}; solicitado: {cantidad}. " +
                    "Las unidades reservadas o bloqueadas no pueden moverse.");
            }

            var ocupacionDestino = await _dbContext.StockFisico
                .Where(s => s.UbicacionAlmacenId == ubicacionDestinoId)
                .SumAsync(s => (int?)s.CantidadTotal, cancellationToken) ?? 0;

            if (ocupacionDestino + cantidad > ubicacionDestino.CapacidadMaxima)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloTransferencia(
                    $"La transferencia excede la capacidad de {ubicacionDestino.Codigo}. " +
                    $"Ocupación actual: {ocupacionDestino}; capacidad: {ubicacionDestino.CapacidadMaxima}; transferencia: {cantidad}.");
            }

            var stockDestino = await _dbContext.StockFisico
                .AsTracking()
                .SingleOrDefaultAsync(s =>
                    s.ProductoId == stockOrigen.ProductoId &&
                    s.UbicacionAlmacenId == ubicacionDestinoId,
                    cancellationToken);

            var ahora = DateTime.UtcNow;
            if (stockDestino is null)
            {
                stockDestino = new StockFisico
                {
                    Id = Guid.NewGuid(),
                    ProductoId = stockOrigen.ProductoId,
                    CantidadTotal = 0,
                    CantidadReservada = 0,
                    CantidadBloqueada = 0,
                    PuntoReorden = stockOrigen.PuntoReorden,
                    UbicacionAlmacenId = ubicacionDestino.Id,
                    UbicacionCodigo = ubicacionDestino.Codigo,
                    UltimaActualizacion = ahora
                };
                _dbContext.StockFisico.Add(stockDestino);
            }

            stockOrigen.CantidadTotal -= cantidad;
            stockOrigen.UbicacionCodigo = ubicacionOrigen.Codigo;
            stockOrigen.UltimaActualizacion = ahora;

            stockDestino.CantidadTotal += cantidad;
            stockDestino.UbicacionCodigo = ubicacionDestino.Codigo;
            stockDestino.UltimaActualizacion = ahora;

            var totalRestanteOrigen = stockOrigen.CantidadTotal +
                (await _dbContext.StockFisico
                    .Where(s => s.UbicacionAlmacenId == ubicacionOrigenId && s.Id != stockOrigen.Id)
                    .SumAsync(s => (int?)s.CantidadTotal, cancellationToken) ?? 0);

            ubicacionOrigen.EstaOcupada = totalRestanteOrigen > 0;
            ubicacionDestino.EstaOcupada = true;

            var observaciones = string.IsNullOrWhiteSpace(notas)
                ? $"Transferencia WMS de {ubicacionOrigen.Codigo} a {ubicacionDestino.Codigo}."
                : notas.Trim();

            _dbContext.MovimientosInventario.AddRange(
                new MovimientoInventario
                {
                    Id = Guid.NewGuid(),
                    ProductoId = stockOrigen.ProductoId,
                    TipoMovimiento = TipoMovimientoInventario.TransferenciaSalida,
                    Cantidad = cantidad,
                    UbicacionOrigenId = ubicacionOrigenId,
                    UbicacionDestinoId = ubicacionDestinoId,
                    Referencia = referencia,
                    Observaciones = observaciones,
                    FechaMovimiento = ahora
                },
                new MovimientoInventario
                {
                    Id = Guid.NewGuid(),
                    ProductoId = stockOrigen.ProductoId,
                    TipoMovimiento = TipoMovimientoInventario.TransferenciaEntrada,
                    Cantidad = cantidad,
                    UbicacionOrigenId = ubicacionOrigenId,
                    UbicacionDestinoId = ubicacionDestinoId,
                    Referencia = referencia,
                    Observaciones = observaciones,
                    FechaMovimiento = ahora
                });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfNeededAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "Transferencia WMS aplicada. ProductoId={ProductoId}, Cantidad={Cantidad}, Origen={Origen}, Destino={Destino}, Operacion={OperacionId}",
                stockOrigen.ProductoId, cantidad, ubicacionOrigen.Codigo, ubicacionDestino.Codigo, operacionId);

            return new TransferenciaInventarioResult(
                true,
                false,
                stockOrigen.ProductoId,
                stockOrigen.Id,
                stockDestino.Id,
                cantidad,
                stockOrigen.CantidadTotal,
                stockDestino.CantidadTotal,
                "Transferencia WMS registrada correctamente.");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public Task<BloqueoInventarioResult> BloquearStockAsync(
        Guid stockFisicoId,
        int cantidad,
        Guid operacionId,
        string motivo,
        CancellationToken cancellationToken = default) =>
        CambiarBloqueoAsync(
            stockFisicoId,
            cantidad,
            operacionId,
            motivo,
            bloquear: true,
            cancellationToken);

    public Task<BloqueoInventarioResult> DesbloquearStockAsync(
        Guid stockFisicoId,
        int cantidad,
        Guid operacionId,
        string motivo,
        CancellationToken cancellationToken = default) =>
        CambiarBloqueoAsync(
            stockFisicoId,
            cantidad,
            operacionId,
            motivo,
            bloquear: false,
            cancellationToken);

    private async Task<BloqueoInventarioResult> CambiarBloqueoAsync(
        Guid stockFisicoId,
        int cantidad,
        Guid operacionId,
        string motivo,
        bool bloquear,
        CancellationToken cancellationToken)
    {
        if (stockFisicoId == Guid.Empty)
            return FalloBloqueo("StockFisicoId es obligatorio.");
        if (cantidad <= 0)
            return FalloBloqueo("La cantidad debe ser mayor que cero.");
        if (operacionId == Guid.Empty)
            return FalloBloqueo("OperacionId es obligatorio.");
        if (string.IsNullOrWhiteSpace(motivo))
            return FalloBloqueo("El motivo es obligatorio.");

        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            var stock = await GetStockForUpdateAsync(stockFisicoId, cancellationToken);
            if (stock is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return FalloBloqueo($"El saldo de stock {stockFisicoId} no existe.");
            }

            var tipo = bloquear
                ? TipoMovimientoInventario.BloqueoDano
                : TipoMovimientoInventario.Desbloqueo;
            var prefijo = bloquear ? "BLQ" : "DBL";
            var referencia = $"{prefijo}-{operacionId:N}";

            var movimientoExistente = await _dbContext.MovimientosInventario
                .AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.TipoMovimiento == tipo &&
                    m.Referencia == referencia,
                    cancellationToken);

            if (movimientoExistente is not null)
            {
                if (movimientoExistente.ProductoId != stock.ProductoId ||
                    movimientoExistente.Cantidad != cantidad ||
                    movimientoExistente.UbicacionOrigenId != stock.UbicacionAlmacenId)
                {
                    await RollbackIfNeededAsync(transaction, cancellationToken);
                    return FalloBloqueo("OperacionId ya fue utilizado con datos diferentes.");
                }

                await CommitIfNeededAsync(transaction, cancellationToken);
                return new BloqueoInventarioResult(
                    true,
                    true,
                    stock.ProductoId,
                    stock.Id,
                    cantidad,
                    stock.CantidadBloqueada,
                    stock.CantidadDisponible,
                    bloquear
                        ? "El bloqueo ya había sido procesado; no se duplicaron unidades bloqueadas."
                        : "El desbloqueo ya había sido procesado; no se duplicó la liberación.");
            }

            if (bloquear)
            {
                if (stock.CantidadDisponible < cantidad)
                {
                    await RollbackIfNeededAsync(transaction, cancellationToken);
                    return FalloBloqueo(
                        $"Stock disponible insuficiente para bloquear. " +
                        $"Disponible: {stock.CantidadDisponible}; solicitado: {cantidad}. " +
                        "Las unidades reservadas o ya bloqueadas no pueden bloquearse nuevamente.");
                }

                stock.CantidadBloqueada += cantidad;
            }
            else
            {
                if (stock.CantidadBloqueada < cantidad)
                {
                    await RollbackIfNeededAsync(transaction, cancellationToken);
                    return FalloBloqueo(
                        $"Unidades bloqueadas insuficientes para desbloquear. " +
                        $"Bloqueadas: {stock.CantidadBloqueada}; solicitado: {cantidad}.");
                }

                stock.CantidadBloqueada -= cantidad;
            }

            var ahora = DateTime.UtcNow;
            stock.UltimaActualizacion = ahora;

            _dbContext.MovimientosInventario.Add(new MovimientoInventario
            {
                Id = Guid.NewGuid(),
                ProductoId = stock.ProductoId,
                TipoMovimiento = tipo,
                Cantidad = cantidad,
                UbicacionOrigenId = stock.UbicacionAlmacenId,
                Referencia = referencia,
                Observaciones = motivo.Trim(),
                FechaMovimiento = ahora
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await CommitIfNeededAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "{Accion} de stock aplicada. ProductoId={ProductoId}, StockId={StockId}, Cantidad={Cantidad}, BloqueadoDespues={Bloqueado}, Operacion={OperacionId}",
                bloquear ? "Bloqueo" : "Desbloqueo",
                stock.ProductoId,
                stock.Id,
                cantidad,
                stock.CantidadBloqueada,
                operacionId);

            return new BloqueoInventarioResult(
                true,
                false,
                stock.ProductoId,
                stock.Id,
                cantidad,
                stock.CantidadBloqueada,
                stock.CantidadDisponible,
                bloquear
                    ? "Stock bloqueado correctamente."
                    : "Stock desbloqueado correctamente.");
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


    private async Task<StockFisico?> GetStockForUpdateAsync(
        Guid stockId,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.StockFisico
                .AsTracking()
                .SingleOrDefaultAsync(s => s.Id == stockId, cancellationToken);
        }

        return await _dbContext.StockFisico
            .FromSqlInterpolated($"SELECT * FROM catalog.stock_fisico WHERE \"Id\" = {stockId} FOR UPDATE")
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

    private static TransferenciaInventarioResult FalloTransferencia(string mensaje) =>
        new(false, false, Guid.Empty, Guid.Empty, Guid.Empty, 0, 0, 0, mensaje);

    private static BloqueoInventarioResult FalloBloqueo(string mensaje) =>
        new(false, false, Guid.Empty, Guid.Empty, 0, 0, 0, mensaje);

    private static RecepcionInventarioResult Fallo(string mensaje) =>
        new(false, false, Guid.Empty, Guid.Empty, 0, mensaje);
}
