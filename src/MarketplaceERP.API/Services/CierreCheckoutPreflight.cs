using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Auditoría PREVIA, exclusivamente consultiva, de un cierre administrativo de checkout.
/// NO aprueba pedidos, NO sustituye una transacción atómica, NO autoriza pagos/reembolsos
/// y NO debe usarse como autorización para cambiar estados en otro contexto.
/// Este prototipo aislado NO está registrado en DI ni expuesto por HTTP.
/// </summary>
public sealed record CierreCheckoutPreflightResult(
    Guid OrdenId, bool DatosCoherentes, IReadOnlyList<string> Bloqueos);

public sealed class CierreCheckoutPreflight
{
    private readonly FinanceDbContext _finance;
    private readonly CatalogDbContext _catalog;

    public CierreCheckoutPreflight(FinanceDbContext finance, CatalogDbContext catalog)
    {
        _finance = finance;
        _catalog = catalog;
    }

    public async Task<CierreCheckoutPreflightResult> EvaluarAsync(
        Guid ordenId, CancellationToken cancellationToken = default)
    {
        var bloqueos = new List<string>();
        if (ordenId == Guid.Empty)
            return new(ordenId, false, new[] { "ORDEN_ID_INVALIDO" });

        var orden = await _finance.Ordenes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == ordenId, cancellationToken);
        if (orden is null)
            return new(ordenId, false, new[] { "ORDEN_NO_EXISTE" });

