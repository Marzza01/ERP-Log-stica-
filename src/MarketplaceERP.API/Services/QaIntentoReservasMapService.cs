using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// EXPERIMENTO SOLO QA: registra durablemente N reservas en Finance ANTES
/// de un eventual pago. No envía cobros ni abre endpoints. Solo BDs efímeras
/// ci_checkout_http_* en localhost:5432; no se registra en DI.
/// </summary>
public sealed record QaVinculoReserva(Guid ReservaId, Guid ProductoId, int Cantidad);

public sealed class QaIntentoReservasMapService
{
    private readonly FinanceDbContext _finance;

    public QaIntentoReservasMapService(FinanceDbContext finance) => _finance = finance;

    public async Task<string> RegistrarAsync(
        Guid intentoId, IReadOnlyList<QaVinculoReserva> vinculos,
        bool autorizacionExplicitaQa, bool inyectarFalloTrasPrimerInsertQa = false,
        CancellationToken cancellationToken = default)
    {
        if (!autorizacionExplicitaQa)
            throw new InvalidOperationException("REGISTRO_QA_NO_AUTORIZADO");
        if (intentoId == Guid.Empty || vinculos is null || vinculos.Count is < 2 or > 20 ||
            vinculos.Any(x => x.ReservaId == Guid.Empty || x.ProductoId == Guid.Empty ||
                              x.Cantidad <= 0) ||
            vinculos.Select(x => x.ReservaId).Distinct().Count() != vinculos.Count ||
            vinculos.Select(x => x.ProductoId).Distinct().Count() != vinculos.Count)
            throw new InvalidOperationException("VINCULOS_QA_INVALIDOS");

        var conexion = _finance.Database.GetDbConnection();
        var str = new NpgsqlConnectionStringBuilder(conexion.ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(str.Host, StringComparer.OrdinalIgnoreCase) ||
            str.Port != 5432 || str.Username != "postgres" ||
            string.IsNullOrWhiteSpace(str.Database) ||
            !str.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL");

        await using var transaction = await _finance.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var intento = await _finance.IntentosPago.AsNoTracking()
                .SingleOrDefaultAsync(i => i.Id == intentoId, cancellationToken)
                ?? throw new InvalidOperationException("INTENTO_INEXISTENTE");
            var ordenId = intento.OrdenId;
            // Mismo lock por orden que la reserva atómica y la recuperación QA.
            var llave = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({llave})", cancellationToken);

            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM finance.ordenes WHERE ""Id"" = {ordenId} FOR UPDATE",
                cancellationToken);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM finance.intentos_pago WHERE ""Id"" = {intentoId} FOR UPDATE",
                cancellationToken);

            var existing = await _finance.IntentoReservasPago.AsNoTracking()
                .Where(x => x.IntentoPagoId == intentoId).ToListAsync(cancellationToken);
            if (existing.Count > 0)
            {
                if (existing.Count != vinculos.Count || existing.Any(x =>
                    x.Procedencia != "MultiSkuQa" ||
                    !vinculos.Any(v => v.ReservaId == x.ReservaId &&
                                      v.ProductoId == x.ProductoId &&
                                      v.Cantidad == x.Cantidad)))
                    throw new InvalidOperationException("HUELLA_DE_RESERVAS_DISTINTA");
                await transaction.CommitAsync(cancellationToken);
                return "YA_REGISTRADO";
            }

            // Nunca crear un mapa nuevo después de enviar la solicitud de pago.
            if (intento.Estado != EstadoIntentoPago.Preparado ||
                intento.ReservaId.HasValue || !string.IsNullOrWhiteSpace(intento.TransaccionId) ||
                intento.ReferenciaOperacion != $"PAY-{ordenId:N}" ||
                intento.Monto <= 0 || intento.Moneda != "USD")
                throw new InvalidOperationException("INTENTO_NO_APTO_PRECOBRO");

            var orden = await _finance.Ordenes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == ordenId, cancellationToken)
                ?? throw new InvalidOperationException("ORDEN_INEXISTENTE");
            if (orden.Estado != EstadoOrden.Pendiente ||
                string.IsNullOrWhiteSpace(orden.IdempotencyKey) ||
                string.IsNullOrWhiteSpace(orden.CheckoutPayloadHash) ||
                orden.MontoTotal != intento.Monto ||
                orden.Moneda != intento.Moneda || orden.VendedorId == Guid.Empty)
                throw new InvalidOperationException("ORDEN_NO_APTA_PRECOBRO");

            var items = await _finance.ItemsOrden.AsNoTracking()
                .Where(x => x.OrdenId == ordenId).ToListAsync(cancellationToken);
            if (items.Count != vinculos.Count ||
                items.Sum(x => x.PrecioUnitario * x.Cantidad) != orden.MontoTotal ||
                items.Any(i => i.Cantidad <= 0 || i.PrecioUnitario <= 0 ||
                    !vinculos.Any(v => v.ProductoId == i.ProductoId &&
                                      v.Cantidad == i.Cantidad)))
                throw new InvalidOperationException("ITEMS_FINANCE_NO_COINCIDEN");

            // Ambas tablas comparten una sola transacción PostgreSQL. No hay
            // FK entre Finance y Catalog: verificamos reservas reales y saldos
            // conservando el mismo lock por orden hasta el commit.
            var options = new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(conexion).Options;
            await using var catalog = new CatalogDbContext(options);
            await catalog.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            var reservas = await catalog.ReservasStock.AsNoTracking()
                .Where(r => r.OrdenId == ordenId).ToListAsync(cancellationToken);
            if (reservas.Count != vinculos.Count || reservas.Any(r =>
                r.Estado != EstadoReserva.Pendiente ||
                !vinculos.Any(v => v.ReservaId == r.Id &&
                                  v.ProductoId == r.ProductoId &&
                                  v.Cantidad == r.CantidadOriginal)))
                throw new InvalidOperationException("RESERVAS_CATALOG_NO_COINCIDEN");

            var rows = vinculos.OrderBy(v => v.ProductoId).Select(v => new IntentoReservaPago
            {
                Id = Guid.NewGuid(), IntentoPagoId = intentoId,
                ReservaId = v.ReservaId, ProductoId = v.ProductoId,
                Cantidad = v.Cantidad, Procedencia = "MultiSkuQa",
                CreadoUtc = DateTime.UtcNow
            }).ToList();
            if (inyectarFalloTrasPrimerInsertQa)
            {
                _finance.IntentoReservasPago.Add(rows[0]);
                await _finance.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("QA_FALLO_POST_PRIMER_INSERT");
            }

            _finance.IntentoReservasPago.AddRange(rows);
            await _finance.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return "REGISTRADO";
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _finance.ChangeTracker.Clear();
            throw;
        }
    }
}
