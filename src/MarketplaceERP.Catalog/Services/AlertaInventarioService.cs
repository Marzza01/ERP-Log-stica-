/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Alertas derivadas del inventario agregado real.
/// Reconocer una alerta no modifica el stock ni resuelve su condición.
/// </summary>
public sealed class AlertaInventarioService : IAlertaInventarioService
{
    private readonly CatalogDbContext _dbContext;

    public AlertaInventarioService(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AlertaInventarioOperativa>> ObtenerActivasAsync(
        CancellationToken cancellationToken = default)
    {
        var productos = await _dbContext.Productos
            .AsNoTracking()
            .Include(p => p.Stocks)
            .Where(p => p.EstaPublicado && p.EstadoProducto == "Activo" && p.StockMinimo > 0)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        var calculadas = productos
            .Select(Calcular)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var revisiones = await _dbContext.AlertasInventarioRevision
            .ToListAsync(cancellationToken);

        // El reconocimiento solo es válido mientras persista exactamente la misma condición.
        // Si cambia de tipo o se resuelve, se elimina para que una futura reactivación nazca abierta.
        var clavesActivas = calculadas
            .Select(a => (a.ProductoId, a.Tipo))
            .ToHashSet();

        var obsoletas = revisiones
            .Where(r => !clavesActivas.Contains((r.ProductoId, r.Tipo)))
            .ToList();

        if (obsoletas.Count > 0)
        {
            _dbContext.AlertasInventarioRevision.RemoveRange(obsoletas);
            await _dbContext.SaveChangesAsync(cancellationToken);
            revisiones.RemoveAll(r => obsoletas.Contains(r));
        }

        var revisionesPorClave = revisiones.ToDictionary(r => (r.ProductoId, r.Tipo));

        return calculadas
            .Select(a =>
            {
                revisionesPorClave.TryGetValue((a.ProductoId, a.Tipo), out var revision);
                return a with
                {
                    Estado = revision is null ? EstadoAlertaInventario.Open : EstadoAlertaInventario.Acknowledged,
                    ReconocidaEn = revision?.ReconocidaEn,
                    ReconocidaPor = revision?.ReconocidaPor
                };
            })
            .OrderByDescending(a => a.Prioridad)
            .ThenBy(a => a.CantidadDisponible)
            .ThenBy(a => a.Sku)
            .ToList();
    }

    public async Task<bool> ReconocerAsync(
        Guid productoId,
        TipoAlertaInventario tipo,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        var producto = await _dbContext.Productos
            .AsNoTracking()
            .Include(p => p.Stocks)
            .SingleOrDefaultAsync(p => p.Id == productoId, cancellationToken);

        if (producto is null)
            return false;

        var activa = Calcular(producto);
        if (activa is null || activa.Tipo != tipo)
            return false;

        var revision = await _dbContext.AlertasInventarioRevision
            .SingleOrDefaultAsync(
                r => r.ProductoId == productoId && r.Tipo == tipo,
                cancellationToken);

        if (revision is null)
        {
            revision = new AlertaInventarioRevision
            {
                Id = Guid.NewGuid(),
                ProductoId = productoId,
                Tipo = tipo,
                ReconocidaEn = DateTime.UtcNow,
                ReconocidaPor = NormalizarUsuario(usuario)
            };
            _dbContext.AlertasInventarioRevision.Add(revision);
        }
        else
        {
            // Idempotente: volver a reconocer no crea duplicados.
            revision.ReconocidaEn = DateTime.UtcNow;
            revision.ReconocidaPor = NormalizarUsuario(usuario);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static AlertaInventarioOperativa? Calcular(Producto producto)
    {
        var fisico = producto.Stocks.Sum(s => s.CantidadTotal);
        var reservado = producto.Stocks.Sum(s => s.CantidadReservada);
        var bloqueado = producto.Stocks.Sum(s => s.CantidadBloqueada);
        var disponible = fisico - reservado - bloqueado;

        // El catálogo actual conserva PuntoReorden en StockFisico. Para un SKU multiubicación
        // se usa el máximo configurado y nunca por debajo del StockMinimo del producto.
        var puntoReordenLocal = producto.Stocks.Count == 0
            ? producto.StockMinimo
            : producto.Stocks.Max(s => s.PuntoReorden);
        var puntoReorden = Math.Max(producto.StockMinimo, puntoReordenLocal);

        TipoAlertaInventario? tipo = null;
        PrioridadAlertaInventario prioridad = PrioridadAlertaInventario.Medium;

        if (disponible <= 0)
        {
            tipo = TipoAlertaInventario.OutOfStock;
            prioridad = PrioridadAlertaInventario.Critical;
        }
        else if (disponible <= producto.StockMinimo)
        {
            tipo = TipoAlertaInventario.CriticalStock;
            prioridad = PrioridadAlertaInventario.High;
        }
        else if (disponible <= puntoReorden)
        {
            tipo = TipoAlertaInventario.LowStock;
            prioridad = PrioridadAlertaInventario.Medium;
        }

        if (tipo is null)
            return null;

        var ubicaciones = producto.Stocks
            .Where(s => s.CantidadTotal > 0)
            .Select(s => s.UbicacionCodigo)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToArray();

        var ubicacionesTexto = ubicaciones.Length switch
        {
            0 => "Sin stock físico",
            1 => ubicaciones[0],
            2 => string.Join(", ", ubicaciones),
            _ => $"{ubicaciones.Length} ubicaciones"
        };

        return new AlertaInventarioOperativa(
            producto.Id,
            $"ALT-{producto.Id:N}-{tipo.Value}",
            producto.Sku,
            producto.Nombre,
            producto.CategoriaNombre,
            tipo.Value,
            prioridad,
            EstadoAlertaInventario.Open,
            disponible,
            fisico,
            reservado,
            bloqueado,
            producto.StockMinimo,
            puntoReorden,
            Math.Max(0, puntoReorden - disponible),
            ubicacionesTexto,
            null,
            null);
    }

    private static string NormalizarUsuario(string usuario) =>
        string.IsNullOrWhiteSpace(usuario) ? "usuario" : usuario.Trim()[..Math.Min(usuario.Trim().Length, 150)];
}
