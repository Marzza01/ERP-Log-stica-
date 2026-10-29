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

internal sealed class ConteoCiclicoService : IConteoCiclicoService
{
    private readonly CatalogDbContext _db;
    private readonly IAjusteInventarioService _ajustes;
    private readonly ILogger<ConteoCiclicoService> _logger;

    public ConteoCiclicoService(
        CatalogDbContext db,
        IAjusteInventarioService ajustes,
        ILogger<ConteoCiclicoService> logger)
    {
        _db = db;
        _ajustes = ajustes;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConteoCiclicoDto>> ListarAsync(
        EstadoConteoCiclico? estado = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ConteosCiclicos
            .AsNoTracking()
            .Include(c => c.Producto)
            .Include(c => c.StockFisico)
            .Include(c => c.AjusteInventario)
            .AsQueryable();

        if (estado.HasValue)
            query = query.Where(c => c.Estado == estado.Value);

        var conteos = await query
            .OrderByDescending(c => c.FechaCreacion)
            .ToListAsync(cancellationToken);

        return conteos.Select(Mapear).ToList();
    }

    public async Task<ConteoCiclicoResult> PlanificarAsync(
        Guid stockFisicoId,
        string? observaciones,
        string? usuario,
        CancellationToken cancellationToken = default)
    {
        if (stockFisicoId == Guid.Empty)
            return Fallo("StockFisicoId es obligatorio.");

        var stock = await _db.StockFisico
            .Include(s => s.Producto)
            .SingleOrDefaultAsync(s => s.Id == stockFisicoId, cancellationToken);

        if (stock is null)
            return Fallo("El saldo de stock indicado no existe.");

        var existeConteoActivo = await _db.ConteosCiclicos.AnyAsync(c =>
            c.StockFisicoId == stockFisicoId &&
            (c.Estado == EstadoConteoCiclico.Planificado ||
             c.Estado == EstadoConteoCiclico.EnConteo ||
             c.Estado == EstadoConteoCiclico.RequiereReconteo), cancellationToken);

        if (existeConteoActivo)
            return Fallo("Ya existe un conteo cíclico activo para este saldo.");

        var existeAjustePendiente = await _db.AjustesInventario.AnyAsync(a =>
            a.StockFisicoId == stockFisicoId &&
            (a.Estado == EstadoAjusteInventario.PendienteAprobacion ||
             a.Estado == EstadoAjusteInventario.Aprobado), cancellationToken);

        if (existeAjustePendiente)
            return Fallo("Existe un ajuste pendiente para este saldo. Debe resolverse antes de iniciar otro conteo.");

        var conteo = new ConteoCiclico
        {
            Id = Guid.NewGuid(),
            StockFisicoId = stock.Id,
            ProductoId = stock.ProductoId,
            Estado = EstadoConteoCiclico.Planificado,
            Observaciones = Normalizar(observaciones),
            CreadoPor = Normalizar(usuario),
            FechaCreacion = DateTime.UtcNow
        };

        _db.ConteosCiclicos.Add(conteo);
        await _db.SaveChangesAsync(cancellationToken);

        conteo.StockFisico = stock;
        conteo.Producto = stock.Producto;
        return Exito(conteo, "Conteo cíclico planificado.");
    }

    public async Task<ConteoCiclicoResult> IniciarAsync(
        Guid conteoId,
        string? usuario,
        CancellationToken cancellationToken = default)
    {
        var conteo = await CargarAsync(conteoId, cancellationToken);
        if (conteo is null) return Fallo("El conteo cíclico no existe.");

        if (conteo.Estado != EstadoConteoCiclico.Planificado)
            return Fallo("Solo un conteo planificado puede iniciarse.");

        var ahora = DateTime.UtcNow;
        conteo.CantidadSistema = conteo.StockFisico.CantidadTotal;
        conteo.Estado = EstadoConteoCiclico.EnConteo;
        conteo.IniciadoPor = Normalizar(usuario);
        conteo.FechaInicio = ahora;

        await _db.SaveChangesAsync(cancellationToken);
        return Exito(conteo, $"Conteo iniciado con snapshot de {conteo.CantidadSistema} unidades físicas.");
    }

    public async Task<ConteoCiclicoResult> RegistrarPrimerConteoAsync(
        Guid conteoId,
        int cantidadContada,
        string? usuario,
        CancellationToken cancellationToken = default)
    {
        if (cantidadContada < 0)
            return Fallo("La cantidad contada no puede ser negativa.");

        var conteo = await CargarAsync(conteoId, cancellationToken);
        if (conteo is null) return Fallo("El conteo cíclico no existe.");

        if (conteo.Estado != EstadoConteoCiclico.EnConteo)
            return Fallo("El primer conteo solo puede registrarse cuando el conteo está iniciado.");

        var vigencia = ValidarSnapshot(conteo);
        if (vigencia is not null) return vigencia;

        var ahora = DateTime.UtcNow;
        conteo.PrimerConteo = cantidadContada;
        conteo.ContadoPor = Normalizar(usuario);
        conteo.FechaPrimerConteo = ahora;

        if (cantidadContada == conteo.CantidadSistema)
        {
            conteo.Estado = EstadoConteoCiclico.Completado;
            conteo.FechaFinalizacion = ahora;
            await _db.SaveChangesAsync(cancellationToken);
            return Exito(conteo, "Conteo completado sin diferencias. No se generó ajuste.");
        }

        conteo.Estado = EstadoConteoCiclico.RequiereReconteo;
        await _db.SaveChangesAsync(cancellationToken);
        return Exito(conteo, "Se detectó una diferencia. Es obligatorio realizar un reconteo antes de generar un ajuste.");
    }

    public async Task<ConteoCiclicoResult> RegistrarReconteoAsync(
        Guid conteoId,
        int cantidadContada,
        string? usuario,
        CancellationToken cancellationToken = default)
    {
        if (cantidadContada < 0)
            return Fallo("La cantidad recontada no puede ser negativa.");

        var conteo = await CargarAsync(conteoId, cancellationToken);
        if (conteo is null) return Fallo("El conteo cíclico no existe.");

        if (conteo.Estado == EstadoConteoCiclico.CompletadoConAjuste && conteo.AjusteInventarioId.HasValue)
            return Exito(conteo, "El reconteo ya había generado un ajuste; no se creó otro.");

        if (conteo.Estado != EstadoConteoCiclico.RequiereReconteo)
            return Fallo("El reconteo solo puede registrarse después de una diferencia en el primer conteo.");

        var vigencia = ValidarSnapshot(conteo);
        if (vigencia is not null) return vigencia;

        if (!conteo.CantidadSistema.HasValue || !conteo.PrimerConteo.HasValue)
            return Fallo("El conteo no tiene la información necesaria para realizar el reconteo.");

        var ahora = DateTime.UtcNow;

        if (cantidadContada == conteo.CantidadSistema.Value)
        {
            conteo.SegundoConteo = cantidadContada;
            conteo.RecontadoPor = Normalizar(usuario);
            conteo.FechaReconteo = ahora;
            conteo.Estado = EstadoConteoCiclico.Completado;
            conteo.FechaFinalizacion = ahora;
            await _db.SaveChangesAsync(cancellationToken);
            return Exito(conteo, "El reconteo coincide con el sistema. Conteo completado sin ajuste.");
        }

        var diferencia = cantidadContada - conteo.CantidadSistema.Value;
        var tipo = diferencia > 0 ? TipoAjusteInventario.Positivo : TipoAjusteInventario.Negativo;
        var cantidadAjuste = Math.Abs(diferencia);

        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);
        try
        {
            var ajuste = await _ajustes.CrearAsync(
                conteo.StockFisicoId,
                tipo,
                cantidadAjuste,
                "Diferencia confirmada en conteo cíclico",
                $"Sistema: {conteo.CantidadSistema}; primer conteo: {conteo.PrimerConteo}; reconteo: {cantidadContada}.",
                $"CONTEO-{conteo.Id:N}",
                usuario,
                cancellationToken);

            if (!ajuste.Exitoso || ajuste.Ajuste is null)
            {
                await RollbackIfNeededAsync(transaction, cancellationToken);
                return Fallo($"El reconteo confirmó una diferencia, pero no se pudo generar el ajuste: {ajuste.Mensaje}");
            }

            conteo.SegundoConteo = cantidadContada;
            conteo.RecontadoPor = Normalizar(usuario);
            conteo.FechaReconteo = ahora;
            conteo.AjusteInventarioId = ajuste.Ajuste.Id;
            conteo.Estado = EstadoConteoCiclico.CompletadoConAjuste;
            conteo.FechaFinalizacion = ahora;

            await _db.SaveChangesAsync(cancellationToken);
            await CommitIfNeededAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "Conteo cíclico {ConteoId} confirmó diferencia {Diferencia} y generó ajuste {AjusteId}",
                conteo.Id, diferencia, ajuste.Ajuste.Id);

            return Exito(conteo, "Reconteo completado. Se generó un ajuste pendiente de aprobación; el stock aún no cambió.");
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<ConteoCiclicoResult> CancelarAsync(
        Guid conteoId,
        string? usuario,
        CancellationToken cancellationToken = default)
    {
        var conteo = await CargarAsync(conteoId, cancellationToken);
        if (conteo is null) return Fallo("El conteo cíclico no existe.");

        if (conteo.Estado is EstadoConteoCiclico.Completado or EstadoConteoCiclico.CompletadoConAjuste or EstadoConteoCiclico.Cancelado)
            return Fallo($"No se puede cancelar un conteo en estado {conteo.Estado}.");

        conteo.Estado = EstadoConteoCiclico.Cancelado;
        conteo.FechaFinalizacion = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(usuario))
        {
            var nota = $"Cancelado por {usuario.Trim()}.";
            conteo.Observaciones = string.IsNullOrWhiteSpace(conteo.Observaciones)
                ? nota
                : $"{conteo.Observaciones} {nota}";
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Exito(conteo, "Conteo cíclico cancelado.");
    }

    private ConteoCiclicoResult? ValidarSnapshot(ConteoCiclico conteo)
    {
        if (!conteo.CantidadSistema.HasValue)
            return Fallo("El conteo no posee snapshot de inventario. Debe iniciarse nuevamente.");

        if (conteo.StockFisico.CantidadTotal != conteo.CantidadSistema.Value)
        {
            return Fallo(
                $"El stock cambió durante el conteo (snapshot {conteo.CantidadSistema}, actual {conteo.StockFisico.CantidadTotal}). " +
                "Cancela este conteo y planifica uno nuevo para evitar un ajuste contra datos obsoletos.");
        }

        return null;
    }

    private async Task<ConteoCiclico?> CargarAsync(Guid conteoId, CancellationToken cancellationToken)
    {
        return await _db.ConteosCiclicos
            .Include(c => c.Producto)
            .Include(c => c.StockFisico)
            .Include(c => c.AjusteInventario)
            .SingleOrDefaultAsync(c => c.Id == conteoId, cancellationToken);
    }

    private static ConteoCiclicoDto Mapear(ConteoCiclico c) => new(
        c.Id,
        c.StockFisicoId,
        c.ProductoId,
        c.Producto?.Sku ?? string.Empty,
        c.Producto?.Nombre ?? string.Empty,
        c.StockFisico?.UbicacionCodigo ?? string.Empty,
        c.Estado,
        c.CantidadSistema,
        c.PrimerConteo,
        c.SegundoConteo,
        c.StockFisico?.CantidadTotal ?? 0,
        c.AjusteInventarioId,
        c.AjusteInventario?.Estado,
        c.Observaciones,
        c.CreadoPor,
        c.IniciadoPor,
        c.ContadoPor,
        c.RecontadoPor,
        c.FechaCreacion,
        c.FechaInicio,
        c.FechaPrimerConteo,
        c.FechaReconteo,
        c.FechaFinalizacion);

    private static string? Normalizar(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ConteoCiclicoResult Exito(ConteoCiclico c, string mensaje) => new(true, Mapear(c), mensaje);
    private static ConteoCiclicoResult Fallo(string mensaje) => new(false, null, mensaje);

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync(CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return null;
        return await _db.Database.BeginTransactionAsync(cancellationToken);
    }

    private static async Task CommitIfNeededAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RollbackIfNeededAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
    }
}
