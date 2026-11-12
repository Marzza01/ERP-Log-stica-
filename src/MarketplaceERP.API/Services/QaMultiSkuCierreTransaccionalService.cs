using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// PROTOTIPO ESTRICTAMENTE QA: completa una orden de 2-20 SKU SOLO después
/// de un pago YA APROBADO por el SIMULADOR QA. No expone endpoints, no se
/// registra en DI, no consulta ni invoca pasarelas y no crea liquidaciones.
/// Bloqueado por código fuera de postgres@localhost:5432/ci_checkout_http_*.
/// Supone un solo vendedor, importe sin IVA/envío y comisión QA fija del 10%;
/// NO trasladar a producción ni usar como comprobante de pago real.
/// </summary>
public sealed class QaMultiSkuCierreTransaccionalService
{
    private readonly FinanceDbContext _finance;

    public QaMultiSkuCierreTransaccionalService(FinanceDbContext finance) =>
        _finance = finance;

    public async Task<string> CerrarAsync(Guid intentoId, bool autorizacionExplicitaQa,
        string? puntoFalloQa = null, CancellationToken ct = default)
    {
        if (!autorizacionExplicitaQa)
            throw new InvalidOperationException("CIERRE_MULTISKU_QA_NO_AUTORIZADO");
        if (intentoId == Guid.Empty)
            throw new InvalidOperationException("INTENTO_VACIO_QA");

        var connection = _finance.Database.GetDbConnection();
        var cs = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(cs.Host, StringComparer.OrdinalIgnoreCase) ||
            cs.Port != 5432 || cs.Username != "postgres" ||
            string.IsNullOrWhiteSpace(cs.Database) ||
            !cs.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("PROHIBIDO_FUERA_DE_BASE_EFIMERA_LOCAL");

        await using var tx = await _finance.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        try
        {
            var primeraLectura = await _finance.IntentosPago.AsNoTracking()
                .SingleOrDefaultAsync(i => i.Id == intentoId, ct)
                ?? throw new InvalidOperationException("INTENTO_NO_EXISTE");
            var ordenId = primeraLectura.OrdenId;
            if (ordenId == Guid.Empty)
                throw new InvalidOperationException("ORDEN_NO_IDENTIFICABLE");

            // Misma llave de generación de PickList y reservas por orden.
            var llave = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({llave})", ct);
            // La ruta RMA usa una segunda llave: orden obligatorio y estable.
            var llaveRma = BitConverter.ToInt32(ordenId.ToByteArray(), 0);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(6173153, {llaveRma})", ct);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM finance.ordenes WHERE ""Id"" = {ordenId} FOR UPDATE", ct);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM finance.intentos_pago WHERE ""Id"" = {intentoId} FOR UPDATE", ct);
            await _finance.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM finance.splits_pago WHERE ""OrdenId"" = {ordenId} FOR UPDATE", ct);

            // Descarta toda entidad leída ANTES de esperar bloqueos.
            _finance.ChangeTracker.Clear();
            var intento = await _finance.IntentosPago.SingleAsync(i => i.Id == intentoId, ct);
            var orden = await _finance.Ordenes.SingleAsync(o => o.Id == ordenId, ct);
            var items = await _finance.ItemsOrden.AsNoTracking()
                .Where(x => x.OrdenId == ordenId).ToListAsync(ct);
            var links = await _finance.IntentoReservasPago.AsNoTracking()
                .Where(x => x.IntentoPagoId == intentoId).ToListAsync(ct);
            var splits = await _finance.SplitsPago.AsNoTracking()
                .Where(x => x.OrdenId == ordenId).ToListAsync(ct);

            // Sin huella completa, sin fecha y referencia QA de cobro no hay cierre.
            if (intento.OrdenId != ordenId ||
                string.IsNullOrWhiteSpace(intento.TransaccionId) ||
                !intento.TransaccionId.StartsWith("QA-SIM-", StringComparison.Ordinal) ||
                intento.ReferenciaOperacion != $"PAY-{ordenId:N}" ||
                intento.ReservaId.HasValue ||
                intento.Monto <= 0 || intento.Moneda != "USD" ||
                orden.CompradorId == Guid.Empty || orden.VendedorId == Guid.Empty ||
                string.IsNullOrWhiteSpace(orden.IdempotencyKey) ||
                string.IsNullOrWhiteSpace(orden.CheckoutPayloadHash) ||
                orden.MontoTotal != intento.Monto || orden.Moneda != intento.Moneda ||
                orden.SubTotal != orden.MontoTotal ||
                orden.ImpuestoIva != 0 || orden.CostoEnvio != 0 ||
                items.Count is < 2 or > 20 || items.Count != links.Count ||
                items.Select(i => i.ProductoId).Distinct().Count() != items.Count ||
                links.Select(l => l.ProductoId).Distinct().Count() != links.Count ||
                links.Select(l => l.ReservaId).Distinct().Count() != links.Count ||
                items.Any(i => i.ProductoId == Guid.Empty ||
                    i.Cantidad <= 0 || i.PrecioUnitario <= 0 ||
                    !links.Any(l => l.ProductoId == i.ProductoId &&
                                    l.Cantidad == i.Cantidad)) ||
                links.Any(l => l.ReservaId == Guid.Empty ||
                    l.Procedencia != "MultiSkuQa" || l.Cantidad <= 0) ||
                items.Sum(i => i.PrecioUnitario * i.Cantidad) != intento.Monto ||
                orden.FechaCancelacion.HasValue || orden.FechaEnvio.HasValue ||
                orden.FechaEntregaReal.HasValue ||
                !string.IsNullOrWhiteSpace(orden.GuiaEnvio))
                throw new InvalidOperationException("PRECIERRE_MULTISKU_FINANCE_INVALIDO");

            if (await _finance.ReembolsosPago.AnyAsync(x => x.OrdenId == ordenId, ct) ||
                await _finance.Liquidaciones.AnyAsync(l =>
                    splits.Select(s => s.Id).Contains(l.SplitPagoId), ct))
                throw new InvalidOperationException("OPERACION_FINANCIERA_POSTERIOR_EXISTENTE");

            // Mismos candados y misma transacción compartida en Finance y Catalog.
            var options = new DbContextOptionsBuilder<CatalogDbContext>()
                .UseNpgsql(connection).Options;
            await using var catalog = new CatalogDbContext(options);
            await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
            var productos = items.Select(i => i.ProductoId).OrderBy(x => x).ToArray();
            foreach (var producto in productos)
                await catalog.Database.ExecuteSqlInterpolatedAsync(
                    $@"SELECT ""Id"" FROM catalog.stock_fisico WHERE ""ProductoId"" = {producto} ORDER BY ""Id"" FOR UPDATE", ct);
            await catalog.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM catalog.reservas_stock WHERE ""OrdenId"" = {ordenId} ORDER BY ""Id"" FOR UPDATE", ct);
            if (await catalog.PickLists.AnyAsync(x => x.OrdenId == ordenId, ct) ||
                await catalog.PaquetesDespacho.AnyAsync(x => x.OrdenId == ordenId, ct) ||
                await catalog.DevolucionesRMA.AnyAsync(x => x.OrdenId == ordenId, ct))
                throw new InvalidOperationException("YA_EXISTE_OPERACION_WMS_RMA");

            var reservas = await catalog.ReservasStock.AsNoTracking()
                .Include(r => r.Detalles)
                .Where(r => r.OrdenId == ordenId)
                .OrderBy(r => r.ProductoId).ToListAsync(ct);
            if (reservas.Count != links.Count ||
                reservas.Any(r => r.CantidadOriginal <= 0 ||
                    r.CantidadLiberada != 0 || r.CantidadConsumida != 0 ||
                    r.Detalles.Count == 0 ||
                    r.Detalles.Sum(d => d.CantidadAsignada) != r.CantidadOriginal ||
                    r.Detalles.Any(d => d.CantidadAsignada <= 0 ||
                        d.CantidadLiberada != 0 || d.CantidadConsumida != 0) ||
                    !links.Any(l => l.ReservaId == r.Id &&
                                    l.ProductoId == r.ProductoId &&
                                    l.Cantidad == r.CantidadOriginal)))
                throw new InvalidOperationException("RESERVAS_NO_COINCIDEN_CON_MAPA");

            if (await catalog.Productos.AsNoTracking()
                .CountAsync(p => productos.Contains(p.Id) &&
                    p.VendedorId == orden.VendedorId, ct) != productos.Length)
                throw new InvalidOperationException("VENDEDOR_O_PRODUCTO_INCOHERENTE");

            var necesarias = reservas.SelectMany(r => r.Detalles)
                .GroupBy(d => d.StockFisicoId)
                .ToDictionary(g => g.Key, g => g.Sum(d => d.CantidadAsignada));
            var saldos = await catalog.StockFisico.AsNoTracking()
                .Where(s => necesarias.Keys.Contains(s.Id)).ToListAsync(ct);
            if (saldos.Count != necesarias.Count ||
                saldos.Any(s => s.CantidadReservada < necesarias[s.Id] ||
                    s.CantidadTotal < s.CantidadReservada))
                throw new InvalidOperationException("SALDOS_FISICOS_NO_CUBREN_RESERVAS");

            // Retorno idempotente solo si TODO lo posterior está coherente.
            if (intento.Estado == EstadoIntentoPago.Completado &&
                orden.Estado == EstadoOrden.Aprobada && splits.Count == 1 &&
                splits[0].TransaccionId == intento.TransaccionId &&
                splits[0].MontoTotal == orden.MontoTotal &&
                splits[0].MontoVendedor + splits[0].MontoMarketplace == orden.MontoTotal &&
                reservas.All(r => r.Estado == EstadoReserva.Confirmada))
            {
                await tx.CommitAsync(ct);
                return "YA_FINALIZADA";
            }

            if (intento.Estado != EstadoIntentoPago.Aprobado ||
                orden.Estado != EstadoOrden.Pendiente ||
                splits.Count != 0 ||
                reservas.Any(r => r.Estado != EstadoReserva.Pendiente))
                throw new InvalidOperationException("ESTADO_NO_PERMITE_CIERRE_QA");

            // Comisión fija de 10% SOLO en ensayo; no sustituye la política real.
            var comision = decimal.Round(intento.Monto * .10m, 2);
            var vendedor = intento.Monto - comision;
            _finance.SplitsPago.Add(new SplitPago
            {
                Id = Guid.NewGuid(), OrdenId = ordenId,
                MontoTotal = intento.Monto,
                MontoVendedor = vendedor, MontoMarketplace = comision,
                PorcentajeVendedor = 90m, PorcentajeMarketplace = 10m,
                TransaccionId = intento.TransaccionId!,
                FechaProcesado = DateTime.UtcNow
            });
            await _finance.SaveChangesAsync(ct);
            if (puntoFalloQa == "trasSplit")
                throw new InvalidOperationException("QA_FALLO_TRAS_SPLIT");

            // Solo en este prototipo QA: reproducir EXACTAMENTE la transición
            // Pendiente -> Confirmada del StockService interno. Las filas están
            // bloqueadas y los SaveChanges usan la MISMA transacción Finance.
            // La implementación definitiva debe pasar por IStockService.
            for (int i = 0; i < reservas.Count; i++)
            {
                var actual = await catalog.ReservasStock
                    .SingleAsync(r => r.Id == reservas[i].Id, ct);
                if (actual.Estado != EstadoReserva.Pendiente)
                    throw new InvalidOperationException("RESERVA_CAMBIO_DURANTE_CIERRE");
                actual.Estado = EstadoReserva.Confirmada;
                actual.FechaConfirmacion = DateTime.UtcNow;
                await catalog.SaveChangesAsync(ct);
                if (puntoFalloQa == "trasPrimeraReserva" && i == 0)
                    throw new InvalidOperationException("QA_FALLO_TRAS_PRIMERA_CONFIRMACION");
            }

            intento.Estado = EstadoIntentoPago.Completado;
            intento.ActualizadoUtc = DateTime.UtcNow;
            orden.Estado = EstadoOrden.Aprobada;
            orden.FechaAprobacion = DateTime.UtcNow;
            await _finance.SaveChangesAsync(ct);
            if (puntoFalloQa == "antesDeCommit")
                throw new InvalidOperationException("QA_FALLO_ANTES_COMMIT");

            await tx.CommitAsync(ct);
            return "FINALIZADA_SIMULADA";
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            _finance.ChangeTracker.Clear();
            throw;
        }
    }
}
