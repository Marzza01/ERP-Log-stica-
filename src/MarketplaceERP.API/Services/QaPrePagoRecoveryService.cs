using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// SOLO QA LOCAL. Compensa un carrito cuyo IntentoPago nunca supero Preparado.
/// Cancela Finance y libera N reservas Catalog EN UNA SOLA transaccion PostgreSQL.
/// No invoca pasarelas, no registra DI ni publica endpoints.
/// Nunca usar para estados Enviado/Incierto/Aprobado/Completado.
/// </summary>
public sealed class QaPrePagoRecoveryService
{
    private readonly FinanceDbContext _finance;
    private readonly ILoggerFactory _logs;

    public QaPrePagoRecoveryService(FinanceDbContext finance, ILoggerFactory logs)
    {
        _finance = finance;
        _logs = logs;
    }

    public async Task<string> CancelarSinEnvioAsync(
        Guid ordenId, bool autorizacionExplicitaQa,
        Guid operadorQaId, MotivoCancelacionPreEnvioQa motivo,
        int? falloTrasLiberarNumeroQa = null,
        CancellationToken ct = default)
    {
        if (!autorizacionExplicitaQa || ordenId == Guid.Empty ||
            operadorQaId == Guid.Empty || !Enum.IsDefined(motivo))
            throw new InvalidOperationException("RECUPERACION_QA_NO_AUTORIZADA");

        var conexion = _finance.Database.GetDbConnection();
        var cs = new NpgsqlConnectionStringBuilder(conexion.ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(cs.Host, StringComparer.OrdinalIgnoreCase) ||
            cs.Port != 5432 || cs.Username != "postgres" ||
            string.IsNullOrWhiteSpace(cs.Database) ||
            !cs.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("RECUPERACION_PROHIBIDA_FUERA_DE_QA_LOCAL");

        await using var tx = await _finance.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Usa exactamente el mismo lock por orden que la reserva, el
            // mapeo y la emision simulada. Nunca liberar a mitad de un envio.
            var lockKey = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", ct);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM finance.ordenes WHERE ""Id"" = {ordenId} FOR UPDATE", ct);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM finance.intentos_pago WHERE ""OrdenId"" = {ordenId} FOR UPDATE", ct);
            _finance.ChangeTracker.Clear();

            var orden = await _finance.Ordenes.AsTracking()
                .SingleOrDefaultAsync(x => x.Id == ordenId, ct)
                ?? throw new InvalidOperationException("ORDEN_QA_INEXISTENTE");
            var intentos = await _finance.IntentosPago.AsTracking()
                .Where(x => x.OrdenId == ordenId).ToListAsync(ct);
            if (intentos.Count != 1 ||
                orden.Estado != EstadoOrden.Pendiente ||
                string.IsNullOrWhiteSpace(orden.IdempotencyKey) ||
                string.IsNullOrWhiteSpace(orden.CheckoutPayloadHash) ||
                intentos[0].Estado != EstadoIntentoPago.Preparado ||
                intentos[0].ReservaId.HasValue ||
                !string.IsNullOrWhiteSpace(intentos[0].TransaccionId) ||
                intentos[0].ReferenciaOperacion != $"PAY-{ordenId:N}" ||
                await _finance.SplitsPago.AnyAsync(x => x.OrdenId == ordenId, ct) ||
                await _finance.ReembolsosPago.AnyAsync(x => x.OrdenId == ordenId, ct) ||
                await _finance.ConciliacionesPago.AnyAsync(x => x.OrdenId == ordenId, ct) ||
                await _finance.PartidasContables.AnyAsync(
                    x => x.ReferenciaOrigenId == ordenId && x.TipoOperacion == "Venta", ct) ||
                await _finance.CancelacionesPreEnvioQa.AnyAsync(x => x.OrdenId == ordenId, ct))
                throw new InvalidOperationException("NO_LIBERAR_INTENTO_NO_VERIFICADO");

            var intento = intentos[0];
            var items = await _finance.ItemsOrden.AsNoTracking()
                .Where(x => x.OrdenId == ordenId).ToListAsync(ct);
            if (items.Count is < 2 or > 20 ||
                items.Select(x => x.ProductoId).Distinct().Count() != items.Count ||
                items.Any(x => x.ProductoId == Guid.Empty ||
                    x.Cantidad <= 0 || x.PrecioUnitario <= 0) ||
                orden.VendedorId == Guid.Empty ||
                orden.MontoTotal != intento.Monto ||
                orden.Moneda != "USD" || intento.Moneda != "USD" ||
                items.Sum(x => x.PrecioUnitario * x.Cantidad) != intento.Monto)
                throw new InvalidOperationException("ITEMS_QA_INCONSISTENTES");

            var links = await _finance.IntentoReservasPago.AsNoTracking()
                .Where(x => x.IntentoPagoId == intento.Id).ToListAsync(ct);

            // Catalog y Finance viven en la misma BD PostgreSQL efimera,
            // asi que comparten el mismo commit/rollback fisico.
            var opciones = new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(conexion).Options;
            await using var catalog = new CatalogDbContext(opciones);
            await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);