        if (orden.Estado != EstadoOrden.Pendiente)
            bloqueos.Add("ORDEN_NO_PENDIENTE");
        if (orden.CompradorId == Guid.Empty || orden.VendedorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(orden.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(orden.CheckoutPayloadHash) ||
            orden.MontoTotal <= 0 || orden.Moneda != "USD")
            bloqueos.Add("IDENTIDAD_CHECKOUT_INVALIDA");
        if (orden.FechaEnvio.HasValue || orden.FechaEntregaReal.HasValue ||
            orden.FechaCancelacion.HasValue || !string.IsNullOrWhiteSpace(orden.GuiaEnvio))
            bloqueos.Add("LOGISTICA_O_CANCELACION_REGISTRADA");

        // Las órdenes comerciales V2 identifican explícitamente CHK2- y
        // mantienen un mapa 1 intento -> N reservas. No inferir V2 de
        // un mapa incompleto ni convertir las órdenes QA en comerciales.
        var esCheckoutMultiSku = !string.IsNullOrWhiteSpace(orden.NumeroOrden) &&
            orden.NumeroOrden.StartsWith("CHK2-", StringComparison.Ordinal) &&
            orden.NumeroOrden.EndsWith("-" + ordenId.ToString("N"), StringComparison.Ordinal);
        var items = await _finance.ItemsOrden.AsNoTracking()
            .Where(i => i.OrdenId == ordenId).ToListAsync(cancellationToken);
        var cantidadItemsValida = esCheckoutMultiSku
            ? items.Count is >= 2 and <= 20
            : items.Count == 1;
        if (!cantidadItemsValida ||
            items.Any(i => i.Cantidad <= 0 || i.ProductoId == Guid.Empty ||
                           i.PrecioUnitario <= 0) ||
            items.Select(i => i.ProductoId).Distinct().Count() != items.Count ||
            items.Sum(i => i.PrecioUnitario * i.Cantidad) != orden.SubTotal ||
            orden.SubTotal != orden.MontoTotal)
            bloqueos.Add("ITEMS_O_PRECIO_NO_COINCIDEN");

        var intentos = await _finance.IntentosPago.AsNoTracking()
            .Where(i => i.OrdenId == ordenId).ToListAsync(cancellationToken);
        var intento = intentos.Count == 1 ? intentos[0] : null;
        if (intento is null || intento.Estado != EstadoIntentoPago.Completado ||
            string.IsNullOrWhiteSpace(intento.TransaccionId) ||
            !string.Equals(intento.ReferenciaOperacion, $"PAY-{ordenId:N}", StringComparison.Ordinal) ||
            intento.Monto != orden.MontoTotal || intento.Moneda != orden.Moneda ||
            (esCheckoutMultiSku
                ? intento.ReservaId.HasValue
                : !intento.ReservaId.HasValue || intento.ReservaId.Value == Guid.Empty))
            bloqueos.Add("INTENTO_NO_CONCILIADO");

        var splits = await _finance.SplitsPago.AsNoTracking()
            .Where(s => s.OrdenId == ordenId).ToListAsync(cancellationToken);
        var split = splits.Count == 1 ? splits[0] : null;
        if (split is null || intento is null ||
            string.IsNullOrWhiteSpace(split.TransaccionId) ||
            split.TransaccionId != intento.TransaccionId ||
            split.MontoTotal != orden.MontoTotal ||
            split.MontoVendedor < 0 || split.MontoMarketplace < 0 ||
            split.MontoVendedor + split.MontoMarketplace != orden.MontoTotal)
            bloqueos.Add("SPLIT_NO_CONCILIADO");

        // Una orden ya aprobada debe tener exactamente un asiento de venta
        // conciliado con el split. Una orden pendiente puede estar en recuperación:
        // CierreCheckoutAdminQaService generará el asiento dentro de su transacción.
        if (orden.Estado == EstadoOrden.Aprobada)
        {
            var asientosVenta = await _finance.PartidasContables.AsNoTracking()
                .Include(p => p.Lineas)
                .Where(p => p.TipoOperacion == "Venta" && p.ReferenciaOrigenId == ordenId)
                .ToListAsync(cancellationToken);
            var asiento = asientosVenta.Count == 1 ? asientosVenta[0] : null;
            var cuentaVendedor = asiento?.Lineas.SingleOrDefault(l => l.CodigoCuenta == "2108");
            var cuentaMarketplace = asiento?.Lineas.SingleOrDefault(l => l.CodigoCuenta == "4101");
            var cuentaIva = asiento?.Lineas.SingleOrDefault(l => l.CodigoCuenta == "2105");
            if (asiento is null || split is null ||
                asiento.TotalDebe != orden.MontoTotal ||
                asiento.TotalHaber != orden.MontoTotal ||
                asiento.Lineas.Sum(l => l.Debe) != orden.MontoTotal ||
                asiento.Lineas.Sum(l => l.Haber) != orden.MontoTotal ||
                cuentaVendedor?.Haber != split.MontoVendedor ||
                cuentaMarketplace?.Haber != split.MontoMarketplace + orden.CostoEnvio ||
                cuentaIva?.Haber != orden.ImpuestoIva)
                bloqueos.Add("ASIENTO_VENTA_NO_CONCILIADO");
        }

        if (await _finance.ReembolsosPago.AsNoTracking()
            .AnyAsync(r => r.OrdenId == ordenId, cancellationToken))
            bloqueos.Add("REEMBOLSO_EXISTENTE");
        if (split is not null && await _finance.Liquidaciones.AsNoTracking()
            .AnyAsync(l => l.SplitPagoId == split.Id, cancellationToken))
            bloqueos.Add("LIQUIDACION_EXISTENTE");

        var reservas = await _catalog.ReservasStock.AsNoTracking()
            .Include(r => r.Detalles)
            .Where(r => r.OrdenId == ordenId).ToListAsync(cancellationToken);

        if (esCheckoutMultiSku)
        {
            var links = intento is null ? new List<IntentoReservaPago>() :
                await _finance.IntentoReservasPago.AsNoTracking()
                    .Where(l => l.IntentoPagoId == intento.Id)
                    .ToListAsync(cancellationToken);

            // Rechazar faltantes, duplicados, enlaces ajenos o cantidades
            // incompatibles. Este preflight es de SOLO LECTURA y nunca
            // interpreta un pago incierto como pago conciliado.
            var mapaCoherente = intento is not null &&
                items.Count is >= 2 and <= 20 &&
                reservas.Count == items.Count && links.Count == items.Count &&
                items.Select(i => i.ProductoId).Distinct().Count() == items.Count &&
                links.Select(l => l.ReservaId).Distinct().Count() == links.Count &&
                links.Select(l => l.ProductoId).Distinct().Count() == links.Count &&
                links.All(l => l.Procedencia == "CheckoutMultiSkuV2" &&
                    l.Cantidad > 0 &&
                    items.Any(i => i.ProductoId == l.ProductoId &&
                                   i.Cantidad == l.Cantidad) &&
                    reservas.Any(r => r.Id == l.ReservaId &&
                                      r.ProductoId == l.ProductoId &&
                                      r.CantidadOriginal == l.Cantidad));
            if (!mapaCoherente)
                bloqueos.Add("MAPA_RESERVAS_MULTISKU_INCONSISTENTE");
        }
        else if (reservas.Count != 1 || intento is null ||
                 intento.ReservaId != reservas[0].Id)
        {
            bloqueos.Add("RESERVA_NO_CONCILIADA");
        }

        if (reservas.Count == 0 ||
            reservas.Any(r => r.Estado != EstadoReserva.Confirmada ||
                r.CantidadOriginal <= 0 || r.CantidadLiberada != 0 ||
                r.CantidadConsumida != 0 || r.Detalles.Count == 0 ||
                r.Detalles.Sum(d => d.CantidadAsignada) != r.CantidadOriginal ||
                r.Detalles.Any(d => d.CantidadAsignada <= 0 ||
                                    d.CantidadLiberada != 0 || d.CantidadConsumida != 0) ||
                !items.Any(i => i.ProductoId == r.ProductoId &&
                                i.Cantidad == r.CantidadOriginal)))
            bloqueos.Add("RESERVA_NO_CONCILIADA");

        foreach (var reserva in reservas)
        {
            var requerimientos = reserva.Detalles
                .GroupBy(d => d.StockFisicoId)
                .ToDictionary(g => g.Key,
                    g => g.Sum(d => d.CantidadAsignada - d.CantidadLiberada - d.CantidadConsumida));
            if (requerimientos.Count == 0)
                continue;

            var saldos = await _catalog.StockFisico.AsNoTracking()
                .Where(s => requerimientos.Keys.Contains(s.Id))
                .ToListAsync(cancellationToken);
            if (saldos.Count != requerimientos.Count ||
                saldos.Any(s => s.ProductoId != reserva.ProductoId ||
                    s.CantidadReservada < requerimientos[s.Id]))
            {
                if (!bloqueos.Contains("SALDOS_WMS_INCONSISTENTES"))
                    bloqueos.Add("SALDOS_WMS_INCONSISTENTES");
            }
        }

        var productosEnOrden = items.Select(i => i.ProductoId).Distinct().ToArray();
        if (productosEnOrden.Length > 0 &&
            await _catalog.Productos.AsNoTracking()
                .CountAsync(p => productosEnOrden.Contains(p.Id) &&
                    p.VendedorId == orden.VendedorId, cancellationToken) != productosEnOrden.Length)
            bloqueos.Add("VENDEDOR_PRODUCTO_NO_COINCIDEN");

        if (await _catalog.PickLists.AsNoTracking()
                .AnyAsync(p => p.OrdenId == ordenId, cancellationToken) ||
            await _catalog.PaquetesDespacho.AsNoTracking()
                .AnyAsync(p => p.OrdenId == ordenId, cancellationToken) ||
            await _catalog.DevolucionesRMA.AsNoTracking()
                .AnyAsync(r => r.OrdenId == ordenId, cancellationToken))
            bloqueos.Add("WMS_O_RMA_YA_INICIADO");

        return new(ordenId, bloqueos.Count == 0, bloqueos);
    }
}
