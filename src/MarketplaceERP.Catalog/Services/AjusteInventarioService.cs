/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

internal sealed class AjusteInventarioService : IAjusteInventarioService
{
    private readonly CatalogDbContext _db;
    private readonly ILogger<AjusteInventarioService> _logger;

    public AjusteInventarioService(CatalogDbContext db, ILogger<AjusteInventarioService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AjusteInventarioDto>> ListarAsync(
        EstadoAjusteInventario? estado = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AjustesInventario
            .AsNoTracking()
            .Include(a => a.Producto)
            .Include(a => a.StockFisico)
            .AsQueryable();

        if (estado.HasValue)
            query = query.Where(a => a.Estado == estado.Value);

        var ajustes = await query
            .OrderByDescending(a => a.FechaCreacion)
            .ToListAsync(cancellationToken);

        return ajustes.Select(Mapear).ToList();
    }

    public async Task<AjusteInventarioResult> CrearAsync(
        Guid stockFisicoId,
        TipoAjusteInventario tipo,
        int cantidad,
        string motivo,
        string? detalle,
        string? referencia,
        string? usuario,
        CancellationToken cancellationToken = default)
    {
        if (stockFisicoId == Guid.Empty)
            return Fallo("StockFisicoId es obligatorio.");
        if (cantidad <= 0)
            return Fallo("La cantidad del ajuste debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(motivo))
            return Fallo("El motivo del ajuste es obligatorio.");

        var stock = await _db.StockFisico
            .Include(s => s.Producto)
            .SingleOrDefaultAsync(s => s.Id == stockFisicoId, cancellationToken);

        if (stock is null)
            return Fallo("El saldo de stock indicado no existe.");

        if (tipo == TipoAjusteInventario.Negativo && cantidad > stock.CantidadDisponible)
            return Fallo($"El ajuste negativo excede las {stock.CantidadDisponible} unidades disponibles.");

        var ajuste = new AjusteInventario
        {
            Id = Guid.NewGuid(),
            StockFisicoId = stock.Id,
            ProductoId = stock.ProductoId,
            Tipo = tipo,
            Cantidad = cantidad,
            Motivo = motivo.Trim(),
            Detalle = string.IsNullOrWhiteSpace(detalle) ? null : detalle.Trim(),
            Referencia = string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim(),
            Estado = EstadoAjusteInventario.PendienteAprobacion,
            CreadoPor = NormalizarUsuario(usuario),
            FechaCreacion = DateTime.UtcNow
        };

        _db.AjustesInventario.Add(ajuste);
        await _db.SaveChangesAsync(cancellationToken);

        ajuste.StockFisico = stock;
        ajuste.Producto = stock.Producto;
        return Exito(ajuste, "Ajuste creado y pendiente de aprobación.");
    }

    public Task<AjusteInventarioResult> AprobarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default) =>
        CambiarDecisionAsync(ajusteId, EstadoAjusteInventario.Aprobado, usuario, "Ajuste aprobado.", cancellationToken);

    public Task<AjusteInventarioResult> RechazarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default) =>
        CambiarDecisionAsync(ajusteId, EstadoAjusteInventario.Rechazado, usuario, "Ajuste rechazado.", cancellationToken);

