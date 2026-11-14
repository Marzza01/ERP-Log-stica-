using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketplaceERP.API.Controllers;

public sealed class CheckoutMultiSkuHttpRequest
{
    public List<CheckoutMultiSkuHttpItem> Items { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? CamposNoPermitidos { get; set; }
}

public sealed class CheckoutMultiSkuHttpItem
{
    public Guid ProductoId { get; set; }
    public int Cantidad { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? CamposNoPermitidos { get; set; }
}

/// <summary>
/// Checkout B2C multi-SKU comercial. El servidor determina comprador, vendedor,
/// precio y total. Orden, intento, reservas y mapa 1:N se preparan en una sola
/// transacción PostgreSQL compartida entre Finance y Catalog.
/// </summary>
[ApiController]
[Route("api/checkout/v2")]
public sealed class CheckoutMultiSkuController : ControllerBase
{
    private const string ProcedenciaReservas = "CheckoutMultiSkuV2";
    private const decimal MaxMoneda18_2 = 9999999999999999.99m;

    private readonly FinanceDbContext _finance;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ICommissionService _commission;
    private readonly IContabilidadService _contabilidad;
    private readonly PaymentOperationsMode _paymentMode;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutMultiSkuController> _logger;

    public CheckoutMultiSkuController(
        FinanceDbContext finance,
        IPaymentGateway paymentGateway,
        ICommissionService commission,
        IContabilidadService contabilidad,
        PaymentOperationsMode paymentMode,
        ILoggerFactory loggerFactory,
        IConfiguration configuration,
        ILogger<CheckoutMultiSkuController> logger)
    {
        _finance = finance;
        _paymentGateway = paymentGateway;
        _commission = commission;
        _contabilidad = contabilidad;
        _paymentMode = paymentMode;
        _loggerFactory = loggerFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("comprar")]
    [Authorize(Roles = "Comprador")]
    [PaymentOperationsGuard]
    [ProducesResponseType(typeof(CheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Comprar(
        [FromBody] CheckoutMultiSkuHttpRequest solicitud,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        // Defensa adicional si el servicio se invoca fuera del filtro HTTP.
        if (_paymentMode.IsDisabled || _paymentGateway is PagoServiceDisabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { tipo = "PagosDeshabilitados", mensaje = "La pasarela de pagos está deshabilitada." });

        if (solicitud?.CamposNoPermitidos?.Count > 0 ||
            solicitud?.Items is null ||
            solicitud.Items.Any(i => i is null || i.CamposNoPermitidos?.Count > 0))
            return BadRequest(new { tipo = "CamposNoPermitidos" });

        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            !Regex.IsMatch(idempotencyKey, @"\A[A-Za-z0-9_-]{8,128}\z", RegexOptions.CultureInvariant))
            return BadRequest(new { tipo = "IdempotencyKeyInvalida" });

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var compradorId) ||
            compradorId == Guid.Empty)
            return Unauthorized(new { tipo = "IdentidadInvalida" });

        IReadOnlyList<ItemReservaCarrito> items;
        try
        {
            items = Normalizar(solicitud.Items);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { tipo = "CarritoInvalido" });
        }

