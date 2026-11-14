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

        var productoIds = calculadas
            .Select(a => a.ProductoId)
            .Distinct()
            .ToArray();

        var revisiones = await _dbContext.AlertasInventarioRevision
            .AsNoTracking()
            .Where(r => productoIds.Contains(r.ProductoId))
            .ToListAsync(cancellationToken);

        // GET permanece de solo lectura. Un movimiento posterior invalida
        // lógicamente el reconocimiento sin borrar su rastro de auditoría.
        var movimientosPosteriores = new Dictionary<Guid, DateTime>();
        if (revisiones.Count > 0)
        {
            var reconocimientoMasAntiguo = revisiones.Min(r => r.ReconocidaEn);
            movimientosPosteriores = await _dbContext.MovimientosInventario
                .AsNoTracking()
                .Where(m => productoIds.Contains(m.ProductoId) &&
                            m.FechaMovimiento > reconocimientoMasAntiguo)
                .GroupBy(m => m.ProductoId)
                .Select(g => new
                {
                    ProductoId = g.Key,
                    FechaMovimiento = g.Max(m => m.FechaMovimiento)
                })
                .ToDictionaryAsync(
                    x => x.ProductoId,
                    x => x.FechaMovimiento,
                    cancellationToken);
        }

        var revisionesPorClave = revisiones.ToDictionary(r => (r.ProductoId, r.Tipo));

        return calculadas
            .Select(a =>
            {
                revisionesPorClave.TryGetValue((a.ProductoId, a.Tipo), out var revision);
                var reconocimientoVigente = revision is not null &&
                    (!movimientosPosteriores.TryGetValue(a.ProductoId, out var ultimoMovimiento) ||
                     ultimoMovimiento <= revision.ReconocidaEn);

                return a with
                {
                    Estado = reconocimientoVigente
                        ? EstadoAlertaInventario.Acknowledged
                        : EstadoAlertaInventario.Open,
                    ReconocidaEn = reconocimientoVigente ? revision?.ReconocidaEn : null,
                    ReconocidaPor = reconocimientoVigente ? revision?.ReconocidaPor : null
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