    public async Task<AjusteInventarioResult> CancelarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default)
    {
        var ajuste = await CargarAsync(ajusteId, cancellationToken);
        if (ajuste is null) return Fallo("El ajuste no existe.");
        if (ajuste.Estado is EstadoAjusteInventario.Aplicado or EstadoAjusteInventario.Rechazado or EstadoAjusteInventario.Cancelado)
            return Fallo($"No se puede cancelar un ajuste en estado {ajuste.Estado}.");

        ajuste.Estado = EstadoAjusteInventario.Cancelado;
        ajuste.FechaDecision = DateTime.UtcNow;
        ajuste.AprobadoPor = NormalizarUsuario(usuario);
        await _db.SaveChangesAsync(cancellationToken);
        return Exito(ajuste, "Ajuste cancelado.");
    }

    public async Task<AjusteInventarioResult> AplicarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default)
    {
        if (ajusteId == Guid.Empty) return Fallo("AjusteId es obligatorio.");

        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);
        try
        {
            var ajuste = await CargarForUpdateAsync(ajusteId, cancellationToken);
            if (ajuste is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo("El ajuste no existe.");
            }

            if (ajuste.Estado == EstadoAjusteInventario.Aplicado)
            {
                await CommitIfNeededAsync(transaction, cancellationToken);
                return Exito(ajuste, "El ajuste ya había sido aplicado; no se modificó stock nuevamente.");
            }

            if (ajuste.Estado != EstadoAjusteInventario.Aprobado)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo("Solo se puede aplicar un ajuste previamente aprobado.");
            }

            var stock = ajuste.StockFisico;
            if (ajuste.Tipo == TipoAjusteInventario.Negativo && ajuste.Cantidad > stock.CantidadDisponible)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo($"El stock disponible cambió y ahora solo hay {stock.CantidadDisponible} unidades disponibles.");
            }

            var ahora = DateTime.UtcNow;
            if (ajuste.Tipo == TipoAjusteInventario.Positivo)
                stock.CantidadTotal += ajuste.Cantidad;
            else
                stock.CantidadTotal -= ajuste.Cantidad;

            stock.UltimaActualizacion = ahora;

            _db.MovimientosInventario.Add(new MovimientoInventario
            {
                Id = Guid.NewGuid(),
                ProductoId = ajuste.ProductoId,
                TipoMovimiento = ajuste.Tipo == TipoAjusteInventario.Positivo
                    ? TipoMovimientoInventario.AjustePositivo
                    : TipoMovimientoInventario.AjusteNegativo,
                Cantidad = ajuste.Cantidad,
                UbicacionOrigenId = ajuste.Tipo == TipoAjusteInventario.Negativo ? stock.UbicacionAlmacenId : null,
                UbicacionDestinoId = ajuste.Tipo == TipoAjusteInventario.Positivo ? stock.UbicacionAlmacenId : null,
                Referencia = $"AJUSTE-{ajuste.Id:N}",
                Observaciones = $"{ajuste.Motivo}. {ajuste.Detalle}".Trim(),
                FechaMovimiento = ahora
            });

            ajuste.Estado = EstadoAjusteInventario.Aplicado;
            ajuste.AplicadoPor = NormalizarUsuario(usuario);
            ajuste.FechaAplicacion = ahora;

            if (stock.UbicacionAlmacenId.HasValue)
            {
                var ubicacion = await _db.UbicacionesAlmacen
                    .SingleOrDefaultAsync(u => u.Id == stock.UbicacionAlmacenId.Value, cancellationToken);
                if (ubicacion is not null)
                {
                    var otros = await _db.StockFisico
                        .Where(s => s.UbicacionAlmacenId == ubicacion.Id && s.Id != stock.Id)
                        .SumAsync(s => (int?)s.CantidadTotal, cancellationToken) ?? 0;
                    ubicacion.EstaOcupada = otros + stock.CantidadTotal > 0;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await CommitIfNeededAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "Ajuste de inventario aplicado. Ajuste={AjusteId}, Stock={StockId}, Tipo={Tipo}, Cantidad={Cantidad}",
                ajuste.Id, stock.Id, ajuste.Tipo, ajuste.Cantidad);

            return Exito(ajuste, "Ajuste aplicado correctamente al inventario.");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task<AjusteInventarioResult> CambiarDecisionAsync(
        Guid ajusteId,
        EstadoAjusteInventario estado,
        string? usuario,
        string mensaje,
        CancellationToken cancellationToken)
    {
        var ajuste = await CargarAsync(ajusteId, cancellationToken);
        if (ajuste is null) return Fallo("El ajuste no existe.");
        if (ajuste.Estado != EstadoAjusteInventario.PendienteAprobacion)
            return Fallo("Solo los ajustes pendientes pueden aprobarse o rechazarse.");

        ajuste.Estado = estado;
        ajuste.AprobadoPor = NormalizarUsuario(usuario);
        ajuste.FechaDecision = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Exito(ajuste, mensaje);
    }

    private Task<AjusteInventario?> CargarAsync(Guid id, CancellationToken cancellationToken) =>
        _db.AjustesInventario
            .Include(a => a.Producto)
            .Include(a => a.StockFisico)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    private async Task<AjusteInventario?> CargarForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            var ids = await _db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM catalog.ajustes_inventario WHERE \"Id\" = {id} FOR UPDATE")
                .ToListAsync(cancellationToken);
            if (ids.Count == 0) return null;
        }
        return await CargarAsync(id, cancellationToken);
    }

    private static string? NormalizarUsuario(string? usuario) => string.IsNullOrWhiteSpace(usuario) ? null : usuario.Trim();

    private AjusteInventarioResult Exito(AjusteInventario ajuste, string mensaje) =>
        new(true, Mapear(ajuste), mensaje);

    private static AjusteInventarioResult Fallo(string mensaje) => new(false, null, mensaje);

    private static AjusteInventarioDto Mapear(AjusteInventario a) => new(
        a.Id,
        a.StockFisicoId,
        a.ProductoId,
        a.Producto?.Sku ?? string.Empty,
        a.Producto?.Nombre ?? string.Empty,
        a.StockFisico?.UbicacionCodigo ?? string.Empty,
        a.Tipo,
        a.Cantidad,
        a.Motivo,
        a.Detalle,
        a.Referencia,
        a.Estado,
        a.StockFisico?.CantidadTotal ?? 0,
        a.StockFisico?.CantidadDisponible ?? 0,
        a.CreadoPor,
        a.AprobadoPor,
        a.AplicadoPor,
        a.FechaCreacion,
        a.FechaDecision,
        a.FechaAplicacion);

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync(CancellationToken cancellationToken) =>
        _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

    private static Task CommitIfNeededAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

    private static Task RollbackIfNeededAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken) =>
        transaction is null ? Task.CompletedTask : transaction.RollbackAsync(cancellationToken);
}