        var payloadHash = HashCarrito(items);
        var anterior = await _finance.Ordenes.AsNoTracking()
            .Include(o => o.SplitPago)
            .SingleOrDefaultAsync(o =>
                o.CompradorId == compradorId && o.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (anterior is not null)
            return await RespuestaRepetidaAsync(anterior, payloadHash, cancellationToken);

        Guid ordenId;
        decimal montoTotal;
        string referenciaPago;
        try
        {
            var creado = await CrearOrdenYReservarAsync(
                compradorId, idempotencyKey, payloadHash, items, cancellationToken);
            ordenId = creado.OrdenId;
            montoTotal = creado.MontoTotal;
            referenciaPago = creado.ReferenciaPago;
        }
        catch (DbUpdateException ex) when (EsColisionIdempotencia(ex))
        {
            _finance.ChangeTracker.Clear();
            var concurrente = await _finance.Ordenes.AsNoTracking()
                .Include(o => o.SplitPago)
                .SingleAsync(o => o.CompradorId == compradorId &&
                                  o.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            return await RespuestaRepetidaAsync(concurrente, payloadHash, cancellationToken);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("STOCK_INSUFICIENTE_MULTISKU:", StringComparison.Ordinal))
        {
            _finance.ChangeTracker.Clear();
            return Conflict(new
            {
                tipo = "StockInsuficiente",
                mensaje = "Uno o más productos no tienen existencias suficientes."
            });
        }
        catch (InvalidOperationException ex) when (
            ex.Message is "CATALOGO_MULTISKU_NO_APTO" or
                          "CARRITO_YA_RESERVADO_CON_OTROS_ITEMS")
        {
            _finance.ChangeTracker.Clear();
            return Conflict(new { tipo = "CatalogoNoApto", mensaje = "El carrito cambió o no es apto para checkout." });
        }
        catch (Exception ex)
        {
            _finance.ChangeTracker.Clear();
            _logger.LogError(ex, "No se pudo preparar checkout multi-SKU para comprador {CompradorId}", compradorId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { tipo = "CheckoutNoPreparado", mensaje = "No se pudo preparar la operación. No se envió ningún pago." });
        }

        // El intento ya quedó en estado Enviado y las reservas están ligadas
        // durablemente antes de invocar al proveedor. Una caída no provoca un
        // segundo cobro: las repeticiones quedan bloqueadas para conciliación.
        PagoResult pago;
        try
        {
            pago = await _paymentGateway.ProcesarPagoAsync(new PagoRequest(
                OrdenId: ordenId,
                CompradorId: compradorId,
                MontoTotal: montoTotal,
                Moneda: "USD",
                ClaveIdempotencia: referenciaPago));
        }
        catch (Exception ex)
        {
            await MarcarEstadoPagoAsync(ordenId, EstadoIntentoPago.Incierto, null,
                permitirDesdeAprobado: false);
            _logger.LogError(ex, "Resultado incierto del pago multi-SKU. OrdenId={OrdenId}", ordenId);
            return Conflict(new
            {
                tipo = "PagoPendienteConciliacion",
                mensaje = "No se confirmó el resultado del pago. Las reservas se conservan y no se repetirá el cobro.",
                ordenId
            });
        }

        if (!pago.Exitoso)
        {
            try
            {
                await RechazarYLiberarAsync(ordenId, cancellationToken);
                return Conflict(new
                {
                    tipo = "PagoRechazado",
                    mensaje = "El pago fue rechazado y todas las reservas fueron liberadas.",
                    ordenId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pago rechazado pero no se pudo completar la compensación multi-SKU. OrdenId={OrdenId}", ordenId);
                return Conflict(new
                {
                    tipo = "PagoRechazadoLiberacionPendiente",
                    mensaje = "El proveedor rechazó el pago, pero la liberación de reservas requiere revisión administrativa. No reintentes la compra.",
                    ordenId
                });
            }
        }

        if (string.IsNullOrWhiteSpace(pago.TransaccionId))
        {
            await MarcarEstadoPagoAsync(ordenId, EstadoIntentoPago.Incierto, null,
                permitirDesdeAprobado: false);
            return Conflict(new
            {
                tipo = "PagoPendienteConciliacion",
                mensaje = "El proveedor indicó aprobación sin una referencia de transacción verificable. Las reservas se conservan.",
                ordenId
            });
        }

        try
        {
            await PersistirAprobacionAsync(ordenId, pago.TransaccionId, CancellationToken.None);
            var resultado = await FinalizarComercialmenteAsync(ordenId, CancellationToken.None);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            await MarcarEstadoPagoAsync(ordenId, EstadoIntentoPago.RequiereConciliacion,
                pago.TransaccionId, permitirDesdeAprobado: true);
            _logger.LogError(ex, "Pago aceptado sin cierre comercial completo. OrdenId={OrdenId}", ordenId);
            return Conflict(new
            {
                tipo = "PagoPendienteConciliacion",
                mensaje = "El pago fue aceptado, pero el cierre comercial requiere conciliación. No se repetirá el cobro.",
                ordenId
            });
        }
    }

    private async Task<(Guid OrdenId, decimal MontoTotal, string ReferenciaPago)> CrearOrdenYReservarAsync(
        Guid compradorId,
        string idempotencyKey,
        string payloadHash,
        IReadOnlyList<ItemReservaCarrito> items,
        CancellationToken ct)
    {
        await using var tx = await _finance.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        await using var catalog = CrearCatalogoCompartido();
        await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);

        try
        {
            var ids = items.Select(i => i.ProductoId).ToArray();
            var productos = await catalog.Productos.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .Select(p => new
                {
                    p.Id, p.Sku, p.Nombre, p.PrecioB2C, p.Moneda,
                    p.VendedorId, p.EstaPublicado, p.EstadoProducto
                })
                .ToListAsync(ct);

            if (productos.Count != items.Count ||
                productos.Select(p => p.VendedorId).Distinct().Count() != 1 ||
                productos.Any(p => p.VendedorId == Guid.Empty ||
                    !p.EstaPublicado ||
                    !string.Equals(p.EstadoProducto, "Activo", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(p.Moneda, "USD", StringComparison.OrdinalIgnoreCase) ||
                    p.PrecioB2C <= 0 || decimal.Round(p.PrecioB2C, 2) != p.PrecioB2C))
                throw new InvalidOperationException("CATALOGO_MULTISKU_NO_APTO");

            var vendedorId = productos[0].VendedorId;
            var porId = productos.ToDictionary(p => p.Id);
            decimal subtotal = 0m;
            foreach (var item in items)
            {
                var precio = porId[item.ProductoId].PrecioB2C;
                subtotal = checked(subtotal + checked(precio * item.Cantidad));
                if (subtotal > MaxMoneda18_2 || decimal.Round(subtotal, 2) != subtotal)
                    throw new InvalidOperationException("IMPORTE_EXCEDE_PRECISION_18_2");
            }

            // Política idéntica al Checkout B2C V1 actual: sin IVA ni envío
            // adicional en esta ruta. No se inventa una política fiscal nueva.
            var ordenId = Guid.NewGuid();
            var orden = new Orden
            {
                Id = ordenId,
                NumeroOrden = $"CHK2-{DateTime.UtcNow:yyyyMMdd}-{ordenId:N}",
                CompradorId = compradorId,
                VendedorId = vendedorId,
                IdempotencyKey = idempotencyKey,
                CheckoutPayloadHash = payloadHash,
                SubTotal = subtotal,
                ImpuestoIva = 0m,
                CostoEnvio = 0m,
                MontoTotal = subtotal,
                Moneda = "USD",
                Estado = EstadoOrden.Pendiente,
                TipoCliente = TipoCliente.B2C,
                TipoComprobante = "FacturaConsumidorFinal",
                CondicionPago = "Inmediato",
                FechaCreacion = DateTime.UtcNow,
                Items = items.OrderBy(i => i.ProductoId).Select(i =>
                {
                    var producto = porId[i.ProductoId];
                    return new ItemOrden
                    {
                        Id = Guid.NewGuid(),
                        OrdenId = ordenId,
                        ProductoId = producto.Id,
                        SkuReferencia = producto.Sku,
                        NombreProducto = producto.Nombre,
                        Cantidad = i.Cantidad,
                        PrecioUnitario = producto.PrecioB2C
                    };
                }).ToList()
            };

            var intento = new IntentoPago
            {
                Id = Guid.NewGuid(),
                OrdenId = ordenId,
                ReferenciaOperacion = $"PAY-{ordenId:N}",
                Monto = subtotal,
                Moneda = "USD",
                Estado = EstadoIntentoPago.Preparado,
                ReservaId = null
            };

            _finance.Ordenes.Add(orden);
            _finance.IntentosPago.Add(intento);
            await _finance.SaveChangesAsync(ct);

            var reservasService = new ReservaCarritoAtomicaService(
                catalog, _loggerFactory, _configuration);
            var reserva = await reservasService.ReservarEnTransaccionAsync(
                ordenId, items, ct);

            var reservasCatalogo = await catalog.ReservasStock.AsNoTracking()
                .Where(r => r.OrdenId == ordenId)
                .OrderBy(r => r.ProductoId)
                .ToListAsync(ct);
            if (reservasCatalogo.Count != items.Count ||
                reservasCatalogo.Any(r => r.Estado != EstadoReserva.Pendiente ||
                    !reserva.ReservaIds.Contains(r.Id) ||
                    !items.Any(i => i.ProductoId == r.ProductoId &&
                                    i.Cantidad == r.CantidadOriginal)))
                throw new InvalidOperationException("RESERVAS_CATALOG_INCOMPLETAS");

            _finance.IntentoReservasPago.AddRange(reservasCatalogo.Select(r =>
                new IntentoReservaPago
                {
                    Id = Guid.NewGuid(),
                    IntentoPagoId = intento.Id,
                    ReservaId = r.Id,
                    ProductoId = r.ProductoId,
                    Cantidad = r.CantidadOriginal,
                    Procedencia = ProcedenciaReservas,
                    CreadoUtc = DateTime.UtcNow
                }));

            // Marcar Enviado ANTES del commit y antes de llamar al proveedor.
            intento.Estado = EstadoIntentoPago.Enviado;
            intento.ActualizadoUtc = DateTime.UtcNow;
            await _finance.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return (ordenId, subtotal, intento.ReferenciaOperacion);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            _finance.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task RechazarYLiberarAsync(Guid ordenId, CancellationToken ct)
    {
        await using var tx = await _finance.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        await BloquearOrdenEIntentoAsync(ordenId, tx, ct);
        _finance.ChangeTracker.Clear();

        var orden = await _finance.Ordenes.SingleAsync(o => o.Id == ordenId, ct);
        var intento = await _finance.IntentosPago.SingleAsync(i => i.OrdenId == ordenId, ct);

        if (intento.Estado == EstadoIntentoPago.Rechazado &&
            orden.Estado == EstadoOrden.Cancelada)
        {
            await tx.CommitAsync(ct);
            return;
        }
        if (intento.Estado != EstadoIntentoPago.Enviado ||
            orden.Estado != EstadoOrden.Pendiente ||
            !string.IsNullOrWhiteSpace(intento.TransaccionId))
            throw new InvalidOperationException("ESTADO_NO_PERMITE_COMPENSACION");

        var links = await _finance.IntentoReservasPago.AsNoTracking()
            .Where(x => x.IntentoPagoId == intento.Id)
            .ToListAsync(ct);
        if (links.Count is < 2 or > 20 ||
            links.Any(x => x.Procedencia != ProcedenciaReservas))
            throw new InvalidOperationException("MAPA_RESERVAS_INVALIDO");

        await using var catalog = CrearCatalogoCompartido();
        await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        var reservasService = new ReservaCarritoAtomicaService(
            catalog, _loggerFactory, _configuration);
        await reservasService.LiberarTrasRechazoEnTransaccionAsync(
            ordenId, links.Select(x => x.ReservaId).ToArray(), ct);

        intento.Estado = EstadoIntentoPago.Rechazado;
        intento.ActualizadoUtc = DateTime.UtcNow;
        orden.Estado = EstadoOrden.Cancelada;
        orden.FechaCancelacion = DateTime.UtcNow;
        await _finance.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task PersistirAprobacionAsync(
        Guid ordenId, string transaccionId, CancellationToken ct)
    {
        await using var tx = await _finance.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        await BloquearOrdenEIntentoAsync(ordenId, tx, ct);
        _finance.ChangeTracker.Clear();

        var intento = await _finance.IntentosPago.SingleAsync(i => i.OrdenId == ordenId, ct);
        if (intento.Estado == EstadoIntentoPago.Aprobado &&
            intento.TransaccionId == transaccionId)
        {
            await tx.CommitAsync(ct);
            return;
        }
        if (intento.Estado != EstadoIntentoPago.Enviado)
            throw new InvalidOperationException("INTENTO_NO_PERMITE_APROBACION");

        intento.Estado = EstadoIntentoPago.Aprobado;
        intento.TransaccionId = transaccionId;
        intento.ActualizadoUtc = DateTime.UtcNow;
        await _finance.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<CheckoutResult> FinalizarComercialmenteAsync(
        Guid ordenId, CancellationToken ct)
    {
        await using var tx = await _finance.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);
        await BloquearOrdenEIntentoAsync(ordenId, tx, ct);
        _finance.ChangeTracker.Clear();

        var orden = await _finance.Ordenes.SingleAsync(o => o.Id == ordenId, ct);
        var intento = await _finance.IntentosPago.SingleAsync(i => i.OrdenId == ordenId, ct);
        var splitExistente = await _finance.SplitsPago.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrdenId == ordenId, ct);

        if (intento.Estado == EstadoIntentoPago.Completado &&
            orden.Estado is EstadoOrden.Aprobada or EstadoOrden.EnProceso or
                EstadoOrden.Enviada or EstadoOrden.Entregada &&
            splitExistente is not null &&
            splitExistente.TransaccionId == intento.TransaccionId)
        {
            await tx.CommitAsync(ct);
            return new CheckoutResult(
                ordenId, splitExistente.TransaccionId,
                splitExistente.MontoVendedor, splitExistente.MontoMarketplace);
        }

        if (intento.Estado != EstadoIntentoPago.Aprobado ||
            string.IsNullOrWhiteSpace(intento.TransaccionId) ||
            orden.Estado != EstadoOrden.Pendiente ||
            splitExistente is not null)
            throw new InvalidOperationException("ESTADO_NO_PERMITE_CIERRE_COMERCIAL");

        var items = await _finance.ItemsOrden.AsNoTracking()
            .Where(i => i.OrdenId == ordenId)
            .OrderBy(i => i.ProductoId)
            .ToListAsync(ct);
        var links = await _finance.IntentoReservasPago.AsNoTracking()
            .Where(x => x.IntentoPagoId == intento.Id)
            .OrderBy(x => x.ProductoId)
            .ToListAsync(ct);

        if (items.Count is < 2 or > 20 || items.Count != links.Count ||
            items.Select(i => i.ProductoId).Distinct().Count() != items.Count ||
            links.Select(l => l.ProductoId).Distinct().Count() != links.Count ||
            links.Select(l => l.ReservaId).Distinct().Count() != links.Count ||
            items.Any(i => i.Cantidad <= 0 || i.PrecioUnitario <= 0 ||
                !links.Any(l => l.ProductoId == i.ProductoId &&
                                l.Cantidad == i.Cantidad)) ||
            links.Any(l => l.Procedencia != ProcedenciaReservas ||
                l.Cantidad <= 0 || l.ReservaId == Guid.Empty) ||
            items.Sum(i => i.PrecioUnitario * i.Cantidad) != orden.SubTotal ||
            intento.Monto != orden.MontoTotal || orden.SubTotal != orden.MontoTotal ||
            orden.ImpuestoIva != 0m || orden.CostoEnvio != 0m ||
            intento.Moneda != "USD" || orden.Moneda != "USD" ||
            intento.ReferenciaOperacion != $"PAY-{ordenId:N}")
            throw new InvalidOperationException("CHECKOUT_MULTISKU_FINANCE_INCONSISTENTE");

        await using var catalog = CrearCatalogoCompartido();
        await catalog.Database.UseTransactionAsync(tx.GetDbTransaction(), ct);
        var reservas = await catalog.ReservasStock.AsNoTracking()
            .Where(r => r.OrdenId == ordenId)
            .OrderBy(r => r.ProductoId)
            .ToListAsync(ct);

        if (reservas.Count != links.Count ||
            reservas.Any(r => r.Estado != EstadoReserva.Pendiente ||
                r.CantidadLiberada != 0 || r.CantidadConsumida != 0 ||
                !links.Any(l => l.ReservaId == r.Id &&
                                l.ProductoId == r.ProductoId &&
                                l.Cantidad == r.CantidadOriginal)))
            throw new InvalidOperationException("CHECKOUT_MULTISKU_RESERVAS_INCONSISTENTES");

        var ids = items.Select(i => i.ProductoId).ToArray();
        if (await catalog.Productos.AsNoTracking().CountAsync(
                p => ids.Contains(p.Id) && p.VendedorId == orden.VendedorId, ct) != items.Count)
            throw new InvalidOperationException("VENDEDOR_PRODUCTO_NO_COINCIDEN");

        var comision = decimal.Round(
            await _commission.CalcularComisionAsync(orden.VendedorId, orden.SubTotal), 2);
        if (comision < 0m || comision >= orden.SubTotal ||
            decimal.Round(comision, 2) != comision)
            throw new InvalidOperationException("COMISION_FUERA_DE_RANGO");

        var montoVendedor = decimal.Round(orden.SubTotal - comision, 2);
        var montoMarketplace = decimal.Round(comision, 2);
        var porcentajeVendedor = decimal.Round(montoVendedor / orden.SubTotal * 100m, 2);
        var porcentajeMarketplace = 100m - porcentajeVendedor;

        _finance.SplitsPago.Add(new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = ordenId,
            MontoTotal = orden.SubTotal,
            PorcentajeVendedor = porcentajeVendedor,
            MontoVendedor = montoVendedor,
            PorcentajeMarketplace = porcentajeMarketplace,
            MontoMarketplace = montoMarketplace,
            TransaccionId = intento.TransaccionId!,
            FechaProcesado = DateTime.UtcNow
        });
        await _finance.SaveChangesAsync(ct);

        var reservasService = new ReservaCarritoAtomicaService(
            catalog, _loggerFactory, _configuration);
        await reservasService.ConfirmarEnTransaccionAsync(
            ordenId, links.Select(x => x.ReservaId).ToArray(), ct);

        await _contabilidad.GenerarAsientoVentaAsync(
            ordenId, orden.SubTotal, orden.ImpuestoIva,
            orden.TipoComprobante, ct);

        orden.Estado = EstadoOrden.Aprobada;
        orden.FechaAprobacion = DateTime.UtcNow;
        intento.Estado = EstadoIntentoPago.Completado;
        intento.ActualizadoUtc = DateTime.UtcNow;
        await _finance.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CheckoutResult(
            ordenId, intento.TransaccionId!,
            montoVendedor, montoMarketplace);
    }

    private async Task MarcarEstadoPagoAsync(
        Guid ordenId,
        EstadoIntentoPago nuevoEstado,
        string? transaccionId,
        bool permitirDesdeAprobado)
    {
        try
        {
            await using var tx = await _finance.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, CancellationToken.None);
            await BloquearOrdenEIntentoAsync(ordenId, tx, CancellationToken.None);
            _finance.ChangeTracker.Clear();

            var intento = await _finance.IntentosPago.SingleAsync(
                i => i.OrdenId == ordenId, CancellationToken.None);
            var permitido = intento.Estado == EstadoIntentoPago.Enviado ||
                (permitirDesdeAprobado && intento.Estado == EstadoIntentoPago.Aprobado);
            if (!permitido)
            {
                await tx.CommitAsync(CancellationToken.None);
                return;
            }

            intento.Estado = nuevoEstado;
            if (!string.IsNullOrWhiteSpace(transaccionId))
                intento.TransaccionId = transaccionId;
            intento.ActualizadoUtc = DateTime.UtcNow;
            await _finance.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo persistir estado {Estado} del intento multi-SKU. OrdenId={OrdenId}",
                nuevoEstado, ordenId);
        }
    }

    private async Task BloquearOrdenEIntentoAsync(
        Guid ordenId, IDbContextTransaction tx, CancellationToken ct)
    {
        await _finance.Database.ExecuteSqlInterpolatedAsync(
            $@"SELECT ""Id"" FROM finance.ordenes WHERE ""Id"" = {ordenId} FOR UPDATE", ct);
        await _finance.Database.ExecuteSqlInterpolatedAsync(
            $@"SELECT ""Id"" FROM finance.intentos_pago WHERE ""OrdenId"" = {ordenId} FOR UPDATE", ct);
    }

    private CatalogDbContext CrearCatalogoCompartido()
    {
        var connection = _finance.Database.GetDbConnection();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(connection,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
            .Options;
        return new CatalogDbContext(options);
    }

    private async Task<IActionResult> RespuestaRepetidaAsync(
        Orden anterior, string payloadHash, CancellationToken ct)
    {
        if (!string.Equals(anterior.CheckoutPayloadHash, payloadHash, StringComparison.Ordinal))
            return Conflict(new { tipo = "IdempotenciaPayloadDistinto" });

        if (anterior.Estado == EstadoOrden.Cancelada)
            return Conflict(new { tipo = "CheckoutCancelado", ordenId = anterior.Id });

        var intento = await _finance.IntentosPago.AsNoTracking()
            .SingleOrDefaultAsync(i => i.OrdenId == anterior.Id, ct);
        if (intento?.Estado == EstadoIntentoPago.Completado &&
            anterior.SplitPago is not null &&
            anterior.SplitPago.TransaccionId == intento.TransaccionId &&
            anterior.Estado is EstadoOrden.Aprobada or EstadoOrden.EnProceso or
                EstadoOrden.Enviada or EstadoOrden.Entregada)
            return Ok(new CheckoutResult(
                anterior.Id, anterior.SplitPago.TransaccionId,
                anterior.SplitPago.MontoVendedor, anterior.SplitPago.MontoMarketplace));

        Response.Headers["Retry-After"] = "2";
        return Conflict(new
        {
            tipo = "CheckoutEnProcesoNoReemitir",
            mensaje = "La operación ya existe o requiere conciliación. No se inició otro cobro.",
            ordenId = anterior.Id
        });
    }

    private static IReadOnlyList<ItemReservaCarrito> Normalizar(
        IReadOnlyList<CheckoutMultiSkuHttpItem> origen)
    {
        if (origen is null || origen.Count is < 2 or > 200 ||
            origen.Any(x => x is null || x.ProductoId == Guid.Empty ||
                            x.Cantidad is < 1 or > 9999))
            throw new InvalidOperationException("CARRITO_INVALIDO");

        var grupos = origen.GroupBy(x => x.ProductoId)
            .OrderBy(g => g.Key).ToArray();
        if (grupos.Length is < 2 or > 20)
            throw new InvalidOperationException("CARRITO_SKUS_FUERA_DE_LIMITE");

        return grupos.Select(g =>
        {
            var cantidad = g.Sum(x => (long)x.Cantidad);
            if (cantidad is < 1 or > 9999)
                throw new InvalidOperationException("CANTIDAD_ACUMULADA_INVALIDA");
            return new ItemReservaCarrito(g.Key, (int)cantidad);
        }).ToArray();
    }

    private static string HashCarrito(IReadOnlyList<ItemReservaCarrito> items)
    {
        var payload = "checkout-v2|" + string.Join("|",
            items.OrderBy(x => x.ProductoId).Select(x =>
                x.ProductoId.ToString("N") + ":" +
                x.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool EsColisionIdempotencia(DbUpdateException ex) =>
        ex.InnerException is PostgresException postgres &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgres.ConstraintName is not null &&
        postgres.ConstraintName.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase);
}
