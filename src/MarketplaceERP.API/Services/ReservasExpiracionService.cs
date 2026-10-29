using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MarketplaceERP.API.Services;

/// <summary>
/// I3: vencimiento seguro de reservas no pagadas. La única liberación permitida es
/// sobre una orden ya CANCELADA que NO tenga intento de pago ni reembolso alguno.
/// Una orden Pendiente, Enviada o con cualquier indicio financiero se retiene.
/// No modifica CantidadTotal: solo libera CantidadReservada mediante StockService.
/// </summary>
public static class PoliticaExpiracionReserva
{
    public static EvaluacionReserva Evaluar(
        ReservaStock reserva, EstadoOrden? estadoOrden,
        bool existeIntento, bool existeReembolso, DateTime ahoraUtc)
    {
        if (reserva.CantidadPendiente <= 0)
            return new(false, "SIN_CANTIDAD_PENDIENTE");
        if (reserva.Estado != EstadoReserva.Pendiente)
            return new(false, "RESERVA_NO_PENDIENTE");
        if (reserva.FechaExpiracion is null)
            return new(false, "SIN_VENCIMIENTO_CONFIGURADO");
        if (reserva.FechaExpiracion.Value > ahoraUtc)
            return new(false, "AUN_VIGENTE");
        if (existeIntento || existeReembolso)
            return new(false, "EXISTE_HUELLA_FINANCIERA_REVISAR");
        if (estadoOrden is null)
            return new(false, "ORDEN_NO_ENCONTRADA_REVISAR");
        if (estadoOrden != EstadoOrden.Cancelada)
            return new(false, "ORDEN_NO_CANCELADA_REVISAR");
        return new(true, "ELEGIBLE_CANCELADA_SIN_PAGO");
    }
}

public sealed record EvaluacionReserva(bool Elegible, string Motivo);
public sealed record ReservaExpiracionDetalle(Guid ReservaId, Guid OrdenId,
    int CantidadPendiente, DateTime? FechaExpiracion, bool Elegible, string Motivo);
public sealed record ResumenExpiracion(int Evaluadas, int Expiradas, int Retenidas,
    IReadOnlyList<ReservaExpiracionDetalle> Resultados);

public sealed class ReservasExpiracionService
{
    private readonly CatalogDbContext _catalog;
    private readonly FinanceDbContext _finance;
    private readonly IStockService _stock;
    private readonly ILogger<ReservasExpiracionService> _logger;

    public ReservasExpiracionService(CatalogDbContext catalog,
        FinanceDbContext finance, IStockService stock,
        ILogger<ReservasExpiracionService> logger)
    {
        _catalog = catalog;
        _finance = finance;
        _stock = stock;
        _logger = logger;
    }

