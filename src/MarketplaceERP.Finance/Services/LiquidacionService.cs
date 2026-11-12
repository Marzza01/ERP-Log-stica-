/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// Servicio de Liquidaciones Periódicas a Vendedores.
/// Implementa el split 90/10 marketplace: 90% vendedor, 10% comisión plataforma.
/// Al liquidar genera asiento contable: DEBE 2108 (Pasivo Vendedores) / HABER 1101 (Caja).
/// PDF 1 y PDF 2 — Módulo de Comisiones y Liquidaciones.
/// </summary>
public class LiquidacionService : ILiquidacionService
{
    private readonly FinanceDbContext _db;
    private readonly IContabilidadService _contabilidad;
    private readonly ILogger<LiquidacionService> _logger;
    private readonly PaymentOperationsMode _paymentMode;

    // Cuentas contables para el asiento de liquidación
    private const string CAJA_BANCOS       = "1101";
    private const string PASIVO_VENDEDORES = "2108";

    public LiquidacionService(
        FinanceDbContext db,
        IContabilidadService contabilidad,
        ILogger<LiquidacionService> logger,
        PaymentOperationsMode paymentMode)
    {
        _db = db;
        _contabilidad = contabilidad;
        _logger = logger;
        _paymentMode = paymentMode;
    }

    // ── Generar Liquidación ───────────────────────────────────────────────────
    public async Task<Liquidacion> GenerarLiquidacionAsync(
        Guid vendedorId, CancellationToken cancellationToken = default)
    {
        if (_paymentMode.IsDisabled)
            throw new InvalidOperationException("Liquidaciones deshabilitadas en este entorno.");

        if (vendedorId == Guid.Empty)
            throw new ArgumentException("El vendedor de la liquidación es obligatorio.", nameof(vendedorId));

        // Misma transacción: se bloquea por vendedor antes de consultar splits
        // para evitar que dos instancias reclamen el mismo lote.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (_db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var lockKey = BitConverter.ToInt64(vendedorId.ToByteArray(), 0);
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        }

        var pendientes = await _db.SplitsPago
            .Include(s => s.Orden)
            .Where(s => s.Orden.VendedorId == vendedorId
                     && s.LiquidacionId == null
                     && s.Orden.Estado == EstadoOrden.Entregada)
            .OrderBy(s => s.FechaProcesado)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        if (pendientes.Count == 0)
            throw new InvalidOperationException(
                $"El vendedor {vendedorId} no tiene saldos pendientes de liquidación.");

        if (pendientes.Any(s => s.MontoVendedor <= 0m ||
                                s.Orden.VendedorId != vendedorId ||
                                s.Orden.Moneda != "USD"))
            throw new InvalidOperationException(
                "El lote contiene splits de importe, propietario o moneda inconsistentes.");

        var liquidacion = new Liquidacion
        {
            Id = Guid.NewGuid(),
            VendedorId = vendedorId,
            // Ancla 1:1 de lectura histórica hasta contraer el esquema.
            SplitPagoId = pendientes[0].Id,
            MontoLiquidar = pendientes.Sum(s => s.MontoVendedor),
            Moneda = "USD",
            Estado = EstadoLiquidacion.Pendiente,
            FechaCreacion = DateTime.UtcNow,
        };

        // Cada split pertenece a una única liquidación gracias a su FK.
        // El vínculo y la liquidación se insertan en una sola transacción.
        _db.Liquidaciones.Add(liquidacion);
        foreach (var split in pendientes)
        {
            split.LiquidacionId = liquidacion.Id;
            split.LiquidacionGrupo = liquidacion;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg &&
            (pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation ||
             pg.SqlState == Npgsql.PostgresErrorCodes.ForeignKeyViolation))
        {
            throw new InvalidOperationException(
                "El lote fue reclamado o modificado por otra solicitud; consulte el historial.", ex);
        }

