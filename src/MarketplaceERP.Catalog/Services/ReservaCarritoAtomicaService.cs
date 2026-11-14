using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

public sealed record ItemReservaCarrito(Guid ProductoId, int Cantidad);
public sealed record ReservaCarrito(Guid OrdenId, IReadOnlyList<Guid> ReservaIds);

/// <summary>
/// Operaciones multi-SKU sobre el mismo transaction scope que Finance.
/// El llamador debe asociar este CatalogDbContext a la transacción PostgreSQL
/// compartida antes de invocar cualquier método.
/// </summary>
public sealed class ReservaCarritoAtomicaService
{
    private readonly CatalogDbContext _db;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration? _configuration;

    public ReservaCarritoAtomicaService(
        CatalogDbContext db,
        ILoggerFactory loggerFactory,
        IConfiguration? configuration = null)
    {
        _db = db;
        _loggerFactory = loggerFactory;
        _configuration = configuration;
    }

    public async Task<ReservaCarrito> ReservarEnTransaccionAsync(
        Guid ordenId,
        IReadOnlyList<ItemReservaCarrito> items,
        CancellationToken cancellationToken = default)
    {
        ExigirTransaccion();
        ValidarItems(ordenId, items);
        await BloquearOrdenAsync(ordenId, cancellationToken);

        var ordenados = items.OrderBy(x => x.ProductoId).ToArray();
        var existentes = await _db.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == ordenId)
            .OrderBy(r => r.ProductoId)
            .ToListAsync(cancellationToken);

        if (existentes.Count > 0)
        {
            if (existentes.Count != ordenados.Length ||
                existentes.Any(r => r.Estado != EstadoReserva.Pendiente ||
                    !ordenados.Any(i => i.ProductoId == r.ProductoId &&
                                        i.Cantidad == r.CantidadOriginal)))
                throw new InvalidOperationException("CARRITO_YA_RESERVADO_CON_OTROS_ITEMS");

            return new ReservaCarrito(ordenId, existentes.Select(x => x.Id).ToArray());
        }

        var ids = ordenados.Select(x => x.ProductoId).ToArray();
        var productos = await _db.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.VendedorId, p.PrecioB2C, p.Moneda,
                p.EstaPublicado, p.EstadoProducto
            })
            .ToListAsync(cancellationToken);

        if (productos.Count != ordenados.Length ||
            productos.Select(p => p.VendedorId).Distinct().Count() != 1 ||
            productos.Any(p => p.VendedorId == Guid.Empty ||
                !p.EstaPublicado ||
                !string.Equals(p.EstadoProducto, "Activo", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(p.Moneda, "USD", StringComparison.OrdinalIgnoreCase) ||
                p.PrecioB2C <= 0))
            throw new InvalidOperationException("CATALOGO_MULTISKU_NO_APTO");

        var stock = new StockService(
            _db, _loggerFactory.CreateLogger<StockService>(), _configuration);
        var reservas = new List<Guid>(ordenados.Length);

        foreach (var item in ordenados)
        {
            var resultado = await stock.ReservarStockAsync(
                item.ProductoId, item.Cantidad, ordenId);
            if (!resultado.Exitoso || resultado.ReservaId == Guid.Empty)
                throw new InvalidOperationException(
                    "STOCK_INSUFICIENTE_MULTISKU:" + item.ProductoId.ToString("N"));
            reservas.Add(resultado.ReservaId);
        }

        return new ReservaCarrito(ordenId, reservas);
    }

    public async Task ConfirmarEnTransaccionAsync(
        Guid ordenId,
        IReadOnlyCollection<Guid> reservaIds,
        CancellationToken cancellationToken = default)
    {
        ExigirTransaccion();
        ValidarReservaIds(ordenId, reservaIds);
        await BloquearOrdenAsync(ordenId, cancellationToken);

        var reservas = await _db.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == ordenId)
            .OrderBy(r => r.ProductoId)
            .ToListAsync(cancellationToken);

        if (reservas.Count != reservaIds.Count ||
            reservas.Any(r => !reservaIds.Contains(r.Id)) ||
            reservas.Select(r => r.ProductoId).Distinct().Count() != reservas.Count)
            throw new InvalidOperationException("RESERVAS_NO_COINCIDEN");

        if (reservas.All(r => r.Estado == EstadoReserva.Confirmada))
            return;
        if (reservas.Any(r => r.Estado != EstadoReserva.Pendiente))
            throw new InvalidOperationException("CONFIRMACION_PARCIAL_INCONSISTENTE");

        var stock = new StockService(
            _db, _loggerFactory.CreateLogger<StockService>(), _configuration);
        foreach (var reserva in reservas)
            await stock.ConfirmarReservaAsync(reserva.Id);
    }

    public async Task LiberarTrasRechazoEnTransaccionAsync(
        Guid ordenId,
        IReadOnlyCollection<Guid> reservaIds,
        CancellationToken cancellationToken = default)
    {
        ExigirTransaccion();
        ValidarReservaIds(ordenId, reservaIds);
        await BloquearOrdenAsync(ordenId, cancellationToken);

        var reservas = await _db.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == ordenId)
            .OrderBy(r => r.ProductoId)
            .ToListAsync(cancellationToken);

        if (reservas.Count != reservaIds.Count ||
            reservas.Any(r => !reservaIds.Contains(r.Id)) ||
            reservas.Select(r => r.ProductoId).Distinct().Count() != reservas.Count)
            throw new InvalidOperationException("RESERVAS_NO_COINCIDEN");

        if (reservas.All(r => r.Estado == EstadoReserva.Liberada))
            return;
        // Nunca liberar reservas confirmadas, consumidas o con estado mixto.
        if (reservas.Any(r => r.Estado != EstadoReserva.Pendiente))
            throw new InvalidOperationException("NO_LIBERAR_ESTADO_INCIERTO");

        var stock = new StockService(
            _db, _loggerFactory.CreateLogger<StockService>(), _configuration);
        foreach (var reserva in reservas)
            await stock.LiberarStockAsync(reserva.Id);
    }

    private void ExigirTransaccion()
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("TRANSACCION_COMPARTIDA_OBLIGATORIA");
    }

    private async Task BloquearOrdenAsync(Guid ordenId, CancellationToken ct)
    {
        var key = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({key})", ct);
    }

    private static void ValidarItems(Guid ordenId, IReadOnlyList<ItemReservaCarrito> items)
    {
        if (ordenId == Guid.Empty || items is null || items.Count is < 2 or > 20 ||
            items.Any(x => x.ProductoId == Guid.Empty || x.Cantidad is < 1 or > 9999) ||
            items.Select(x => x.ProductoId).Distinct().Count() != items.Count)
            throw new InvalidOperationException("CARRITO_MULTISKU_INVALIDO");
    }

    private static void ValidarReservaIds(Guid ordenId, IReadOnlyCollection<Guid> reservaIds)
    {
        if (ordenId == Guid.Empty || reservaIds is null || reservaIds.Count is < 2 or > 20 ||
            reservaIds.Any(x => x == Guid.Empty) ||
            reservaIds.Distinct().Count() != reservaIds.Count)
            throw new InvalidOperationException("RESERVAS_MULTISKU_INVALIDAS");
    }
}
