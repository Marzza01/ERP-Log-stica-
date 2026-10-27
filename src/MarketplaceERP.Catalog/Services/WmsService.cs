/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

public class WmsService : IWmsService
{
    private readonly CatalogDbContext _dbContext;
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<WmsService> _logger;

    public WmsService(
        CatalogDbContext dbContext,
        IInventoryService inventoryService,
        ILogger<WmsService> logger)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _logger = logger;
    }

    public async Task<ResumenBodegaDto> ObtenerMapaBodegaAsync(CancellationToken cancellationToken = default)
    {
        var ubicaciones = await _dbContext.UbicacionesAlmacen
            .Include(u => u.StocksAlmacenados)
            .OrderBy(u => u.Pasillo)
            .ThenBy(u => u.Rack)
            .ThenBy(u => u.Nivel)
            .ThenBy(u => u.Posicion)
            .ToListAsync(cancellationToken);

        int total = ubicaciones.Count;
        int ocupadas = ubicaciones.Count(u => u.EstaOcupada || u.StocksAlmacenados.Any(s => s.CantidadTotal > 0));
        int disponibles = total - ocupadas;
        decimal pct = total > 0 ? Math.Round(((decimal)ocupadas / total) * 100m, 2) : 0m;

        var pasillosAgrupados = ubicaciones
            .GroupBy(u => u.Pasillo)
            .Select(g => new MapaPasilloDto(
                Pasillo: g.Key,
                TotalRacks: g.Select(x => x.Rack).Distinct().Count(),
                TotalPosiciones: g.Count(),
                PosicionesOcupadas: g.Count(x => x.EstaOcupada || x.StocksAlmacenados.Any(s => s.CantidadTotal > 0)),
                Ubicaciones: g.ToList()
            ))
            .ToList();

        return new ResumenBodegaDto(total, ocupadas, disponibles, pct, pasillosAgrupados);
    }

    public async Task<IEnumerable<UbicacionAlmacen>> ListarUbicacionesAsync(
        string? pasillo = null,
        bool? soloDisponibles = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.UbicacionesAlmacen.AsQueryable();

        if (!string.IsNullOrWhiteSpace(pasillo))
        {
            query = query.Where(u => u.Pasillo.Contains(pasillo));
        }

        if (soloDisponibles.HasValue)
        {
            query = soloDisponibles.Value
                ? query.Where(u => !u.EstaOcupada && !u.StocksAlmacenados.Any(s => s.CantidadTotal > 0))
                : query.Where(u => u.EstaOcupada || u.StocksAlmacenados.Any(s => s.CantidadTotal > 0));
        }

        return await query
            .OrderBy(u => u.Codigo)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<StockWmsDto>> ListarStocksAsync(
        Guid? ubicacionId = null,
        string? sku = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.StockFisico
            .AsNoTracking()
            .Include(s => s.Producto)
            .Include(s => s.UbicacionAlmacen)
            .Where(s => s.CantidadTotal > 0)
            .AsQueryable();

        if (ubicacionId.HasValue)
            query = query.Where(s => s.UbicacionAlmacenId == ubicacionId.Value);

        if (!string.IsNullOrWhiteSpace(sku))
        {
            var filtroSku = sku.Trim();
            query = query.Where(s => s.Producto.Sku.Contains(filtroSku));
        }

        var stocks = await query
            .OrderBy(s => s.Producto.Sku)
            .ThenBy(s => s.UbicacionCodigo)
            .ToListAsync(cancellationToken);

        return stocks.Select(s => new StockWmsDto(
            s.Id,
            s.ProductoId,
            s.Producto.Sku,
            s.Producto.Nombre,
            s.CantidadTotal,
            s.CantidadReservada,
            s.CantidadBloqueada,
            s.CantidadDisponible,
            s.UbicacionAlmacenId,
            s.UbicacionCodigo,
            s.UbicacionAlmacen?.Zona ?? string.Empty,
            s.UltimaActualizacion));
    }

    public Task<TransferenciaInventarioResult> TransferirStockAsync(
        Guid stockFisicoId,
        Guid nuevaUbicacionId,
        int cantidad,
        Guid operacionId,
        string? observaciones = null,
        CancellationToken cancellationToken = default) =>
        _inventoryService.TransferirStockAsync(
            stockFisicoId,
            nuevaUbicacionId,
            cantidad,
            operacionId,
            observaciones,
            cancellationToken);

    public async Task<bool> ReubicarStockAsync(
        Guid stockFisicoId,
        Guid nuevaUbicacionId,
        CancellationToken cancellationToken = default)
    {
        var stock = await _dbContext.StockFisico
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == stockFisicoId, cancellationToken);

        if (stock is null || stock.CantidadTotal <= 0)
            return false;

        // El endpoint legado solo puede mover el saldo completo si todo es disponible.
        // Unidades reservadas o bloqueadas deben permanecer en su ubicación actual.
        if (stock.CantidadDisponible != stock.CantidadTotal)
            return false;

        var resultado = await _inventoryService.TransferirStockAsync(
            stockFisicoId,
            nuevaUbicacionId,
            stock.CantidadTotal,
            Guid.NewGuid(),
            "Reubicación completa solicitada mediante endpoint legado.",
            cancellationToken);

        if (resultado.Exitoso)
        {
            _logger.LogInformation(
                "Stock {StockId} reubicado completamente a {NuevaUbicacionId}",
                stockFisicoId,
                nuevaUbicacionId);
        }

        return resultado.Exitoso;
    }
}
