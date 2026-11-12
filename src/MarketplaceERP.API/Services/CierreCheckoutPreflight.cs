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

        var items = await _finance.ItemsOrden.AsNoTracking()
            .Where(i => i.OrdenId == ordenId).ToListAsync(cancellationToken);
        if (items.Count != 1 || items[0].Cantidad <= 0 ||
            items[0].ProductoId == Guid.Empty || items[0].PrecioUnitario <= 0 ||
            items[0].PrecioUnitario * items[0].Cantidad != orden.SubTotal ||
            orden.SubTotal != orden.MontoTotal)
            bloqueos.Add("ITEMS_O_PRECIO_NO_COINCIDEN");

        var intentos = await _finance.IntentosPago.AsNoTracking()
            .Where(i => i.OrdenId == ordenId).ToListAsync(cancellationToken);
        var intento = intentos.Count == 1 ? intentos[0] : null;
        if (intento is null || intento.Estado != EstadoIntentoPago.Completado ||
            string.IsNullOrWhiteSpace(intento.TransaccionId) ||
            !string.Equals(intento.ReferenciaOperacion, $"PAY-{ordenId:N}", StringComparison.Ordinal) ||
            intento.Monto != orden.MontoTotal || intento.Moneda != orden.Moneda ||
            !intento.ReservaId.HasValue || intento.ReservaId.Value == Guid.Empty)
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
        var reserva = reservas.Count == 1 ? reservas[0] : null;
        if (reserva is null || intento is null || items.Count != 1 ||
            intento.ReservaId != reserva.Id ||
            reserva.Estado != EstadoReserva.Confirmada ||
            reserva.ProductoId != items[0].ProductoId ||
            reserva.CantidadOriginal != items[0].Cantidad ||
            reserva.CantidadLiberada != 0 || reserva.CantidadConsumida != 0 ||
            reserva.Detalles.Count == 0 ||
            reserva.Detalles.Sum(d => d.CantidadAsignada) != reserva.CantidadOriginal ||
            reserva.Detalles.Any(d => d.CantidadLiberada != 0 || d.CantidadConsumida != 0))
            bloqueos.Add("RESERVA_NO_CONCILIADA");

        if (reserva is not null)
        {
            var requerimientos = reserva.Detalles
                .GroupBy(d => d.StockFisicoId)
                .ToDictionary(g => g.Key,
                    g => g.Sum(d => d.CantidadAsignada - d.CantidadLiberada - d.CantidadConsumida));
            var saldos = await _catalog.StockFisico.AsNoTracking()
                .Where(s => requerimientos.Keys.Contains(s.Id))
                .ToListAsync(cancellationToken);
            if (saldos.Count != requerimientos.Count ||
                saldos.Any(s => s.ProductoId != reserva.ProductoId ||
                    s.CantidadReservada < requerimientos[s.Id]))
                bloqueos.Add("SALDOS_WMS_INCONSISTENTES");
        }

        if (items.Count == 1 &&
            !await _catalog.Productos.AsNoTracking()
                .AnyAsync(p => p.Id == items[0].ProductoId &&
                    p.VendedorId == orden.VendedorId, cancellationToken))
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
