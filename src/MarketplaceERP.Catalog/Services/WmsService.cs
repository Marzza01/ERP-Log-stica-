/**
 * ERP desarrollado por Marcelo Zepeda
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
    private readonly ILogger<WmsService> _logger;

    public WmsService(CatalogDbContext dbContext, ILogger<WmsService> logger)
    {
        _dbContext = dbContext;
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
        int ocupadas = ubicaciones.Count(u => u.EstaOcupada || u.StocksAlmacenados.Any());
        int disponibles = total - ocupadas;
        decimal pct = total > 0 ? Math.Round(((decimal)ocupadas / total) * 100m, 2) : 0m;

        var pasillosAgrupados = ubicaciones
            .GroupBy(u => u.Pasillo)
            .Select(g => new MapaPasilloDto(
                Pasillo: g.Key,
                TotalRacks: g.Select(x => x.Rack).Distinct().Count(),
                TotalPosiciones: g.Count(),
                PosicionesOcupadas: g.Count(x => x.EstaOcupada || x.StocksAlmacenados.Any()),
                Ubicaciones: g.ToList()
            ))
            .ToList();

        return new ResumenBodegaDto(total, ocupadas, disponibles, pct, pasillosAgrupados);
    }

    public async Task<IEnumerable<UbicacionAlmacen>> ListarUbicacionesAsync(
        string? pasillo = null, bool? soloDisponibles = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.UbicacionesAlmacen.AsQueryable();

        if (!string.IsNullOrWhiteSpace(pasillo))
        {
            query = query.Where(u => u.Pasillo.Contains(pasillo));
        }

        if (soloDisponibles.HasValue)
        {
            query = soloDisponibles.Value
                ? query.Where(u => !u.EstaOcupada)
                : query.Where(u => u.EstaOcupada);
        }

        return await query
            .OrderBy(u => u.Codigo)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ReubicarStockAsync(Guid stockFisicoId, Guid nuevaUbicacionId, CancellationToken cancellationToken = default)
    {
        var stock = await _dbContext.StockFisico.FindAsync(new object[] { stockFisicoId }, cancellationToken);
        var nuevaUbicacion = await _dbContext.UbicacionesAlmacen.FindAsync(new object[] { nuevaUbicacionId }, cancellationToken);

        if (stock is null || nuevaUbicacion is null)
        {
            return false;
        }

        // Si tenía ubicación previa, marcarla como potencialmente libre si no tiene otros items
        if (stock.UbicacionAlmacenId.HasValue)
        {
            var anteriorUbicacion = await _dbContext.UbicacionesAlmacen
                .Include(u => u.StocksAlmacenados)
                .FirstOrDefaultAsync(u => u.Id == stock.UbicacionAlmacenId.Value, cancellationToken);

            if (anteriorUbicacion is not null && anteriorUbicacion.StocksAlmacenados.Count <= 1)
            {
                anteriorUbicacion.EstaOcupada = false;
            }
        }

        stock.UbicacionAlmacenId = nuevaUbicacion.Id;
        stock.UbicacionCodigo = nuevaUbicacion.Codigo;
        stock.UltimaActualizacion = DateTime.UtcNow;

        nuevaUbicacion.EstaOcupada = true;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Stock {StockId} reubicado a {NuevaUbicacion}", stockFisicoId, nuevaUbicacion.Codigo);

        return true;
    }
}
