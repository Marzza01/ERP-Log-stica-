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
/// Servicio de Contabilidad General. Genera asientos de partida doble (Libro Diario y Mayor).
/// Plan de Cuentas simplificado adaptado para El Salvador (IVA 13%).
/// PDF 1 - Grupo B / Requisito R-CONT-01 a R-CONT-05.
/// </summary>
public class ContabilidadService : IContabilidadService
{
    private readonly FinanceDbContext _dbContext;
    private readonly ILogger<ContabilidadService> _logger;

    // ── Plan de Cuentas ──────────────────────────────────────────────────────
    private const string CAJA_BANCOS       = "1101"; // Efectivo y Equivalentes
    private const string CXC_CLIENTES      = "1105"; // Cuentas por Cobrar Comerciales
    private const string INVENTARIO        = "1108"; // Inventarios de Mercaderías
    private const string IVA_CREDITO       = "1110"; // IVA Crédito Fiscal (13% compras)
    private const string CXP_PROVEEDORES   = "2101"; // Cuentas por Pagar Comerciales
    private const string IVA_DEBITO        = "2105"; // IVA Débito Fiscal (13% ventas)
    private const string PASIVO_VENDEDORES = "2108"; // Pasivos por Liquidar a Vendedores
    private const string INGRESOS_VENTAS   = "4101"; // Ingresos por Ventas / Comisiones
    private const string COSTO_VENTAS      = "5101"; // Costo de Ventas

    private static readonly Dictionary<string, string> NombresCuenta = new()
    {
        [CAJA_BANCOS]       = "Efectivo y Equivalentes de Efectivo",
        [CXC_CLIENTES]      = "Cuentas por Cobrar Comerciales",
        [INVENTARIO]        = "Inventarios de Mercaderías",
        [IVA_CREDITO]       = "IVA Crédito Fiscal (13%)",
        [CXP_PROVEEDORES]   = "Cuentas por Pagar Comerciales",
        [IVA_DEBITO]        = "IVA Débito Fiscal (13%)",
        [PASIVO_VENDEDORES] = "Pasivos por Liquidar a Vendedores Marketplace",
        [INGRESOS_VENTAS]   = "Ingresos por Ventas y Comisiones",
        [COSTO_VENTAS]      = "Costo de Ventas",
    };

    public ContabilidadService(FinanceDbContext dbContext, ILogger<ContabilidadService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // ── Asiento de Venta ─────────────────────────────────────────────────────
    /// <summary>
    /// Genera el asiento contable para una venta del OMS.
    /// Factura de Consumidor Final o Crédito Fiscal (diferencia en cuenta destino).
    ///   DEBE: 1101 Caja / 1105 CxC por el total con IVA
    ///   HABER: 4101 Ingresos por el subtotal sin IVA
    ///   HABER: 2105 IVA Débito Fiscal por el IVA
    /// </summary>
    public async Task<PartidaContable> GenerarAsientoVentaAsync(
        Guid ordenId, decimal montoVenta, decimal iva,
        string tipoComprobante, CancellationToken cancellationToken = default)
    {
        decimal totalConIva = montoVenta + iva;
        bool esFacturaCredito = tipoComprobante.Contains("Crédito", StringComparison.OrdinalIgnoreCase)
                             || tipoComprobante.Contains("Credito", StringComparison.OrdinalIgnoreCase)
                             || tipoComprobante.Contains("CCF", StringComparison.OrdinalIgnoreCase);
        string cuentaActivo = esFacturaCredito ? CXC_CLIENTES : CAJA_BANCOS;
        string numeroPartida = await GenerarNumeroPartidaAsync("VTA", cancellationToken);

        var partida = new PartidaContable
        {
            Id = Guid.NewGuid(),
            NumeroPartida = numeroPartida,
            Fecha = DateTime.UtcNow,
            Concepto = $"Venta OMS Orden {ordenId:N} - {tipoComprobante}",
            TipoOperacion = "Venta",
            ReferenciaOrigenId = ordenId,
            DocumentoReferencia = ordenId.ToString("N"),
            TotalDebe = totalConIva,
            TotalHaber = totalConIva,
            FechaRegistro = DateTime.UtcNow,
            Lineas = new List<LineaPartidaContable>
            {
                new() { Id = Guid.NewGuid(), CodigoCuenta = cuentaActivo,
                        NombreCuenta = NombresCuenta[cuentaActivo],
                        Debe = totalConIva, Haber = 0m,
                        Glosa = $"Cobro venta {tipoComprobante}" },
                new() { Id = Guid.NewGuid(), CodigoCuenta = INGRESOS_VENTAS,
                        NombreCuenta = NombresCuenta[INGRESOS_VENTAS],
                        Debe = 0m, Haber = montoVenta,
                        Glosa = "Ingreso neto por venta" },
                new() { Id = Guid.NewGuid(), CodigoCuenta = IVA_DEBITO,
                        NombreCuenta = NombresCuenta[IVA_DEBITO],
                        Debe = 0m, Haber = iva,
                        Glosa = "IVA Débito Fiscal 13%" },
            }
        };

        _dbContext.PartidasContables.Add(partida);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Asiento venta {Numero} generado. Debe={Debe:C} == Haber={Haber:C}",
            numeroPartida, partida.TotalDebe, partida.TotalHaber);

        return partida;
    }

