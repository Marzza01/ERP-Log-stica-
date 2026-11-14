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
/// Solo un SIMULADOR QA, NO representa IPaymentGateway ni permite pagos reales.
/// Resultado sintético con ID prefijado QA-SIM-.
/// </summary>
public sealed record QaRespuestaPagoSimulado(bool Exitoso, string? TransaccionSimuladaId);
public interface IQaPagoSimulador
{
    Task<QaRespuestaPagoSimulado> SimularAsync(
        Guid ordenId, decimal monto, string moneda, string claveIdempotencia);
}

/// <summary>
/// Prototipo aislado P0: prepara una sola llamada SIMULADA una vez que Finance
/// posee un mapa completo 1 IntentoPago → N ReservaStock.
/// No crea splits, no cierra órdenes y no confirma reservas; queda pendiente
/// una Saga de producción y conciliación verificable.
/// </summary>
public sealed class QaMultiSkuPagoSimuladoService
{
    private readonly FinanceDbContext _finance;
    private readonly IQaPagoSimulador _simulador;

    public QaMultiSkuPagoSimuladoService(
        FinanceDbContext finance, IQaPagoSimulador simulador)
    {
        _finance = finance;
        _simulador = simulador;
    }

    public async Task<string> SimularUnaVezAsync(
        Guid intentoId, bool autorizacionExplicitaQa,
        bool falloDespuesDeMarcarEnviadoQa = false,
        bool falloDespuesDeRespuestaQa = false,
        CancellationToken ct = default)
    {
        if (!autorizacionExplicitaQa)
            throw new InvalidOperationException("SIMULADOR_QA_NO_AUTORIZADO");
        if (intentoId == Guid.Empty)
            throw new InvalidOperationException("INTENTO_INVALIDO");

        var conexion = _finance.Database.GetDbConnection();
        var str = new NpgsqlConnectionStringBuilder(conexion.ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(str.Host, StringComparer.OrdinalIgnoreCase) ||
            str.Port != 5432 || str.Username != "postgres" ||
            string.IsNullOrWhiteSpace(str.Database) ||
            !str.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL");

        Guid ordenId;
        decimal monto;
        string moneda;
        string clave;
        await using (var tx = await _finance.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct))
        {
            try
            {
                var intento = await _finance.IntentosPago.AsTracking()
                    .SingleOrDefaultAsync(i => i.Id == intentoId, ct)
                    ?? throw new InvalidOperationException("INTENTO_INEXISTENTE");
                ordenId = intento.OrdenId;
                var llave = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
                await _finance.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({llave})", ct);
                await _finance.Database.ExecuteSqlInterpolatedAsync(
                    $@"SELECT ""Id"" FROM finance.intentos_pago WHERE ""Id"" = {intentoId} FOR UPDATE", ct);
                // Reread después de adquirir locks para no confiar en un snapshot
                // anterior; borrar tracking es necesario si otra petición cambió estado.
                _finance.ChangeTracker.Clear();
                intento = await _finance.IntentosPago.AsTracking()
                    .SingleAsync(i => i.Id == intentoId, ct);

                if (intento.Estado != EstadoIntentoPago.Preparado)
                {
                    await tx.CommitAsync(ct);
                    return "YA_EXISTE_INTENTO_NO_REEMITIR";
                }
                if (intento.ReservaId.HasValue || !string.IsNullOrWhiteSpace(intento.TransaccionId))
                    throw new InvalidOperationException("INTENTO_LEGACY_O_COBRADO");

                var orden = await _finance.Ordenes.AsNoTracking()
                    .SingleAsync(o => o.Id == ordenId, ct);
                monto = intento.Monto;
                moneda = intento.Moneda;
                clave = intento.ReferenciaOperacion;
                var items = await _finance.ItemsOrden.AsNoTracking()
                    .Where(i => i.OrdenId == ordenId).ToListAsync(ct);
                var links = await _finance.IntentoReservasPago.AsNoTracking()
                    .Where(x => x.IntentoPagoId == intentoId).ToListAsync(ct);
                if (orden.Estado != EstadoOrden.Pendiente ||
                    string.IsNullOrWhiteSpace(orden.IdempotencyKey) ||
                    string.IsNullOrWhiteSpace(orden.CheckoutPayloadHash) ||
                    orden.MontoTotal != monto || moneda != "USD" ||
                    clave != $"PAY-{ordenId:N}" || items.Count is < 2 or > 20 ||
                    items.Count != links.Count ||
                    items.Sum(i => i.PrecioUnitario * i.Cantidad) != monto ||
                    links.Any(l => l.Procedencia != "MultiSkuQa" ||
                        !items.Any(i => i.ProductoId == l.ProductoId &&
                                        i.Cantidad == l.Cantidad)) ||
                    links.Select(x => x.ReservaId).Distinct().Count() != links.Count ||
                    await _finance.ReembolsosPago.AnyAsync(r => r.OrdenId == ordenId, ct) ||
                    await _finance.SplitsPago.AnyAsync(s => s.OrdenId == ordenId, ct))
                    throw new InvalidOperationException("MAPA_FINANCE_INCOMPLETO_PRECOBRO");

                var options = new DbContextOptionsBuilder<CatalogDbContext>()
                    .UseNpgsql(conexion).Options;
                await using var catalog = new CatalogDbContext(options);
                await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
                var reservas = await catalog.ReservasStock.AsNoTracking()
                    .Where(r => r.OrdenId == ordenId).ToListAsync(ct);
                if (reservas.Count != links.Count || reservas.Any(r =>
                    r.Estado != EstadoReserva.Pendiente ||
                    !links.Any(l => l.ReservaId == r.Id &&
                                    l.ProductoId == r.ProductoId &&
                                    l.Cantidad == r.CantidadOriginal)))
                    throw new InvalidOperationException("RESERVAS_CATALOG_INCOMPLETAS_PRECOBRO");

                // Punto de no retorno de reintento automático: persistir Enviado
                // y COMMIT antes de invocar el simulador externo al SQL.
                intento.Estado = EstadoIntentoPago.Enviado;
                intento.ActualizadoUtc = DateTime.UtcNow;
                await _finance.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                _finance.ChangeTracker.Clear();
                throw;
            }
        }

        // Falla deliberada entre compromiso SQL y callback del simulador:
        // el reintento no invoca al proveedor, aun con estado duradero Enviado.
        if (falloDespuesDeMarcarEnviadoQa)
            throw new InvalidOperationException("QA_CAIDA_POST_ENVIADO_ANTES_DE_CALLBACK");

        QaRespuestaPagoSimulado respuesta;
        try
        {
            respuesta = await _simulador.SimularAsync(ordenId, monto, moneda, clave);
        }
        catch
        {
            await MarcarAsync(intentoId, EstadoIntentoPago.Incierto, null, ct);
            return "INCIERTO_NO_REEMITIR";
        }

        if (falloDespuesDeRespuestaQa)
            throw new InvalidOperationException("QA_CAIDA_POST_RESPUESTA_ANTES_DE_PERSISTIR");

        if (respuesta.Exitoso &&
            (string.IsNullOrWhiteSpace(respuesta.TransaccionSimuladaId) ||
             !respuesta.TransaccionSimuladaId.StartsWith("QA-SIM-", StringComparison.Ordinal)))
        {
            await MarcarAsync(intentoId, EstadoIntentoPago.Incierto, null, ct);
            return "INCIERTO_SIN_REFERENCIA";
        }
        if (!respuesta.Exitoso)
        {
            await MarcarAsync(intentoId, EstadoIntentoPago.Rechazado, null, ct);
            return "RECHAZADO_SIMULADO_RESERVAS_AUN_PENDIENTES";
        }

        await MarcarAsync(intentoId, EstadoIntentoPago.Aprobado,
            respuesta.TransaccionSimuladaId, ct);
        return "APROBADO_SIMULADO_SIN_CIERRE_COMERCIAL";
    }

    private async Task MarcarAsync(Guid intentoId, EstadoIntentoPago estado,
        string? id, CancellationToken ct)
    {
        _finance.ChangeTracker.Clear();
        var intento = await _finance.IntentosPago.SingleAsync(i => i.Id == intentoId, ct);
        if (intento.Estado != EstadoIntentoPago.Enviado)
            throw new InvalidOperationException("NO_MUTAR_INTENTO_YA_TERMINAL");
        intento.Estado = estado;
        intento.TransaccionId = id;
        intento.ActualizadoUtc = DateTime.UtcNow;
        await _finance.SaveChangesAsync(ct);
    }
}
