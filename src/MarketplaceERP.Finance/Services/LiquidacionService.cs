/**
 * ERP desarrollado por Marcelo Zepeda
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
        // Obtener splits de este vendedor que aún no tienen liquidación
        var splitsPendientes = await _db.SplitsPago
            .Include(s => s.Liquidacion)
            .Include(s => s.Orden)
            .Where(s => s.Orden.VendedorId == vendedorId
                     && s.Liquidacion == null
                     && s.Orden.Estado == EstadoOrden.Entregada)
            .ToListAsync(cancellationToken);

        if (!splitsPendientes.Any())
            throw new InvalidOperationException(
                $"El vendedor {vendedorId} no tiene saldos pendientes de liquidación.");

        decimal montoTotal = splitsPendientes.Sum(s => s.MontoVendedor);

        // Tomar el primer split como referencia (en producción se haría uno por período)
        var splitRef = splitsPendientes.First();

        var liquidacion = new Liquidacion
        {
            Id = Guid.NewGuid(),
            VendedorId = vendedorId,
            SplitPagoId = splitRef.Id,
            MontoLiquidar = montoTotal,
            Moneda = "USD",
            Estado = EstadoLiquidacion.Pendiente,
            FechaCreacion = DateTime.UtcNow,
        };

        _db.Liquidaciones.Add(liquidacion);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Liquidación {Id} generada para vendedor {VendedorId}. Monto={Monto:C}",
            liquidacion.Id, vendedorId, montoTotal);

        return liquidacion;
    }

    // ── Procesar Pago ─────────────────────────────────────────────────────────
    public async Task<Liquidacion> ProcesarPagoAsync(
        Guid liquidacionId, CancellationToken cancellationToken = default)
    {
        var liquidacion = await _db.Liquidaciones
            .FirstOrDefaultAsync(l => l.Id == liquidacionId, cancellationToken)
            ?? throw new InvalidOperationException($"Liquidación {liquidacionId} no encontrada.");

        if (liquidacion.Estado != EstadoLiquidacion.Pendiente)
            throw new InvalidOperationException(
                $"La liquidación {liquidacionId} ya está en estado {liquidacion.Estado}.");

        liquidacion.Estado = EstadoLiquidacion.EnProceso;
        await _db.SaveChangesAsync(cancellationToken);

        // Generar asiento contable de desembolso:
        // DEBE: 2108 Pasivo Vendedores (se cancela la obligación)
        // HABER: 1101 Caja (sale el dinero)
        var asientoMonto = liquidacion.MontoLiquidar;
        var partida = new PartidaContable
        {
            Id = Guid.NewGuid(),
            NumeroPartida = $"LIQ-{DateTime.UtcNow:yyyy}-{await _db.PartidasContables.CountAsync(cancellationToken) + 1:D6}",
            Fecha = DateTime.UtcNow,
            Concepto = $"Liquidación vendedor {liquidacion.VendedorId:N} — Liq#{liquidacion.Id:N}",
            TipoOperacion = "Liquidacion",
            ReferenciaOrigenId = liquidacion.Id,
            DocumentoReferencia = liquidacion.Id.ToString("N"),
            TotalDebe = asientoMonto,
            TotalHaber = asientoMonto,
            FechaRegistro = DateTime.UtcNow,
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
        liquidacion.FechaLiquidacion = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Liquidación {Id} pagada. Asiento={Partida}. Monto={Monto:C}",
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