    /// <summary>Consulta administrativa. La vista NO autoriza liberaciones.</summary>
    public async Task<IReadOnlyList<ReservaExpiracionDetalle>> ConsultarAsync(
        int limite, CancellationToken ct = default)
    {
        if (limite is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limite));
        var ahora = DateTime.UtcNow;
        var candidatas = await _catalog.ReservasStock.AsNoTracking()
            .Where(r => r.FechaExpiracion != null && r.FechaExpiracion <= ahora &&
                        r.CantidadOriginal > r.CantidadLiberada + r.CantidadConsumida)
            .OrderBy(r => r.FechaExpiracion).ThenBy(r => r.Id)
            .Take(limite).ToListAsync(ct);
        var resultados = new List<ReservaExpiracionDetalle>();
        foreach (var reserva in candidatas)
        {
            var estado = await _finance.Ordenes.AsNoTracking()
                .Where(o => o.Id == reserva.OrdenId)
                .Select(o => (EstadoOrden?)o.Estado).SingleOrDefaultAsync(ct);
            var intento = await _finance.IntentosPago.AsNoTracking()
                .AnyAsync(i => i.OrdenId == reserva.OrdenId, ct);
            var reembolso = await _finance.ReembolsosPago.AsNoTracking()
                .AnyAsync(r => r.OrdenId == reserva.OrdenId, ct);
            var splitLegado = await _finance.SplitsPago.AsNoTracking()
                .AnyAsync(p => p.OrdenId == reserva.OrdenId, ct);
            var decision = PoliticaExpiracionReserva.Evaluar(reserva, estado,
                intento || splitLegado, reembolso, ahora);
            resultados.Add(new ReservaExpiracionDetalle(reserva.Id, reserva.OrdenId,
                reserva.CantidadPendiente, reserva.FechaExpiracion,
                decision.Elegible, decision.Motivo));
        }
        return resultados;
    }

    /// <summary>
    /// Ejecución administrativa en QA con revalidación dentro de UNA transacción
    /// PostgreSQL: bloquea la reserva y la orden Finance; la FK del intento de pago
    /// obliga a serializar su inserción frente al FOR UPDATE de la orden.
    /// StockService comparte el CatalogDbContext y utiliza la transacción existente.
    /// </summary>
    public async Task<ResumenExpiracion> EjecutarAsync(int limite, CancellationToken ct = default)
    {
        if (limite is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(limite));
        if (!_catalog.Database.IsNpgsql() ||
            !string.Equals(_catalog.Database.GetDbConnection().Database,
                "marketplace_erp_i2a_qa", StringComparison.Ordinal))
            throw new InvalidOperationException("La expiración operativa solo está permitida en PostgreSQL QA.");

        // Filtrar primero en SQL: un gran volumen de casos retenidos NO debe
        // impedir procesar reservas elegibles situadas más adelante.
        // La selección es informativa y se REVALIDA bajo locks antes de escribir.
        var ahora = DateTime.UtcNow;
        var seleccion = await _catalog.ReservasStock.FromSqlInterpolated($"""
            SELECT r.* FROM catalog.reservas_stock r
            INNER JOIN finance.ordenes o ON o."Id" = r."OrdenId"
            WHERE r."Estado" = 'Pendiente'
              AND o."Estado" = 'Cancelada'
              AND r."FechaExpiracion" IS NOT NULL
              AND r."FechaExpiracion" <= {ahora}
              AND r."CantidadOriginal" > r."CantidadLiberada" + r."CantidadConsumida"
              AND NOT EXISTS (SELECT 1 FROM finance.intentos_pago i WHERE i."OrdenId" = r."OrdenId")
              AND NOT EXISTS (SELECT 1 FROM finance.reembolsos_pago p WHERE p."OrdenId" = r."OrdenId")
              AND NOT EXISTS (SELECT 1 FROM finance.splits_pago s WHERE s."OrdenId" = r."OrdenId")
            ORDER BY r."FechaExpiracion", r."Id"
            LIMIT {limite}
            """).AsNoTracking().ToListAsync(ct);
        var respuestas = new List<ReservaExpiracionDetalle>();
        var expiradas = 0;
        foreach (var candidato in seleccion)
        {
            // Una transacción por reserva; otra orden no queda bloqueada innecesariamente.
            await using var transaccion = await _catalog.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, ct);
            try
            {
                var reserva = await _catalog.ReservasStock
                    .FromSqlInterpolated($"SELECT * FROM catalog.reservas_stock WHERE \"Id\" = {candidato.Id} FOR UPDATE")
                    .AsTracking().SingleOrDefaultAsync(ct);
                if (reserva is null)
                {
                    respuestas.Add(new ReservaExpiracionDetalle(candidato.Id, candidato.OrdenId,
                        candidato.CantidadPendiente, candidato.FechaExpiracion,
                        false, "RESERVA_YA_NO_EXISTE"));
                    await transaccion.RollbackAsync(ct);
                    continue;
                }

                // La orden se bloquea ANTES de inspeccionar el historial financiero.
                // Un INSERT de intento requiere KEY SHARE sobre finance.ordenes (FK).
                var estadoTexto = await ConsultarEscalarAsync(
                    "SELECT \"Estado\" FROM finance.ordenes WHERE \"Id\" = @orden FOR UPDATE",
                    reserva.OrdenId, ct) as string;
                EstadoOrden? estado = Enum.TryParse<EstadoOrden>(estadoTexto, out var parsed)
                    ? parsed : null;
                var huella = await ConsultarEscalarAsync(
                    "SELECT EXISTS (SELECT 1 FROM finance.intentos_pago WHERE \"OrdenId\" = @orden) " +
                    "OR EXISTS (SELECT 1 FROM finance.reembolsos_pago WHERE \"OrdenId\" = @orden) " +
                    "OR EXISTS (SELECT 1 FROM finance.splits_pago WHERE \"OrdenId\" = @orden)",
                    reserva.OrdenId, ct);
                var decision = PoliticaExpiracionReserva.Evaluar(reserva, estado,
                    huella is true, false, DateTime.UtcNow);
                if (!decision.Elegible)
                {
                    respuestas.Add(new ReservaExpiracionDetalle(reserva.Id, reserva.OrdenId,
                        reserva.CantidadPendiente, reserva.FechaExpiracion, false, decision.Motivo));
                    await transaccion.RollbackAsync(ct);
                    continue;
                }

                var cantidad = reserva.CantidadPendiente;
                await _stock.LiberarStockAsync(reserva.Id);
                // El movimiento LiberacionReserva y el descuento de CantidadReservada
                // ya están en la transacción; cambiar exclusivamente el estado final.
                if (reserva.CantidadPendiente != 0 || reserva.Estado != EstadoReserva.Liberada)
                    throw new InvalidOperationException("La liberación no concluyó; se revierte la transacción.");
                reserva.Estado = EstadoReserva.Expirada;
                reserva.FechaFinalizacion ??= DateTime.UtcNow;
                await _catalog.SaveChangesAsync(ct);
                await transaccion.CommitAsync(ct);
                expiradas++;
                respuestas.Add(new ReservaExpiracionDetalle(reserva.Id, reserva.OrdenId,
                    cantidad, reserva.FechaExpiracion, true, "EXPIRADA_Y_LIBERADA"));
                _logger.LogInformation("I3: reserva vencida liberada en QA. ReservaId={ReservaId}, OrdenId={OrdenId}",
                    reserva.Id, reserva.OrdenId);
            }
            catch
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                // No continuar silenciosamente: requiere investigar antes de más liberaciones.
                throw;
            }
            finally
            {
                _catalog.ChangeTracker.Clear();
            }
        }
        return new ResumenExpiracion(respuestas.Count, expiradas,
            respuestas.Count - expiradas, respuestas);
    }

    private async Task<object?> ConsultarEscalarAsync(string sql, Guid ordenId, CancellationToken ct)
    {
        var conexion = _catalog.Database.GetDbConnection();
        await using var comando = conexion.CreateCommand();
        comando.Transaction = _catalog.Database.CurrentTransaction?.GetDbTransaction();
        comando.CommandText = sql;
        var parametro = comando.CreateParameter();
        parametro.ParameterName = "orden";
        parametro.Value = ordenId;
        comando.Parameters.Add(parametro);
        return await comando.ExecuteScalarAsync(ct);
    }
}