            if (await catalog.PickLists.AnyAsync(x => x.OrdenId == ordenId, ct) ||
                await catalog.PaquetesDespacho.AnyAsync(x => x.OrdenId == ordenId, ct))
                throw new InvalidOperationException("WMS_INICIADO_NO_LIBERAR");

            var reservas = await catalog.ReservasStock.AsNoTracking()
                .Where(x => x.OrdenId == ordenId)
                .OrderBy(x => x.ProductoId).ToListAsync(ct);

            if (reservas.Count != 0 && reservas.Count != items.Count)
                throw new InvalidOperationException("RESERVAS_INCOMPLETAS_NO_LIBERAR");
            if (reservas.Count == 0 && links.Count != 0)
                throw new InvalidOperationException("MAPA_HUERFANO_NO_LIBERAR");
            if (links.Count != 0 && links.Count != reservas.Count)
                throw new InvalidOperationException("MAPA_PARCIAL_NO_LIBERAR");
            if (reservas.Any(r =>
                r.Estado != EstadoReserva.Pendiente ||
                r.CantidadConsumida != 0 || r.CantidadLiberada != 0 ||
                !items.Any(x => x.ProductoId == r.ProductoId &&
                    x.Cantidad == r.CantidadOriginal)) ||
                links.Any(l => l.Procedencia != "MultiSkuQa" ||
                    !reservas.Any(r => r.Id == l.ReservaId &&
                        r.ProductoId == l.ProductoId &&
                        r.CantidadOriginal == l.Cantidad)) ||
                links.Select(x => x.ReservaId).Distinct().Count() != links.Count)
                throw new InvalidOperationException("INVENTARIO_NO_APTO_PARA_COMPENSAR");

            if (reservas.Count > 0)
                await new QaLiberacionEnTransaccionService(catalog, _logs)
                    .LiberarAsync(ordenId, reservas.Select(x => x.Id).ToArray(),
                        autorizadoQa: true,
                        falloTrasLiberarNumeroQa: falloTrasLiberarNumeroQa);
            orden.Estado = EstadoOrden.Cancelada;
            orden.FechaCancelacion = DateTime.UtcNow;
            // Este estado representa una decisión local QA, NO un rechazo bancario.
            intento.Estado = EstadoIntentoPago.CanceladoAntesEnvio;
            intento.ActualizadoUtc = DateTime.UtcNow;
            // El registro de auditoría y la liberación WMS comparten UN COMMIT.
            _finance.CancelacionesPreEnvioQa.Add(new CancelacionPreEnvioQa
            {
                Id = Guid.NewGuid(),
                OrdenId = ordenId,
                IntentoPagoId = intento.Id,
                OperadorQaId = operadorQaId,
                Motivo = motivo,
                ReservasLiberadas = reservas.Count,
                RegistradoUtc = DateTime.UtcNow
            });
            await _finance.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return reservas.Count == 0
                ? "CANCELADO_SIN_RESERVAS"
                : "CANCELADO_RESERVAS_LIBERADAS";
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            _finance.ChangeTracker.Clear();
            throw;
        }
    }
}