    // ── Asiento de Compra ────────────────────────────────────────────────────
    /// <summary>
    /// Genera el asiento contable para una Orden de Compra (PO) recibida.
    ///   DEBE: 1108 Inventario por el subtotal sin IVA
    ///   DEBE: 1110 IVA Crédito Fiscal por el IVA
    ///   HABER: 2101 CxP Proveedores por el total con IVA
    /// </summary>
    public async Task<PartidaContable> GenerarAsientoCompraAsync(
        Guid ordenCompraId, decimal montoCompra, decimal iva,
        CancellationToken cancellationToken = default)
    {
        decimal totalConIva = montoCompra + iva;
        string numeroPartida = await GenerarNumeroPartidaAsync("CMP", cancellationToken);

        var po = await _dbContext.OrdenesCompra
            .FirstOrDefaultAsync(p => p.Id == ordenCompraId, cancellationToken);
        string refDoc = po?.NumeroPO ?? ordenCompraId.ToString("N");

        var partida = new PartidaContable
        {
            Id = Guid.NewGuid(),
            NumeroPartida = numeroPartida,
            Fecha = DateTime.UtcNow,
            Concepto = $"Compra PO {refDoc} - Recepción de mercadería",
            TipoOperacion = "Compra",
            ReferenciaOrigenId = ordenCompraId,
            DocumentoReferencia = refDoc,
            TotalDebe = totalConIva,
            TotalHaber = totalConIva,
            FechaRegistro = DateTime.UtcNow,
            Lineas = new List<LineaPartidaContable>
            {
                new() { Id = Guid.NewGuid(), CodigoCuenta = INVENTARIO,
                        NombreCuenta = NombresCuenta[INVENTARIO],
                        Debe = montoCompra, Haber = 0m,
                        Glosa = "Ingreso de mercadería a bodega WMS" },
                new() { Id = Guid.NewGuid(), CodigoCuenta = IVA_CREDITO,
                        NombreCuenta = NombresCuenta[IVA_CREDITO],
                        Debe = iva, Haber = 0m,
                        Glosa = "IVA Crédito Fiscal 13%" },
                new() { Id = Guid.NewGuid(), CodigoCuenta = CXP_PROVEEDORES,
                        NombreCuenta = NombresCuenta[CXP_PROVEEDORES],
                        Debe = 0m, Haber = totalConIva,
                        Glosa = $"Obligación con proveedor PO {refDoc}" },
            }
        };

        _dbContext.PartidasContables.Add(partida);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Asiento compra {Numero} generado. Debe={Debe:C} == Haber={Haber:C}",
            numeroPartida, partida.TotalDebe, partida.TotalHaber);

        return partida;
    }

    // ── Libro Diario ─────────────────────────────────────────────────────────
    public async Task<IEnumerable<PartidaContable>> ObtenerLibroDiarioAsync(
        DateTime? desde = null, DateTime? hasta = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PartidasContables
            .Include(p => p.Lineas)
            .AsQueryable();

        if (desde.HasValue)
            query = query.Where(p => p.Fecha >= desde.Value);
        if (hasta.HasValue)
            query = query.Where(p => p.Fecha <= hasta.Value);

        return await query.OrderBy(p => p.Fecha)
                          .ThenBy(p => p.NumeroPartida)
                          .ToListAsync(cancellationToken);
    }

    // ── Libro Mayor ──────────────────────────────────────────────────────────
    public async Task<IEnumerable<LibroMayorCuenta>> ObtenerLibroMayorAsync(
        string? codigoCuenta = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.LineasPartidaContable.AsQueryable();

        if (!string.IsNullOrWhiteSpace(codigoCuenta))
            query = query.Where(l => l.CodigoCuenta == codigoCuenta);

        var lineas = await query.ToListAsync(cancellationToken);

        var agrupado = lineas
            .GroupBy(l => new { l.CodigoCuenta, l.NombreCuenta })
            .Select(g => new LibroMayorCuenta(
                g.Key.CodigoCuenta,
                g.Key.NombreCuenta,
                g.Sum(l => l.Debe),
                g.Sum(l => l.Haber),
                g.Sum(l => l.Debe) - g.Sum(l => l.Haber)))
            .OrderBy(c => c.CodigoCuenta)
            .ToList();

        return agrupado;
    }

    // ── Balance de Comprobación ───────────────────────────────────────────────
    public async Task<BalanceComprobacion> ObtenerBalanceComprobacionAsync(
        CancellationToken cancellationToken = default)
    {
        var cuentas = (await ObtenerLibroMayorAsync(null, cancellationToken)).ToList();
        decimal totalDebe   = cuentas.Sum(c => c.TotalDebe);
        decimal totalHaber  = cuentas.Sum(c => c.TotalHaber);
        bool estaCuadrado   = Math.Abs(totalDebe - totalHaber) < 0.01m;

        _logger.LogInformation("Balance de comprobación: Debe={Debe:C}, Haber={Haber:C}, Cuadrado={OK}",
            totalDebe, totalHaber, estaCuadrado);

        return new BalanceComprobacion(totalDebe, totalHaber, estaCuadrado, cuentas);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task<string> GenerarNumeroPartidaAsync(string prefijo, CancellationToken ct)
    {
        int total = await _dbContext.PartidasContables.CountAsync(ct);
        return $"{prefijo}-{DateTime.UtcNow:yyyy}-{(total + 1):D6}";
    }
}
