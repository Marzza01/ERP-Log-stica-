using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Dashboard Ejecutivo de KPIs Logísticos — Célula A+B integrada.
/// Expone los 8 KPIs requeridos por el PDF con semáforo Verde/Amarillo/Rojo.
/// Requisito: PDF2 — Indicadores de Desempeño Logístico.
///
/// KPIs implementados:
///   1. Fill Rate (tasa de órdenes completadas)
///   2. OTIF — On-Time In-Full
///   3. Tasa de Cancelación
///   4. Tiempo Promedio de Ciclo (horas)
///   5. Rotación de Inventario
///   6. Tasa de Devolución RMA
///   7. Precisión del 3-Way Match
///   8. Margen de Comisión Marketplace (10%)
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class KpisDashboardController : ControllerBase
{
    private readonly FinanceDbContext _financeDb;
    private readonly CatalogDbContext _catalogDb;
    private readonly ILogger<KpisDashboardController> _logger;

    public KpisDashboardController(
        FinanceDbContext financeDb,
        CatalogDbContext catalogDb,
        ILogger<KpisDashboardController> logger)
    {
        _financeDb = financeDb;
        _catalogDb = catalogDb;
        _logger = logger;
    }

    // ── GET /api/kpis-dashboard ───────────────────────────────────────────────
    /// <summary>
    /// Retorna el panel completo con los 8 KPIs y semáforos.
    /// </summary>
    [HttpGet(Name = "DashboardKpis")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ObtenerDashboard(
        [FromQuery] int diasPeriodo = 30,
        CancellationToken cancellationToken = default)
    {
        var desde = DateTime.UtcNow.AddDays(-diasPeriodo);
        var hasta = DateTime.UtcNow;

        // ── Datos Finance ─────────────────────────────────────────────────────
        var ordenes = await _financeDb.Ordenes
            .Where(o => o.FechaCreacion >= desde)
            .ToListAsync(cancellationToken);

        var matches = await _financeDb.MatchesTridireccionales
            .Where(m => m.FechaAuditoria >= desde)
            .ToListAsync(cancellationToken);

        var splits = await _financeDb.SplitsPago
            .Where(s => s.FechaProcesado >= desde)
            .ToListAsync(cancellationToken);

        // ── Datos Catalog ─────────────────────────────────────────────────────
        var rmas = await _catalogDb.DevolucionesRMA
            .Where(r => r.FechaSolicitud >= desde)
            .ToListAsync(cancellationToken);

        var stocks = await _catalogDb.StockFisico.ToListAsync(cancellationToken);

        // ── KPI 1: Fill Rate ──────────────────────────────────────────────────
        int totalOrdenes    = ordenes.Count;
        int entregadas      = ordenes.Count(o => o.Estado == EstadoOrden.Entregada);
        double fillRate     = totalOrdenes > 0 ? (double)entregadas / totalOrdenes * 100.0 : 0;

        // ── KPI 2: OTIF (On-Time In-Full) ─────────────────────────────────────
        var ordenesConFechaEstimada = ordenes
            .Where(o => o.Estado == EstadoOrden.Entregada
                     && o.FechaEntregaReal.HasValue
                     && o.FechaEntregaEstimada.HasValue)
            .ToList();
        int onTime      = ordenesConFechaEstimada.Count(o => o.FechaEntregaReal!.Value <= o.FechaEntregaEstimada!.Value);
        double otif     = ordenesConFechaEstimada.Count > 0 ? (double)onTime / ordenesConFechaEstimada.Count * 100.0 : 0;

        // ── KPI 3: Tasa de Cancelación ────────────────────────────────────────
        int canceladas      = ordenes.Count(o => o.Estado == EstadoOrden.Cancelada);
        double tasaCancelacion = totalOrdenes > 0 ? (double)canceladas / totalOrdenes * 100.0 : 0;

        // ── KPI 4: Tiempo Promedio de Ciclo (horas) ───────────────────────────
        var tiemposCiclo = ordenes
            .Where(o => o.Estado == EstadoOrden.Entregada && o.FechaEntregaReal.HasValue)
            .Select(o => (o.FechaEntregaReal!.Value - o.FechaCreacion).TotalHours)
            .ToList();
        double tiempoCicloPromedio = tiemposCiclo.Count > 0 ? tiemposCiclo.Average() : 0;

        // ── KPI 5: Rotación de Inventario (simplificada) ──────────────────────
        decimal totalInventarioValor = stocks.Sum(s => s.CantidadTotal * 1m); // unidades totales en bodega
        decimal ventasTotales        = ordenes.Where(o => o.Estado == EstadoOrden.Entregada).Sum(o => o.MontoTotal);
        double rotacionInventario    = totalInventarioValor > 0
            ? (double)(ventasTotales / totalInventarioValor)
            : 0;

        // ── KPI 6: Tasa de Devolución RMA ─────────────────────────────────────
        int totalRMA        = rmas.Count;
        double tasaRMA      = entregadas > 0 ? (double)totalRMA / entregadas * 100.0 : 0;

        // ── KPI 7: Precisión del 3-Way Match ──────────────────────────────────
        int totalMatches    = matches.Count;
        int matchesConformes = matches.Count(m => m.Resultado == Estado3WayMatch.Conforme);
        double precisionMatch = totalMatches > 0 ? (double)matchesConformes / totalMatches * 100.0 : 100;

        // ── KPI 8: Margen de Comisión Marketplace ─────────────────────────────
        decimal totalVentas     = splits.Sum(s => s.MontoTotal);
        decimal totalComision   = splits.Sum(s => s.MontoMarketplace);
        double margenComision   = totalVentas > 0 ? (double)(totalComision / totalVentas) * 100.0 : 10.0;

        // ── Construir respuesta con semáforos ─────────────────────────────────
        var dashboard = new
        {
            periodo = new { desde = desde.ToString("yyyy-MM-dd"), hasta = hasta.ToString("yyyy-MM-dd"), diasPeriodo },
            generadoEn = DateTime.UtcNow,
            resumenEjecutivo = new
            {
                totalOrdenes,
                ordenesEntregadas = entregadas,
                ordenesCanceladas = canceladas,
                totalMatches3Way  = totalMatches,
                totalRmasPeriodo  = totalRMA,
                totalComisionGenerada = totalComision,
            },
            kpis = new object[]
            {
                Kpi(1, "Fill Rate",
                    Math.Round(fillRate, 2), "%",
                    meta: 95, umbralVerde: 95, umbralAmarillo: 85,
                    descripcion: "Porcentaje de órdenes completadas vs. recibidas."),

                Kpi(2, "OTIF (On-Time In-Full)",
                    Math.Round(otif, 2), "%",
                    meta: 95, umbralVerde: 95, umbralAmarillo: 85,
                    descripcion: "Órdenes entregadas a tiempo y en su totalidad."),

                Kpi(3, "Tasa de Cancelación",
                    Math.Round(tasaCancelacion, 2), "%",
                    meta: 5, umbralVerde: 5, umbralAmarillo: 10,
                    invertido: true,
                    descripcion: "Porcentaje de órdenes canceladas. Menor es mejor."),

                Kpi(4, "Tiempo Ciclo Pedido",
                    Math.Round(tiempoCicloPromedio, 1), "horas",
                    meta: 48, umbralVerde: 48, umbralAmarillo: 72,
                    invertido: true,
                    descripcion: "Tiempo promedio desde creación hasta entrega."),

                Kpi(5, "Rotación de Inventario",
                    Math.Round(rotacionInventario, 2), "veces",
                    meta: 4, umbralVerde: 4, umbralAmarillo: 2,
                    descripcion: "Veces que el inventario se renueva en el período."),

                Kpi(6, "Tasa de Devolución RMA",
                    Math.Round(tasaRMA, 2), "%",
                    meta: 3, umbralVerde: 3, umbralAmarillo: 7,
                    invertido: true,
                    descripcion: "Porcentaje de órdenes entregadas con devolución."),

                Kpi(7, "Precisión 3-Way Match",
                    Math.Round(precisionMatch, 2), "%",
                    meta: 98, umbralVerde: 98, umbralAmarillo: 90,
                    descripcion: "Porcentaje de matches conformes sin discrepancias."),

                Kpi(8, "Margen Comisión Marketplace",
                    Math.Round(margenComision, 2), "%",
                    meta: 10, umbralVerde: 9.5, umbralAmarillo: 8,
                    descripcion: "Comisión promedio retenida por el marketplace (objetivo 10%)."),
            }
        };

        return Ok(dashboard);
    }

    // ── GET /api/kpis-dashboard/modelo-financiero ─────────────────────────────
    /// <summary>
    /// Proyección financiera de 12 meses basada en tendencias actuales.
    /// Exportable como JSON para consumo en Excel o Power BI.
    /// </summary>
    [HttpGet("modelo-financiero", Name = "ModeloFinanciero12Meses")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ModeloFinanciero(CancellationToken cancellationToken)
    {
        // Calcular base mensual de los últimos 30 días
        var desde = DateTime.UtcNow.AddDays(-30);
        var ordenesBase = await _financeDb.Ordenes
            .Where(o => o.FechaCreacion >= desde && o.Estado == EstadoOrden.Entregada)
            .ToListAsync(cancellationToken);

        decimal ventasBase       = ordenesBase.Sum(o => o.MontoTotal);
        decimal comisionBase     = ventasBase * 0.10m;
        decimal costoOperaciones = ventasBase * 0.065m; // estimado 6.5% ops
        decimal utilidadBase     = comisionBase - costoOperaciones;

        // Factor de crecimiento mensual (8% compuesto — proyección conservadora)
        const double tasaCrecimiento = 0.08;

        var proyeccion = Enumerable.Range(0, 12).Select(mes =>
        {
            double factor     = Math.Pow(1 + tasaCrecimiento, mes);
            decimal ventas    = Math.Round(ventasBase * (decimal)factor, 2);
            decimal comision  = Math.Round(ventas * 0.10m, 2);
            decimal costos    = Math.Round(ventas * 0.065m, 2);
            decimal utilidad  = comision - costos;
            decimal margen    = comision > 0 ? Math.Round(utilidad / comision * 100m, 2) : 0m;

            return new
            {
                mes = mes + 1,
                periodo = DateTime.UtcNow.AddMonths(mes).ToString("yyyy-MM"),
                ventasProyectadas = ventas,
                comisionMarketplace = comision,
                costosOperativos = costos,
                utilidadNeta = utilidad,
                margenNetoPct = margen,
                factorCrecimiento = Math.Round(factor, 4)
            };
        }).ToList();

        return Ok(new
        {
            generadoEn = DateTime.UtcNow,
            supuestos = new
            {
                baseCalculoUltimos30Dias = true,
                tasaCrecimientoMensual = "8%",
                comisionMarketplace = "10%",
                costosOperativosEstimados = "6.5% de ventas",
                moneda = "USD"
            },
            resumenAnual = new
            {
                ventasTotalesAnuales  = proyeccion.Sum(p => p.ventasProyectadas),
                comisionTotalAnual    = proyeccion.Sum(p => p.comisionMarketplace),
                costosTotalesAnuales  = proyeccion.Sum(p => p.costosOperativos),
                utilidadTotalAnual    = proyeccion.Sum(p => p.utilidadNeta),
            },
            meses = proyeccion
        });
    }

    // ── Helper: construir objeto KPI con semáforo ──────────────────────────────
    private static object Kpi(int numero, string nombre, double valor, string unidad,
        double meta, double umbralVerde, double umbralAmarillo,
        string descripcion = "", bool invertido = false)
    {
        string semaforo = invertido
            ? (valor <= umbralVerde ? "Verde" : valor <= umbralAmarillo ? "Amarillo" : "Rojo")
            : (valor >= umbralVerde ? "Verde" : valor >= umbralAmarillo ? "Amarillo" : "Rojo");

        return new
        {
            numero, nombre, valor, unidad, meta, semaforo, descripcion,
            cumpleMeta = invertido ? valor <= meta : valor >= meta
        };
    }
}