        _logger.LogInformation(
            "Liquidación {Id} agrupada para vendedor {VendedorId}. Splits={Count}, Monto={Monto:C}",
            liquidacion.Id, vendedorId, pendientes.Count, liquidacion.MontoLiquidar);
        return liquidacion;
    }

    // ── Procesar Pago ─────────────────────────────────────────────────────────
    public async Task<Liquidacion> ProcesarPagoAsync(
        Guid liquidacionId, CancellationToken cancellationToken = default)
    {
        if (_paymentMode.IsDisabled)
            throw new InvalidOperationException("Desembolsos deshabilitados en este entorno.");

        // En bases relacionales, estado y asiento deben confirmarse juntos.
        // El UPDATE condicional reclama exclusivamente la liquidación pendiente:
        // una llamada concurrente no puede ejecutar otro asiento ni otro pago.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var liquidacion = await _db.Liquidaciones
            .FirstOrDefaultAsync(l => l.Id == liquidacionId, cancellationToken)
            ?? throw new InvalidOperationException($"Liquidación {liquidacionId} no encontrada.");

        if (liquidacion.Estado != EstadoLiquidacion.Pendiente)
            throw new InvalidOperationException(
                $"La liquidación {liquidacionId} ya está en estado {liquidacion.Estado}.");

        // La FK 1:N es la única fuente autoritativa. Un ancla histórica
        // sin backfill NO permite desembolso ni siquiera en simulación.
        var splits = await _db.SplitsPago.AsNoTracking()
            .Where(s => s.LiquidacionId == liquidacion.Id)
            .Select(s => new
            {
                s.Id,
                s.MontoVendedor,
                VendedorId = s.Orden.VendedorId,
                EstadoOrden = s.Orden.Estado,
                Moneda = s.Orden.Moneda
            })
            .ToListAsync(cancellationToken);
        if (splits.Count == 0 ||
            !splits.Any(s => s.Id == liquidacion.SplitPagoId) ||
            splits.Any(s => s.VendedorId != liquidacion.VendedorId ||
                            s.EstadoOrden != EstadoOrden.Entregada ||
                            s.MontoVendedor <= 0m ||
                            s.Moneda != liquidacion.Moneda) ||
            splits.Sum(s => s.MontoVendedor) != liquidacion.MontoLiquidar)
            throw new InvalidOperationException(
                "Liquidación con splits, propietario, estado o importe inconsistentes; requiere revisión manual.");

        if (await _db.PartidasContables.AsNoTracking().AnyAsync(p =>
            p.TipoOperacion == "Liquidacion" && p.ReferenciaOrigenId == liquidacion.Id,
            cancellationToken))
            throw new InvalidOperationException(
                "Ya existe un asiento para esta liquidación; requiere conciliación manual.");

        if (_db.Database.IsRelational())
        {
            var reservadas = await _db.Liquidaciones
                .Where(l => l.Id == liquidacionId && l.Estado == EstadoLiquidacion.Pendiente)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(l => l.Estado, EstadoLiquidacion.EnProceso), cancellationToken);
            if (reservadas != 1)
                throw new InvalidOperationException(
                    "La liquidación está siendo procesada por otra solicitud; consulte su estado.");
        }

        // Solo la simulación de pagos está habilitada actualmente. Este asiento
        // no debe interpretarse como evidencia de transferencia bancaria real.
        var ahora = DateTime.UtcNow;
        var asientoMonto = liquidacion.MontoLiquidar;
        var partida = new PartidaContable
        {
            Id = Guid.NewGuid(),
            NumeroPartida = $"LIQ-{ahora:yyyy}-{liquidacion.Id:N}",
            Fecha = ahora,
            Concepto = $"Liquidación vendedor {liquidacion.VendedorId:N} — Liq#{liquidacion.Id:N}",
            TipoOperacion = "Liquidacion",
            ReferenciaOrigenId = liquidacion.Id,
            DocumentoReferencia = liquidacion.Id.ToString("N"),
            TotalDebe = asientoMonto,
            TotalHaber = asientoMonto,
            FechaRegistro = ahora,
            Lineas = new List<LineaPartidaContable>
            {
                new() { Id = Guid.NewGuid(), CodigoCuenta = PASIVO_VENDEDORES,
                        NombreCuenta = "Pasivos por Liquidar a Vendedores Marketplace",
                        Debe = asientoMonto, Haber = 0m,
                        Glosa = $"Cancelación obligación vendedor — Liq#{liquidacion.Id:N}" },
                new() { Id = Guid.NewGuid(), CodigoCuenta = CAJA_BANCOS,
                        NombreCuenta = "Efectivo y Equivalentes de Efectivo",
                        Debe = 0m, Haber = asientoMonto,
                        Glosa = "Desembolso a cuenta de vendedor" },
            }
        };

        _db.PartidasContables.Add(partida);
        liquidacion.Estado = EstadoLiquidacion.Completada;
        liquidacion.FechaLiquidacion = ahora;
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Liquidación simulada {Id} completada. Asiento={Partida}. Monto={Monto:C}",
            liquidacion.Id, partida.NumeroPartida, asientoMonto);

        return liquidacion;
    }

    // ── Historial ─────────────────────────────────────────────────────────────
    public async Task<IEnumerable<Liquidacion>> ObtenerHistorialAsync(
        Guid? vendedorId = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Liquidaciones.AsQueryable();
        if (vendedorId.HasValue)
            query = query.Where(l => l.VendedorId == vendedorId.Value);

        return await query
            .AsNoTracking()
            .Include(l => l.SplitsPago)
                .ThenInclude(s => s.Orden)
            .OrderByDescending(l => l.FechaCreacion)
            .ToListAsync(cancellationToken);
    }

    // ── KPIs por Vendedor ─────────────────────────────────────────────────────
    public async Task<IEnumerable<KpiVendedor>> ObtenerKpisVendedoresAsync(
        CancellationToken cancellationToken = default)
    {
        var ordenesPorVendedor = await _db.Ordenes
            .GroupBy(o => o.VendedorId)
            .Select(g => new
            {
                VendedorId    = g.Key,
                TotalOrdenes  = g.Count(),
                Entregadas    = g.Count(o => o.Estado == EstadoOrden.Entregada),
            })
            .ToListAsync(cancellationToken);

        var liquidacionesPorVendedor = await _db.Liquidaciones
            .Where(l => l.Estado == EstadoLiquidacion.Completada)
            .GroupBy(l => l.VendedorId)
            .Select(g => new { VendedorId = g.Key, Total = g.Sum(l => l.MontoLiquidar) })
            .ToListAsync(cancellationToken);

        var kpis = ordenesPorVendedor.Select(o =>
        {
            double fillRate = o.TotalOrdenes > 0
                ? (double)o.Entregadas / o.TotalOrdenes * 100.0
                : 0.0;

            decimal liquidado = liquidacionesPorVendedor
                .FirstOrDefault(l => l.VendedorId == o.VendedorId)?.Total ?? 0m;

            return new KpiVendedor(o.VendedorId, o.TotalOrdenes, o.Entregadas, liquidado, Math.Round(fillRate, 2));
        });

        return kpis.OrderByDescending(k => k.TotalLiquidado);
    }
}
