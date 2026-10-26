/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    public const string CAJA_BANCOS       = "1101"; // Efectivo y Equivalentes
    public const string CXC_CLIENTES      = "1105"; // Cuentas por Cobrar Comerciales
    public const string INVENTARIO        = "1108"; // Inventarios de Mercaderías
    public const string IVA_CREDITO       = "1110"; // IVA Crédito Fiscal (13% compras)
    public const string CXP_PROVEEDORES   = "2101"; // Cuentas por Pagar Comerciales
    public const string IVA_DEBITO        = "2105"; // IVA Débito Fiscal (13% ventas)
    public const string PASIVO_VENDEDORES = "2108"; // Pasivos por Liquidar a Vendedores Marketplace
    public const string INGRESOS_VENTAS   = "4101"; // Ingresos por Ventas / Comisiones Marketplace
    public const string COSTO_VENTAS      = "5101"; // Costo de Ventas

    public static readonly IReadOnlyDictionary<string, string> NombresCuenta = new Dictionary<string, string>
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

    // ── Asiento Manual Centralizado ──────────────────────────────────────────
    /// <summary>
    /// Único punto de persistencia para toda PartidaContable del sistema.
    /// Valida: mínimo 2 líneas, Debe >= 0, Haber >= 0, no ambos > 0 en la misma línea,
    /// cuentas válidas en el catálogo, y sum(Debe) == sum(Haber) con tolerancia de 0.01.
    /// Aplica número de partida desde secuencia PostgreSQL (o fallback seguro) e idempotencia.
    /// </summary>
    public async Task<PartidaContable> GenerarAsientoManualAsync(
        string concepto,
        string tipoOperacion,
        IEnumerable<LineaAsientoInput> lineasInput,
        Guid? referenciaOrigenId = null,
        string? documentoReferencia = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(concepto))
            throw new ArgumentException("El concepto de la partida es obligatorio.", nameof(concepto));

        if (string.IsNullOrWhiteSpace(tipoOperacion))
            throw new ArgumentException("El tipo de operación es obligatorio.", nameof(tipoOperacion));

        // 1. Idempotencia: Si ya existe una partida para este tipo y referencia, se retorna la existente sin duplicar
        if (referenciaOrigenId.HasValue && referenciaOrigenId.Value != Guid.Empty)
        {
            var existente = await _dbContext.PartidasContables
                .Include(p => p.Lineas)
                .FirstOrDefaultAsync(p => p.TipoOperacion == tipoOperacion && p.ReferenciaOrigenId == referenciaOrigenId.Value, cancellationToken);

            if (existente != null)
            {
                _logger.LogInformation(
                    "Partida para TipoOperacion={Tipo} y ReferenciaOrigenId={RefId} ya existe ({NumeroPartida}). Idempotencia aplicada.",
                    tipoOperacion, referenciaOrigenId, existente.NumeroPartida);
                return existente;
            }
        }

        var listaLineas = lineasInput?.ToList() ?? new List<LineaAsientoInput>();

        // 2. Mínimo 2 líneas
        if (listaLineas.Count < 2)
            throw new ArgumentException("Un asiento contable debe contener un mínimo de 2 líneas para cumplir con la partida doble.");

        decimal sumDebe = 0m;
        decimal sumHaber = 0m;
        var lineasEntidad = new List<LineaPartidaContable>();

        for (int i = 0; i < listaLineas.Count; i++)
        {
            var linea = listaLineas[i];

            if (string.IsNullOrWhiteSpace(linea.CodigoCuenta))
                throw new ArgumentException($"La línea {i + 1} no especifica un código de cuenta.");

            string codigo = linea.CodigoCuenta.Trim();
            if (!NombresCuenta.TryGetValue(codigo, out var nombreCuenta))
                throw new ArgumentException($"La cuenta '{codigo}' en la línea {i + 1} no existe o no está activa en el catálogo de cuentas.");

            if (linea.Debe < 0m || linea.Haber < 0m)
                throw new ArgumentException($"En la línea {i + 1} ({codigo}), los valores de Debe y Haber no pueden ser negativos.");

            if (linea.Debe > 0m && linea.Haber > 0m)
                throw new ArgumentException($"En la línea {i + 1} ({codigo}), no se permite registrar montos positivos en Debe y Haber simultáneamente.");

            if (linea.Debe == 0m && linea.Haber == 0m)
                throw new ArgumentException($"En la línea {i + 1} ({codigo}), el monto no puede ser cero en ambos lados.");

            sumDebe += linea.Debe;
            sumHaber += linea.Haber;

            lineasEntidad.Add(new LineaPartidaContable
            {
                Id = Guid.NewGuid(),
                CodigoCuenta = codigo,
                NombreCuenta = nombreCuenta,
                Debe = linea.Debe,
                Haber = linea.Haber,
                Glosa = linea.Glosa ?? concepto
            });
        }

        // 3. Cuadre estricto de partida doble con tolerancia de 0.01m
        if (Math.Abs(sumDebe - sumHaber) > 0.01m)
        {
            throw new InvalidOperationException(
                $"El asiento no cuadra. Total Debe: {sumDebe:F2}, Total Haber: {sumHaber:F2}. Diferencia: {Math.Abs(sumDebe - sumHaber):F2}.");
        }

        // 4. Generación de número de partida correlativo mediante secuencia PostgreSQL
        string prefijo = tipoOperacion.ToUpperInvariant() switch
        {
            "VENTA" => "VTA",
            "COMPRA" => "CMP",
            "LIQUIDACION" => "LIQ",
            _ => "AST"
        };
        string numeroPartida = await GenerarNumeroPartidaAsync(prefijo, cancellationToken);

        var partida = new PartidaContable
        {
            Id = Guid.NewGuid(),
            NumeroPartida = numeroPartida,
            Fecha = DateTime.UtcNow,
            Concepto = concepto,
            TipoOperacion = tipoOperacion,
            ReferenciaOrigenId = referenciaOrigenId,
            DocumentoReferencia = documentoReferencia ?? referenciaOrigenId?.ToString("N") ?? string.Empty,
            TotalDebe = sumDebe,
            TotalHaber = sumHaber,
            FechaRegistro = DateTime.UtcNow,
            Lineas = lineasEntidad
        };

        _dbContext.PartidasContables.Add(partida);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (referenciaOrigenId.HasValue &&
                                          (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505" ||
                                           ex.InnerException?.Message.Contains("IX_partidas_contables_TipoOperacion_ReferenciaOrigenId") == true ||
                                           ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true ||
                                           ex.Message.Contains("IX_partidas_contables_TipoOperacion_ReferenciaOrigenId") ||
                                           ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)))
        {
            // IDEMPOTENCIA Y CONCURRENCIA:
            // Si GenerarAsientoManualAsync se ejecuta dentro de una transacción abierta (ej. BeginTransactionAsync en ProcesarPagoAsync),
            // PostgreSQL aborta la transacción ante un error 23505 (Current transaction is aborted, commands ignored until end of transaction block).
            // En ese escenario, no es posible ejecutar consultas de recuperación (FirstAsync fallará); se debe dejar subir la excepción
            // para que la transacción haga Rollback y la capa superior responda con 409 Conflict.
            // La recuperación y relectura de la partida ganadora solo es aplicable cuando NO existe una transacción externa activa.
            if (_dbContext.Database.CurrentTransaction != null)
            {
                _logger.LogWarning(
                    "Conflicto de clave única (23505) en partida {TipoOperacion}/{ReferenciaOrigenId} dentro de una transacción activa. Propagando para Rollback / 409 Conflict.",
                    tipoOperacion, referenciaOrigenId);
                throw;
            }

            _logger.LogWarning("Conflicto de concurrencia (Postgres 23505) al crear partida {TipoOperacion}/{ReferenciaOrigenId}. Recuperando existente...",
                tipoOperacion, referenciaOrigenId);

            _dbContext.Entry(partida).State = EntityState.Detached;
            foreach (var l in partida.Lineas)
            {
                _dbContext.Entry(l).State = EntityState.Detached;
            }

            var existente = await _dbContext.PartidasContables
                .Include(p => p.Lineas)
                .FirstOrDefaultAsync(p => p.TipoOperacion == tipoOperacion && p.ReferenciaOrigenId == referenciaOrigenId.Value, cancellationToken);

            if (existente != null)
                return existente;

            throw;
        }

        _logger.LogInformation("Asiento {Numero} ({Tipo}) registrado exitosamente. Debe={Debe:C} == Haber={Haber:C}",
            partida.NumeroPartida, partida.TipoOperacion, partida.TotalDebe, partida.TotalHaber);

        return partida;
    }

    // ── Asiento de Venta ─────────────────────────────────────────────────────
    public async Task<PartidaContable> GenerarAsientoVentaAsync(
        Guid ordenId, decimal montoVenta, decimal iva,
        string tipoComprobante, CancellationToken cancellationToken = default)
    {
        // 1. Cargar la orden con sus splits si existen en la BD
        var orden = await _dbContext.Ordenes
            .Include(o => o.SplitPago)
            .FirstOrDefaultAsync(o => o.Id == ordenId, cancellationToken);

        string cuentaActivo = CAJA_BANCOS;
        if (!string.IsNullOrWhiteSpace(tipoComprobante) &&
            (tipoComprobante.Contains("Crédito", StringComparison.OrdinalIgnoreCase) ||
             tipoComprobante.Contains("Credito", StringComparison.OrdinalIgnoreCase) ||
             tipoComprobante.Contains("CCF", StringComparison.OrdinalIgnoreCase)))
        {
            cuentaActivo = CXC_CLIENTES;
        }

        var lineas = new List<LineaAsientoInput>();

        // Si la orden y su split existen, aplicar el desglose contable correcto de marketplace
        if (orden?.SplitPago != null)
        {
            var split = orden.SplitPago;

            // ── Debe: Total cobrado al cliente (incluye SubTotal + IVA + CostoEnvio) ──
            decimal totalCobrado = orden.MontoTotal;

            // ── Haber 2108: Obligación con vendedor ──
            decimal pasivoVendedor = split.MontoVendedor;

            // ── Haber 2105: IVA Débito Fiscal completo de la venta (sin recalcular ni dividir entre 1.13) ──
            decimal ivaDebito = orden.ImpuestoIva;

            // ── Haber 4101: Residuo exacto = MontoTotal − 2108 − 2105 ──
            // Incluye la comisión bruta del marketplace y el CostoEnvio.
            // DECISIÓN-PENDIENTE / validar con contador:
            //   (a) Si la comisión genera IVA Débito adicional y cómo se factura al vendedor.
            //   (b) El CostoEnvio queda hoy dentro de 4101 (Ingresos por Ventas / Comisiones Marketplace).
            decimal ingresosMarketplace = totalCobrado - pasivoVendedor - ivaDebito;

            if (ingresosMarketplace < 0m)
            {
                throw new InvalidOperationException(
                    $"El residuo para 4101 es negativo ({ingresosMarketplace:F2}). " +
                    $"MontoTotal={totalCobrado:F2}, MontoVendedor(2108)={pasivoVendedor:F2}, IVA(2105)={ivaDebito:F2}. " +
                    $"Revisar la configuración del split o el cálculo del IVA.");
            }

            lineas.Add(new LineaAsientoInput(cuentaActivo, totalCobrado, 0m, $"Cobro orden {orden.NumeroOrden}"));
            lineas.Add(new LineaAsientoInput(PASIVO_VENDEDORES, 0m, pasivoVendedor, $"Obligación liquidación vendedor orden {orden.NumeroOrden}"));
            lineas.Add(new LineaAsientoInput(INGRESOS_VENTAS, 0m, ingresosMarketplace, "Ingreso por comisión e intermediación marketplace"));
            lineas.Add(new LineaAsientoInput(IVA_DEBITO, 0m, ivaDebito, "IVA Débito Fiscal 13%"));
        }
        else
        {
            // DECISIÓN-PENDIENTE: Orden sin SplitPago asociado (ej. creada vía OrdenesController sin Checkout Saga).
            // No se acredita la cuenta 2108 (Pasivo Vendedores) con $0 en silencio.
            _logger.LogWarning(
                "DECISIÓN-PENDIENTE: Orden {OrdenId} aprobada sin SplitPago asociado. No se acredita cuenta 2108.",
                ordenId);

            decimal totalCobrado = orden?.MontoTotal ?? (montoVenta + iva);
            decimal ivaOrden = orden?.ImpuestoIva ?? iva;
            decimal ingresoVenta = totalCobrado - ivaOrden; // Incluye costo de envío dentro de 4101 si existe

            lineas.Add(new LineaAsientoInput(cuentaActivo, totalCobrado, 0m, $"Cobro venta {tipoComprobante}"));
            lineas.Add(new LineaAsientoInput(INGRESOS_VENTAS, 0m, ingresoVenta, "Ingreso por venta"));
            lineas.Add(new LineaAsientoInput(IVA_DEBITO, 0m, ivaOrden, "IVA Débito Fiscal 13%"));
        }

        string docRef = orden?.NumeroOrden ?? ordenId.ToString("N");
        string concepto = $"Venta OMS Orden {docRef} - {tipoComprobante}";

        return await GenerarAsientoManualAsync(
            concepto,
            "Venta",
            lineas,
            referenciaOrigenId: ordenId,
            documentoReferencia: docRef,
            cancellationToken: cancellationToken);
    }

    // ── Asiento de Compra ────────────────────────────────────────────────────
    public async Task<PartidaContable> GenerarAsientoCompraAsync(
        Guid ordenCompraId, decimal montoCompra, decimal iva,
        CancellationToken cancellationToken = default)
    {
        decimal totalConIva = montoCompra + iva;
        var po = await _dbContext.OrdenesCompra
            .FirstOrDefaultAsync(p => p.Id == ordenCompraId, cancellationToken);
        string refDoc = po?.NumeroPO ?? ordenCompraId.ToString("N");

        var lineas = new List<LineaAsientoInput>
        {
            new(INVENTARIO, montoCompra, 0m, "Ingreso de mercadería a bodega WMS"),
            new(IVA_CREDITO, iva, 0m, "IVA Crédito Fiscal 13%"),
            new(CXP_PROVEEDORES, 0m, totalConIva, $"Obligación con proveedor PO {refDoc}")
        };

        return await GenerarAsientoManualAsync(
            $"Compra PO {refDoc} - Recepción de mercadería",
            "Compra",
            lineas,
            referenciaOrigenId: ordenCompraId,
            documentoReferencia: refDoc,
            cancellationToken: cancellationToken);
    }

    // ── Libro Diario (Paginado) ──────────────────────────────────────────────
    public async Task<LibroDiarioPaginado> ObtenerLibroDiarioPaginadoAsync(
        DateTime? desde = null, DateTime? hasta = null,
        int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var query = _dbContext.PartidasContables.AsQueryable();

        if (desde.HasValue)
            query = query.Where(p => p.Fecha >= desde.Value);
        if (hasta.HasValue)
            query = query.Where(p => p.Fecha <= hasta.Value);

        int total = await query.CountAsync(cancellationToken);

        var partidas = await query
            .Include(p => p.Lineas)
            .OrderByDescending(p => p.Fecha)
            .ThenByDescending(p => p.NumeroPartida)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new LibroDiarioPaginado(total, page, pageSize, partidas);
    }

    public async Task<IEnumerable<PartidaContable>> ObtenerLibroDiarioAsync(
        DateTime? desde = null, DateTime? hasta = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ObtenerLibroDiarioPaginadoAsync(desde, hasta, 1, 200, cancellationToken);
        return result.Partidas;
    }

    // ── Libro Mayor (SQL GROUP BY) ───────────────────────────────────────────
    public async Task<IEnumerable<LibroMayorCuenta>> ObtenerLibroMayorAsync(
        string? codigoCuenta = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.LineasPartidaContable.AsQueryable();

        if (!string.IsNullOrWhiteSpace(codigoCuenta))
        {
            string cod = codigoCuenta.Trim();
            query = query.Where(l => l.CodigoCuenta == cod);
        }

        // Agrupación ejecutada directamente en SQL sin cargar todas las líneas en memoria
        var agrupado = await query
            .GroupBy(l => new { l.CodigoCuenta, l.NombreCuenta })
            .Select(g => new
            {
                g.Key.CodigoCuenta,
                g.Key.NombreCuenta,
                TotalDebe = g.Sum(l => l.Debe),
                TotalHaber = g.Sum(l => l.Haber)
            })
            .OrderBy(c => c.CodigoCuenta)
            .ToListAsync(cancellationToken);

        return agrupado.Select(a => new LibroMayorCuenta(
            a.CodigoCuenta,
            a.NombreCuenta,
            a.TotalDebe,
            a.TotalHaber,
            a.TotalDebe - a.TotalHaber)).ToList();
    }

    // ── Balance de Comprobación ───────────────────────────────────────────────
    public async Task<BalanceComprobacion> ObtenerBalanceComprobacionAsync(
        CancellationToken cancellationToken = default)
    {
        var cuentas = (await ObtenerLibroMayorAsync(null, cancellationToken)).ToList();
        decimal totalDebe  = cuentas.Sum(c => c.TotalDebe);
        decimal totalHaber = cuentas.Sum(c => c.TotalHaber);
        bool estaCuadrado  = Math.Abs(totalDebe - totalHaber) < 0.01m;

        _logger.LogInformation("Balance de comprobación: Debe={Debe:C}, Haber={Haber:C}, Cuadrado={OK}",
            totalDebe, totalHaber, estaCuadrado);

        return new BalanceComprobacion(totalDebe, totalHaber, estaCuadrado, cuentas);
    }

    // ── Generador de Correlativo mediante Secuencia PostgreSQL ────────────────
    private async Task<string> GenerarNumeroPartidaAsync(string prefijo, CancellationToken ct)
    {
        long correlativo;
        if (_dbContext.Database.IsRelational())
        {
            try
            {
                await using var cmd = _dbContext.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = "SELECT nextval('finance.\"PartidaContableNumeroSeq\"');";
                if (_dbContext.Database.CurrentTransaction != null)
                {
                    cmd.Transaction = _dbContext.Database.CurrentTransaction.GetDbTransaction();
                }

                if (cmd.Connection!.State != ConnectionState.Open)
                {
                    await cmd.Connection.OpenAsync(ct);
                }

                var scalar = await cmd.ExecuteScalarAsync(ct);
                correlativo = Convert.ToInt64(scalar);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallo al consultar secuencia PostgreSQL. Empleando fallback...");
                int total = await _dbContext.PartidasContables.CountAsync(ct);
                correlativo = total + 1;
            }
        }
        else
        {
            int total = await _dbContext.PartidasContables.CountAsync(ct);
            correlativo = total + 1;
        }

        return $"{prefijo}-{DateTime.UtcNow:yyyy}-{correlativo:D6}";
    }
}
