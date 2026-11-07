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

        // Finance almacena una relación Liquidacion <-> SplitPago 1:1. No se puede
        // agrupar más de un split en MontoLiquidar sin cambiar el modelo y su
        // migración: los splits no referenciados quedarían disponibles de nuevo.
        // Emitir una liquidación por split hasta implementar 1:N de forma atómica.
        var splitPendiente = await _db.SplitsPago
            .Where(s => s.Orden.VendedorId == vendedorId
                     && s.Liquidacion == null
                     && s.Orden.Estado == EstadoOrden.Entregada)
            .OrderBy(s => s.FechaProcesado)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (splitPendiente is null)
            throw new InvalidOperationException(
                $"El vendedor {vendedorId} no tiene saldos pendientes de liquidación.");

        if (splitPendiente.MontoVendedor <= 0m)
            throw new InvalidOperationException("El split pendiente tiene un monto vendedor inválido.");

        var liquidacion = new Liquidacion
        {
            Id = Guid.NewGuid(),
            VendedorId = vendedorId,
            SplitPagoId = splitPendiente.Id,
            MontoLiquidar = splitPendiente.MontoVendedor,
            Moneda = "USD",
            Estado = EstadoLiquidacion.Pendiente,
            FechaCreacion = DateTime.UtcNow,
        };

        _db.Liquidaciones.Add(liquidacion);
        try
        {
            // El índice UNIQUE de finance.liquidaciones.SplitPagoId impide que
            // dos solicitudes concurrentes liquiden el mismo split en PostgreSQL.
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg &&
            pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException(
                "El split ya fue reclamado por otra liquidación; consulte el historial antes de reintentar.", ex);
        }

        _logger.LogInformation("Liquidación {Id} generada para vendedor {VendedorId}. Monto={Monto:C}",
            liquidacion.Id, vendedorId, liquidacion.MontoLiquidar);

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

        // Verificar nuevamente el split original antes de contabilizar; los
        // registros históricos creados con la suma incorrecta quedan bloqueados.
        var split = await _db.SplitsPago.AsNoTracking()
            .Where(s => s.Id == liquidacion.SplitPagoId)
            .Select(s => new
            {
                s.MontoVendedor,
                VendedorId = s.Orden.VendedorId,
                EstadoOrden = s.Orden.Estado
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (split is null ||
            split.VendedorId != liquidacion.VendedorId ||
            split.EstadoOrden != EstadoOrden.Entregada ||
            split.MontoVendedor <= 0m ||
            split.MontoVendedor != liquidacion.MontoLiquidar)
            throw new InvalidOperationException(
                "Liquidación con split, propietario, estado o importe inconsistentes; requiere revisión manual.");

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
