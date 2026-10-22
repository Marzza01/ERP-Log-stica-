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

    // Cuentas contables para el asiento de liquidación
    private const string CAJA_BANCOS       = "1101";
    private const string PASIVO_VENDEDORES = "2108";

    public LiquidacionService(
        FinanceDbContext db,
        IContabilidadService contabilidad,
        ILogger<LiquidacionService> logger)
    {
        _db = db;
        _contabilidad = contabilidad;
        _logger = logger;
    }

    // ── Generar Liquidación ───────────────────────────────────────────────────
    public async Task<Liquidacion> GenerarLiquidacionAsync(
        Guid vendedorId, CancellationToken cancellationToken = default)
    {
        // Envolver en transacción para garantizar que la creación de la liquidación y la marcación de splits sean atómicas
        var executionStrategy = _db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Si el proveedor de base de datos soporta raw SQL (PostgreSQL), bloqueamos con SELECT ... FOR UPDATE
            // para evitar que dos transacciones concurrentes lean y procesen los mismos splits en paralelo.
            List<SplitPago> splitsPendientes;
            if (_db.Database.IsRelational())
            {
                splitsPendientes = await _db.SplitsPago
                    .FromSqlRaw(@"
                        SELECT s.* FROM finance.splits_pago s
                        INNER JOIN finance.ordenes o ON o.""Id"" = s.""OrdenId""
                        WHERE o.""VendedorId"" = {0}
                          AND s.""LiquidacionId"" IS NULL
                          AND o.""Estado"" = 'Entregada'
                        FOR UPDATE OF s
                    ", vendedorId)
                    .Include(s => s.Orden)
                    .ToListAsync(cancellationToken);
            }
            else
            {
                splitsPendientes = await _db.SplitsPago
                    .Include(s => s.Orden)
                    .Where(s => s.Orden.VendedorId == vendedorId
                             && s.LiquidacionId == null
                             && s.Orden.Estado == EstadoOrden.Entregada)
                    .ToListAsync(cancellationToken);
            }

            if (!splitsPendientes.Any())
                throw new InvalidOperationException(
                    $"El vendedor {vendedorId} no tiene saldos pendientes de liquidación.");

            decimal montoTotal = splitsPendientes.Sum(s => s.MontoVendedor);

            var liquidacion = new Liquidacion
            {
                Id = Guid.NewGuid(),
                VendedorId = vendedorId,
                MontoLiquidar = montoTotal,
                Moneda = "USD",
                Estado = EstadoLiquidacion.Pendiente,
                FechaCreacion = DateTime.UtcNow,
            };

            _db.Liquidaciones.Add(liquidacion);

            // Asociar todos los splits pendientes a esta liquidación (relación 1:N)
            foreach (var split in splitsPendientes)
            {
                split.LiquidacionId = liquidacion.Id;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Liquidación {Id} generada para vendedor {VendedorId}. Splits={Cantidad}, Monto={Monto:C}",
                liquidacion.Id, vendedorId, splitsPendientes.Count, montoTotal);

            return liquidacion;
        });
    }

    // ── Procesar Pago ─────────────────────────────────────────────────────────
    public async Task<Liquidacion> ProcesarPagoAsync(
        Guid liquidacionId, CancellationToken cancellationToken = default)
    {
        var executionStrategy = _db.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            var liquidacion = await _db.Liquidaciones
                .FirstOrDefaultAsync(l => l.Id == liquidacionId, cancellationToken)
                ?? throw new InvalidOperationException($"Liquidación {liquidacionId} no encontrada.");

            if (liquidacion.Estado != EstadoLiquidacion.Pendiente)
                throw new InvalidOperationException(
                    $"La liquidación {liquidacionId} ya está en estado {liquidacion.Estado}.");

            // Marcar como completada en una única operación atómica con la partida contable
            liquidacion.Estado = EstadoLiquidacion.Completada;
            liquidacion.FechaLiquidacion = DateTime.UtcNow;

            // Generar asiento contable de desembolso mediante IContabilidadService (mismo DbContext y transacción):
            // DEBE: 2108 Pasivo Vendedores (se cancela la obligación)
            // HABER: 1101 Caja (sale el dinero)
            var asientoMonto = liquidacion.MontoLiquidar;
            var lineasAsiento = new List<LineaAsientoInput>
            {
                new(PASIVO_VENDEDORES, asientoMonto, 0m, $"Cancelación obligación vendedor — Liq#{liquidacion.Id:N}"),
                new(CAJA_BANCOS, 0m, asientoMonto, "Desembolso a cuenta de vendedor")
            };

            var partida = await _contabilidad.GenerarAsientoManualAsync(
                concepto: $"Liquidación vendedor {liquidacion.VendedorId:N} — Liq#{liquidacion.Id:N}",
                tipoOperacion: "Liquidacion",
                lineas: lineasAsiento,
                referenciaOrigenId: liquidacion.Id,
                documentoReferencia: liquidacion.Id.ToString("N"),
                cancellationToken: cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Liquidación {Id} pagada atómicamente. Asiento={Partida}. Monto={Monto:C}",
                liquidacion.Id, partida.NumeroPartida, asientoMonto);

            return liquidacion;
        });
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
