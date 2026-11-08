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

    // ── FIN-01: Asiento manual ──────────────────────────────────────────────
    /// <summary>
    /// Registra las cuentas y los importes proporcionados, sin reutilizar el
    /// generador de ventas ni fabricar movimientos de Caja/IVA.
    /// El endpoint permanece protegido por PaymentOperationsGuard.
    /// </summary>
    public async Task<PartidaContable> GenerarAsientoManualAsync(
        string concepto,
        string tipoOperacion,
        IEnumerable<LineaAsientoInput> lineas,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(concepto) || concepto.Trim().Length > 500)
            throw new ArgumentException("El concepto es obligatorio y admite hasta 500 caracteres.");

        var tipo = tipoOperacion?.Trim();
        if (string.IsNullOrWhiteSpace(tipo) ||
            !new[] { "Ajuste", "Correccion", "Reversion", "Cierre" }
                .Contains(tipo, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("El tipo de asiento manual no es válido.");

        var detalle = lineas?.ToList() ?? new List<LineaAsientoInput>();
        if (detalle.Count < 2 || detalle.Count > 100)
            throw new ArgumentException("El asiento debe contener entre 2 y 100 líneas.");

        decimal totalDebe = 0m;
        decimal totalHaber = 0m;
        var entidades = new List<LineaPartidaContable>(detalle.Count);

        for (var i = 0; i < detalle.Count; i++)
        {
            var item = detalle[i];
            if (item is null)
                throw new ArgumentException($"La línea {i + 1} no puede ser nula.");
            var codigo = item.CodigoCuenta?.Trim();
            if (string.IsNullOrWhiteSpace(codigo) ||
                !NombresCuenta.TryGetValue(codigo, out var nombre))
                throw new ArgumentException($"Cuenta contable no reconocida en línea {i + 1}.");

            if (item.Debe < 0m || item.Haber < 0m ||
                (item.Debe > 0m && item.Haber > 0m) ||
                (item.Debe == 0m && item.Haber == 0m))
                throw new ArgumentException($"Montos Debe/Haber no válidos en línea {i + 1}.");

            if (decimal.Round(item.Debe, 2) != item.Debe ||
                decimal.Round(item.Haber, 2) != item.Haber)
                throw new ArgumentException($"Solo se admiten dos decimales en línea {i + 1}.");

            const decimal maximo = 9999999999999999.99m; // precision PostgreSQL numeric(18,2)
            if (item.Debe > maximo || item.Haber > maximo)
                throw new ArgumentException($"El monto de línea {i + 1} supera la precisión contable.");

            var glosa = string.IsNullOrWhiteSpace(item.Glosa) ? concepto.Trim() : item.Glosa.Trim();
            if (glosa.Length > 300)
                throw new ArgumentException($"La glosa de línea {i + 1} supera 300 caracteres.");

            totalDebe = checked(totalDebe + item.Debe);
            totalHaber = checked(totalHaber + item.Haber);
            entidades.Add(new LineaPartidaContable
            {
                Id = Guid.NewGuid(),
                CodigoCuenta = codigo,
                NombreCuenta = nombre,
                Debe = item.Debe,
                Haber = item.Haber,
                Glosa = glosa
            });
        }

        // Los totales también deben caber en numeric(18,2).
        if (totalDebe > 9999999999999999.99m || totalHaber > 9999999999999999.99m)
            throw new ArgumentException("El total del asiento supera la precisión contable.");

        // No permitir un descuadre de un centavo por redondeo/tolerancia.
        if (totalDebe == 0m || totalDebe != totalHaber)
            throw new ArgumentException("El asiento debe tener montos positivos e iguales en Debe y Haber.");

        var now = DateTime.UtcNow;
        // Identificador único sin usar Count()+1, que colisiona entre peticiones.
        // Un correlativo fiscal exigiría una secuencia PostgreSQL y revisión adicional.
        var partida = new PartidaContable
        {
            Id = Guid.NewGuid(),
            NumeroPartida = $"AST-{now:yyyy}-{Guid.NewGuid():N}",
            Fecha = now,
            Concepto = concepto.Trim(),
            TipoOperacion = tipo!,
            ReferenciaOrigenId = null,
            DocumentoReferencia = string.Empty,
            TotalDebe = totalDebe,
            TotalHaber = totalHaber,
            FechaRegistro = now,
            Lineas = entidades
        };

        _dbContext.PartidasContables.Add(partida);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Asiento manual {Numero} registrado con {Lineas} líneas.",
            partida.NumeroPartida, entidades.Count);
        return partida;
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

        // Proteccion para reintentos secuenciales. La concurrencia verdadera requiere
        // adicionalmente un indice parcial UNIQUE (TipoOperacion=Venta, ReferenciaOrigenId)
        // sobre una migracion validada contra datos historicos.
        var existente = await _dbContext.PartidasContables
            .Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.TipoOperacion == "Venta" &&
                                      p.ReferenciaOrigenId == ordenId, cancellationToken);
        if (existente is not null)
        {
            var ingreso = existente.Lineas
                .Where(l => l.CodigoCuenta == INGRESOS_VENTAS).Sum(l => l.Haber);
            var ivaRegistrado = existente.Lineas
                .Where(l => l.CodigoCuenta == IVA_DEBITO).Sum(l => l.Haber);
            if (existente.TotalDebe != totalConIva ||
                existente.TotalHaber != totalConIva ||
                ingreso != montoVenta || ivaRegistrado != iva)
                throw new InvalidOperationException(
                    "La orden ya tiene un asiento Venta con importes diferentes.");

            return existente;
        }

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
    private static Task<string> GenerarNumeroPartidaAsync(string prefijo, CancellationToken ct)
    {
        // Count()+1 reutilizaba números tras bajas y en escrituras simultáneas.
        // La referencia conserva el prefijo contable y el año, pero no depende
        // de consultas ni de un contador compartido con otros tipos de asiento.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult($"{prefijo}-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}");
    }
}
