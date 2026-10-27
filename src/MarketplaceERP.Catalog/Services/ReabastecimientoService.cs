/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Detecta necesidades operativas de reabastecimiento usando el stock agregado real.
/// No crea órdenes de compra: esa responsabilidad permanece en Finance/Compras.
/// </summary>
public sealed class ReabastecimientoService : IReabastecimientoService
{
    private readonly CatalogDbContext _dbContext;

    public ReabastecimientoService(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<NecesidadReabastecimiento>> ObtenerNecesidadesAsync(
        CancellationToken cancellationToken = default)
    {
        var productos = await _dbContext.Productos
            .AsNoTracking()
            .Include(p => p.Stocks)
            .Where(p => p.EstaPublicado && p.EstadoProducto == "Activo" && p.StockMinimo > 0)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        return productos
            .Select(p =>
            {
                var fisico = p.Stocks.Sum(s => s.CantidadTotal);
                var reservado = p.Stocks.Sum(s => s.CantidadReservada);
                var bloqueado = p.Stocks.Sum(s => s.CantidadBloqueada);
                var disponible = fisico - reservado - bloqueado;

                return new NecesidadReabastecimiento(
                    p.Id,
                    p.Sku,
                    p.Nombre,
                    p.CategoriaNombre,
                    p.ProveedorNombre,
                    p.StockMinimo,
                    fisico,
                    reservado,
                    bloqueado,
                    disponible,
                    Math.Max(0, p.StockMinimo - disponible),
                    Math.Max(0, (p.StockMinimo + 1) - disponible),
                    disponible <= 0
                        ? "AGOTADO"
                        : disponible < p.StockMinimo
                            ? "BAJO_MINIMO"
                            : "EN_PUNTO_REORDEN");
            })
            .Where(n => n.CantidadDisponible <= n.StockMinimo)
            .OrderBy(n => n.CantidadDisponible)
            .ThenBy(n => n.Sku)
            .ToList();
    }
}
