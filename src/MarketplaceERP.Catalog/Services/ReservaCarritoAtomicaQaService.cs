using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// PROTOTIPO DE QA: reserva multi-SKU únicamente ANTES de un hipotético pago.
/// No hay pasarela ni endpoint; NO ejecutar en staging/producción.
/// Solo permite BD efímera ci_checkout_http_* de postgres en localhost:5432.
/// No implementa aún el checkout multi-SKU completo.
/// </summary>
public sealed record ItemReservaCarritoQa(Guid ProductoId, int Cantidad);
public sealed record ReservaCarritoQa(Guid OrdenId, IReadOnlyList<Guid> ReservaIds);

public sealed class ReservaCarritoAtomicaQaService
{
    private readonly CatalogDbContext _db;
    private readonly ILoggerFactory _loggerFactory;

    public ReservaCarritoAtomicaQaService(CatalogDbContext db, ILoggerFactory loggerFactory)
    {
        _db = db;
        _loggerFactory = loggerFactory;
    }

    public async Task<ReservaCarritoQa> ReservarAsync(
        Guid ordenId, IReadOnlyList<ItemReservaCarritoQa> items,
        bool autorizacionExplicitaQa, CancellationToken cancellationToken = default,
        int? falloTrasReservaNumeroQa = null)
    {
        if (!autorizacionExplicitaQa)
            throw new InvalidOperationException("ENSAYO_NO_AUTORIZADO");
        if (ordenId == Guid.Empty || items is null || items.Count is < 2 or > 20 ||
            items.Any(x => x.ProductoId == Guid.Empty || x.Cantidad <= 0) ||
            items.Select(x => x.ProductoId).Distinct().Count() != items.Count)
            throw new InvalidOperationException("CARRITO_INVALIDO");

        var connection = _db.Database.GetDbConnection();
        var descriptor = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(descriptor.Host, StringComparer.OrdinalIgnoreCase) ||
            descriptor.Port != 5432 || descriptor.Username != "postgres" ||
            string.IsNullOrWhiteSpace(descriptor.Database) ||
            !descriptor.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL");

        // Orden determinista por SKU para limitar interbloqueos entre carritos.
        var ordenados = items.OrderBy(x => x.ProductoId).ToList();
        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Excluye simultáneamente otro carrito para la MISMA orden. Lo utilizan
            // también pruebas QA de cierre y Fulfillment para ese OrdenId.
            var key = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({key})", cancellationToken);

            var existentes = await _db.ReservasStock.AsNoTracking()
                .Where(r => r.OrdenId == ordenId)
                .ToListAsync(cancellationToken);
            if (existentes.Count > 0)
            {
                // La llave de orden no puede reutilizarse con otro carrito ni con
                // reservas ya liberadas, expiradas o confirmadas.
                if (existentes.Count != ordenados.Count ||
                    existentes.Select(r => r.ProductoId).Distinct().Count() != existentes.Count ||
                    existentes.Any(r => r.Estado != EstadoReserva.Pendiente ||
                        !ordenados.Any(i => i.ProductoId == r.ProductoId &&
                                           i.Cantidad == r.CantidadOriginal)))
                    throw new InvalidOperationException("CARRITO_YA_RESERVADO_CON_OTROS_ITEMS");

                await transaction.CommitAsync(cancellationToken);
                return new ReservaCarritoQa(ordenId,
                    existentes.OrderBy(x => x.ProductoId).Select(x => x.Id).ToList());
            }

            // Un único vendedor/moneda en la versión 1: el modelo actual de
            // Finance guarda un VendedorId por Orden y un SplitPago por Orden.
            var ids = ordenados.Select(i => i.ProductoId).ToList();
            var productos = await _db.Productos.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToListAsync(cancellationToken);
            if (productos.Count != ordenados.Count ||
                productos.Select(p => p.VendedorId).Distinct().Count() != 1 ||
                productos.Any(p => p.VendedorId == Guid.Empty ||
                    !p.EstaPublicado ||
                    !string.Equals(p.EstadoProducto, "Activo", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(p.Moneda, "USD", StringComparison.OrdinalIgnoreCase) ||
                    p.PrecioB2C <= 0))
                throw new InvalidOperationException("CATALOGO_MULTISKU_NO_APTO");

            var stock = new StockService(_db, _loggerFactory.CreateLogger<StockService>());
            var reservas = new List<Guid>(ordenados.Count);
            foreach (var item in ordenados)
            {
                var resultado = await stock.ReservarStockAsync(
                    item.ProductoId, item.Cantidad, ordenId);
                if (!resultado.Exitoso || resultado.ReservaId == Guid.Empty)
                    throw new InvalidOperationException(
                        "STOCK_INSUFICIENTE_MULTISKU_" + item.ProductoId.ToString("N"));
                reservas.Add(resultado.ReservaId);
                // Inyectar crash local DESPUÉS de un INSERT del segundo SKU
                // para verificar que la transacción revierte ambas reservas.
                // Nunca exponer esta opción en API o entorno no efímero.
                if (falloTrasReservaNumeroQa == reservas.Count)
                    throw new InvalidOperationException("QA_FALLO_TRAS_RESERVA_NUMERO_" + reservas.Count);
            }

            await transaction.CommitAsync(cancellationToken);
            return new ReservaCarritoQa(ordenId, reservas);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// Ensayo de confirmación TODOS-O-NINGUNO después de un resultado de pago
    /// SIMULADO. NO verifica cobros ni se expone fuera de la base efímera QA.
    /// </summary>
    public async Task<string> ConfirmarAsync(
        Guid ordenId, IReadOnlyList<Guid> reservaIds, bool autorizacionExplicitaQa,
        int? falloEnReservaNumeroQa = null, CancellationToken cancellationToken = default)
    {
        if (!autorizacionExplicitaQa)
            throw new InvalidOperationException("ENSAYO_NO_AUTORIZADO");
        if (ordenId == Guid.Empty || reservaIds is null || reservaIds.Count is < 2 or > 20 ||
            reservaIds.Any(x => x == Guid.Empty) ||
            reservaIds.Distinct().Count() != reservaIds.Count)
            throw new InvalidOperationException("RESERVAS_INVALIDAS");

        var descriptor = new NpgsqlConnectionStringBuilder(
            _db.Database.GetDbConnection().ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(descriptor.Host, StringComparer.OrdinalIgnoreCase) ||
            descriptor.Port != 5432 || descriptor.Username != "postgres" ||
            string.IsNullOrWhiteSpace(descriptor.Database) ||
            !descriptor.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL");

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var key = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            var reservas = await _db.ReservasStock.AsNoTracking()
                .Where(x => x.OrdenId == ordenId)
                .OrderBy(x => x.ProductoId)
                .ToListAsync(cancellationToken);
            if (reservas.Count != reservaIds.Count ||
                reservas.Any(x => !reservaIds.Contains(x.Id)) ||
                reservas.Select(x => x.ProductoId).Distinct().Count() != reservas.Count)
                throw new InvalidOperationException("RESERVAS_NO_COINCIDEN");

            if (reservas.All(x => x.Estado == EstadoReserva.Confirmada))
            {
                await transaction.CommitAsync(cancellationToken);
                return "YA_CONFIRMADAS";
            }
            if (reservas.Any(x => x.Estado != EstadoReserva.Pendiente))
                throw new InvalidOperationException("CONFIRMACION_PARCIAL_INCONSISTENTE");

            var stock = new StockService(_db, _loggerFactory.CreateLogger<StockService>());
            for (int i = 0; i < reservas.Count; i++)
            {
                if (falloEnReservaNumeroQa == i + 1)
                    throw new InvalidOperationException("FALLO_CONFIRMACION_INYECTADO_QA");
                await stock.ConfirmarReservaAsync(reservas[i].Id);
            }
            await transaction.CommitAsync(cancellationToken);
            return "CONFIRMADAS";
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// Compensación conjunta QA SOLO ante un rechazo inequívoco SIMULADO del pago.
    /// Nunca utilizar con pago incierto, aprobado o confirmado. No hay pasarela.
    /// </summary>
    public async Task<string> LiberarTrasRechazoAsync(
        Guid ordenId, IReadOnlyList<Guid> reservaIds, bool autorizacionExplicitaQa,
        bool rechazoExplicitoSimulado, int? falloEnReservaNumeroQa = null,
        CancellationToken cancellationToken = default)
    {
        if (!autorizacionExplicitaQa || !rechazoExplicitoSimulado)
            throw new InvalidOperationException("NO_LIBERAR_SIN_RECHAZO_EXPLICITO_QA");
        if (ordenId == Guid.Empty || reservaIds is null || reservaIds.Count is < 2 or > 20 ||
            reservaIds.Any(x => x == Guid.Empty) ||
            reservaIds.Distinct().Count() != reservaIds.Count)
            throw new InvalidOperationException("RESERVAS_INVALIDAS");

        var descriptor = new NpgsqlConnectionStringBuilder(
            _db.Database.GetDbConnection().ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(descriptor.Host, StringComparer.OrdinalIgnoreCase) ||
            descriptor.Port != 5432 || descriptor.Username != "postgres" ||
            string.IsNullOrWhiteSpace(descriptor.Database) ||
            !descriptor.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL");

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var key = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({key})", cancellationToken);
            var reservas = await _db.ReservasStock.AsNoTracking()
                .Where(x => x.OrdenId == ordenId)
                .OrderBy(x => x.ProductoId)
                .ToListAsync(cancellationToken);
            if (reservas.Count != reservaIds.Count ||
                reservas.Any(r => !reservaIds.Contains(r.Id)) ||
                reservas.Select(r => r.ProductoId).Distinct().Count() != reservas.Count)
                throw new InvalidOperationException("RESERVAS_NO_COINCIDEN");
            if (reservas.All(x => x.Estado == EstadoReserva.Liberada))
            {
                await transaction.CommitAsync(cancellationToken);
                return "YA_LIBERADAS";
            }
            // Prohibir liberación de reservas confirmadas y estados parciales.
            if (reservas.Any(x => x.Estado != EstadoReserva.Pendiente))
                throw new InvalidOperationException("NO_LIBERAR_ESTADO_INCIERTO");

            var stock = new StockService(_db, _loggerFactory.CreateLogger<StockService>());
            for (int i = 0; i < reservas.Count; i++)
            {
                if (falloEnReservaNumeroQa == i + 1)
                    throw new InvalidOperationException("FALLO_LIBERACION_INYECTADO_QA");
                await stock.LiberarStockAsync(reservas[i].Id);
            }
            await transaction.CommitAsync(cancellationToken);
            return "LIBERADAS";
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }
    }
}
