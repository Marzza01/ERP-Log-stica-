using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MarketplaceERP.API.Services;

public sealed class MarketplaceOrderRequest
{
    public string ExternalOrderCode { get; init; } = string.Empty;
    public string CustomerType { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string? CustomerPhone { get; init; }
    public string? Company { get; init; }
    public string? TaxId { get; init; }
    public string DeliveryAddress { get; init; } = string.Empty;
    public string? City { get; init; }
    public string RequestedPaymentMethod { get; init; } = string.Empty;
    public List<MarketplaceOrderItemRequest> Items { get; init; } = new();
}

public sealed class MarketplaceOrderItemRequest
{
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
}

public sealed class MarketplaceReturnRequest
{
    public string ExternalReturnCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class MarketplaceOrderTransitionRequest
{
    public string NewStatus { get; init; } = string.Empty;
    public string? TrackingCode { get; init; }
}

public sealed record MarketplaceOrderLineResponse(
    string Sku, string Name, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record MarketplaceOrderResponse(
    Guid ErpOrderId,
    string ErpOrderNumber,
    string ExternalOrderCode,
    string Status,
    string CustomerType,
    decimal Subtotal,
    decimal Tax,
    decimal Shipping,
    decimal Total,
    string PaymentMethod,
    string PaymentStatus,
    DateTime CreatedAtUtc,
    DateTime? EstimatedDeliveryUtc,
    DateTime? DeliveredAtUtc,
    string? Carrier,
    string? TrackingCode,
    string? ReturnStatus,
    IReadOnlyList<Guid> ReturnRmaIds,
    IReadOnlyList<MarketplaceOrderLineResponse> Items);

public sealed class MarketplaceIntegrationException(int statusCode, string code, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>
/// ERP-owned LOGIMKT integration. Prices, seller, taxes, stock and order state
/// are resolved by the ERP. This flow creates no payment attempt and never calls
/// a payment gateway; all received orders begin pending manual/operational review.
/// </summary>
public sealed class MarketplaceIntegrationService
{
    private const decimal MaxMoney = 9999999999999999.99m;
    private readonly FinanceDbContext _finance;
    private readonly CatalogDbContext _catalog;
    private readonly CoreDbContext _core;
    private readonly IStockService _stock;
    private readonly IRmaService _rma;
    private readonly IConfiguration _configuration;

    public MarketplaceIntegrationService(
        FinanceDbContext finance,
        CatalogDbContext catalog,
        CoreDbContext core,
        IStockService stock,
        IRmaService rma,
        IConfiguration configuration)
    {
        _finance = finance;
        _catalog = catalog;
        _core = core;
        _stock = stock;
        _rma = rma;
        _configuration = configuration;
    }

    public async Task<MarketplaceOrderResponse> CreateOrderAsync(
        MarketplaceOrderRequest request, Guid buyerId, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request);
        var payloadHash = HashPayload(normalized);

        return await InSharedTransactionAsync(async ct =>
        {
            await LockAsync("ORDER:" + normalized.ExternalOrderCode, ct);

            var existing = await _finance.MarketplaceOrders
                .Include(m => m.Order).ThenInclude(o => o.Items)
                .SingleOrDefaultAsync(m => m.ExternalOrderCode == normalized.ExternalOrderCode, ct);
            if (existing is not null)
            {
                if (existing.Order.CompradorId != buyerId)
                    throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                        "No existe un pedido LOGIMKT con ese código.");
                if (!string.Equals(existing.Order.CheckoutPayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new MarketplaceIntegrationException(409, "IDEMPOTENCY_CONFLICT",
                        "El código externo ya fue utilizado con datos diferentes.");
                return Map(existing);
            }

            if (buyerId == Guid.Empty)
                throw new MarketplaceIntegrationException(401, "BUYER_IDENTITY_REQUIRED",
                    "La sesión ERP no contiene un comprador válido.");

            var buyer = await _core.Usuarios.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == buyerId && x.Activo, ct);
            if (buyer is null || !string.Equals(buyer.Rol?.Trim(), "Comprador", StringComparison.OrdinalIgnoreCase))
                throw new MarketplaceIntegrationException(403, "BUYER_ACCOUNT_INVALID",
                    "La cuenta ERP autenticada no está activa o no tiene rol Comprador.");

            var skus = normalized.Items.Select(x => x.Sku).ToArray();
            var products = await _catalog.Productos
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM catalog.productos
                    WHERE "Sku" = ANY({skus})
                    ORDER BY "Sku"
                    FOR SHARE
                    """)
                .AsNoTracking()
                .ToListAsync(ct);

            if (products.Count != normalized.Items.Count)
                throw new MarketplaceIntegrationException(422, "SKU_NOT_FOUND",
                    "Uno o más SKU del carrito no existen en el catálogo del ERP.");

            var bySku = products.ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
            if (products.Select(x => x.VendedorId).Distinct().Count() != 1 ||
                products.Any(x => x.VendedorId == Guid.Empty))
                throw new MarketplaceIntegrationException(409, "MULTI_SELLER_CART",
                    "El ERP requiere separar el carrito por vendedor antes de crear la orden.");

            if (products.Any(x => !x.EstaPublicado ||
                    !string.Equals(x.EstadoProducto, "Activo", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(x.Moneda, "USD", StringComparison.OrdinalIgnoreCase)))
                throw new MarketplaceIntegrationException(409, "PRODUCT_UNAVAILABLE",
                    "Uno o más productos no están publicados o no están disponibles en USD.");

            var tipoCliente = normalized.CustomerType;
            decimal subtotal = 0m;
            var orderId = Guid.NewGuid();
            var orderItems = new List<ItemOrden>();
            foreach (var line in normalized.Items)
            {
                var product = bySku[line.Sku];
                var price = tipoCliente == TipoCliente.B2B ? product.PrecioB2B : product.PrecioB2C;
                if (price <= 0m || price > 1000000m || decimal.Round(price, 2) != price)
                    throw new MarketplaceIntegrationException(409, "INVALID_ERP_PRICE",
                        $"El SKU {line.Sku} no tiene un precio ERP válido para este tipo de cliente.");

                try { subtotal = checked(subtotal + checked(price * line.Quantity)); }
                catch (OverflowException)
                {
                    throw new MarketplaceIntegrationException(422, "ORDER_TOTAL_OVERFLOW",
                        "El importe del pedido excede los límites admitidos.");
                }
                if (subtotal > MaxMoney || decimal.Round(subtotal, 2) != subtotal)
                    throw new MarketplaceIntegrationException(422, "ORDER_TOTAL_OVERFLOW",
                        "El importe del pedido excede los límites admitidos.");

                orderItems.Add(new ItemOrden
                {
                    Id = Guid.NewGuid(),
                    OrdenId = orderId,
                    ProductoId = product.Id,
                    SkuReferencia = product.Sku,
                    NombreProducto = product.Nombre,
                    Cantidad = line.Quantity,
                    PrecioUnitario = price
                });
            }

            var minimum = tipoCliente == TipoCliente.B2B
                ? _configuration.GetValue<decimal?>("Marketplace:MinimumB2B") ?? 500m
                : _configuration.GetValue<decimal?>("Marketplace:MinimumB2C") ?? 5m;
            if (subtotal < minimum)
                throw new MarketplaceIntegrationException(422, "MINIMUM_ORDER_NOT_MET",
                    $"El mínimo de compra para {tipoCliente} es USD {minimum:0.00}.");

            var shipping = CalculateShipping(tipoCliente, subtotal);
            var iva = decimal.Round(subtotal * 0.13m, 2);
            var total = checked(subtotal + iva + shipping);
            if (iva > MaxMoney || total > MaxMoney || decimal.Round(total, 2) != total)
                throw new MarketplaceIntegrationException(422, "ORDER_TOTAL_OVERFLOW",
                    "El importe total con IVA y envío excede los límites admitidos.");

            var now = DateTime.UtcNow;
            var sellerId = products[0].VendedorId;
            var seller = await _core.Vendedores.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == sellerId && x.EstaActivo, ct);
            if (seller is null)
                throw new MarketplaceIntegrationException(409, "SELLER_UNAVAILABLE",
                    "El vendedor asociado al catálogo no está activo en el ERP.");

            var order = new Orden
            {
                Id = orderId,
                NumeroOrden = normalized.ExternalOrderCode,
                CompradorId = buyerId,
                VendedorId = sellerId,
                IdempotencyKey = "LOGIMKT-" + normalized.ExternalOrderCode,
                CheckoutPayloadHash = payloadHash,
                SubTotal = subtotal,
                ImpuestoIva = iva,
                CostoEnvio = shipping,
                MontoTotal = total,
                Moneda = "USD",
                Estado = EstadoOrden.Pendiente,
                TipoCliente = tipoCliente,
                TipoComprobante = tipoCliente == TipoCliente.B2B ? "CreditoFiscalCCF" : "FacturaConsumidorFinal",
                CondicionPago = normalized.RequestedPaymentMethod == "credito_30" ? "Credito30" : "Inmediato",
                DireccionEntrega = normalized.DeliveryAddress,
                ZonaEnvio = normalized.City ?? "San Salvador y AMSS",
                Transportista = "LOGIMKT Express",
                FechaCreacion = now,
                FechaEntregaEstimada = now.AddDays(tipoCliente == TipoCliente.B2B ? 5 : 3),
                Items = orderItems
            };

            var metadata = new MarketplaceOrderMetadata
            {
                OrderId = orderId,
                ExternalOrderCode = normalized.ExternalOrderCode,
                CustomerName = normalized.CustomerName,
                CustomerEmail = normalized.CustomerEmail,
                CustomerPhone = normalized.CustomerPhone,
                Company = normalized.Company,
                TaxId = normalized.TaxId,
                RequestedPaymentMethod = normalized.RequestedPaymentMethod,
                PaymentStatus = normalized.RequestedPaymentMethod switch
                {
                    "transferencia" => "Pendiente de verificación",
                    "contra_entrega" => "Cobro contra entrega pendiente",
                    "credito_30" => "Crédito 30 días pendiente de autorización",
                    _ => "Pago con tarjeta no habilitado"
                },
                CreatedAtUtc = now,
                Order = order
            };

            _finance.Ordenes.Add(order);
            _finance.MarketplaceOrders.Add(metadata);
            await _finance.SaveChangesAsync(ct);

            // Reserve every SKU in the same PostgreSQL transaction as the order.
            // A failure on any line rolls back the order and every reservation.
            foreach (var line in normalized.Items.OrderBy(x => x.Sku, StringComparer.Ordinal))
            {
                var reservation = await _stock.ReservarStockAsync(
                    bySku[line.Sku].Id, line.Quantity, orderId);
                if (!reservation.Exitoso || reservation.ReservaId == Guid.Empty)
                    throw new MarketplaceIntegrationException(409, "INSUFFICIENT_STOCK",
                        $"No hay inventario suficiente para el SKU {line.Sku}.");
            }

            return Map(metadata);
        }, cancellationToken);
    }

    public async Task<MarketplaceOrderResponse> GetOrderAsync(
        string externalOrderCode, Guid buyerId, bool isAdmin, CancellationToken cancellationToken)
    {
        var code = NormalizeExternalCode(externalOrderCode);
        var metadata = await _finance.MarketplaceOrders.AsNoTracking()
            .Include(m => m.Order).ThenInclude(o => o.Items)
            .SingleOrDefaultAsync(m => m.ExternalOrderCode == code, cancellationToken);
        if (metadata is null || (!isAdmin && metadata.Order.CompradorId != buyerId))
            throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                "No existe un pedido LOGIMKT con ese código.");
        return Map(metadata);
    }

    public async Task<MarketplaceOrderResponse> CancelOrderAsync(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        var code = NormalizeExternalCode(externalOrderCode);
        return await InSharedTransactionAsync(async ct =>
        {
            await LockAsync("ORDER:" + code, ct);
            var metadata = await FindTrackedAsync(code, ct);
            if (metadata is null)
                throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                    "No existe un pedido LOGIMKT con ese código.");

            var order = metadata.Order;
            if (order.Estado == EstadoOrden.Cancelada)
                return Map(metadata);
            if (order.Estado is not (EstadoOrden.Pendiente or EstadoOrden.Aprobada or EstadoOrden.EnProceso))
                throw new MarketplaceIntegrationException(409, "ORDER_NOT_CANCELLABLE",
                    "Solo se pueden cancelar pedidos que aún no han sido enviados desde el ERP.");

            await EnsureNoPaymentArtifactsAsync(metadata, ct);
            await ReleaseReservationsAsync(order.Id, ct);
            order.Estado = EstadoOrden.Cancelada;
            order.FechaCancelacion = DateTime.UtcNow;
            metadata.PaymentStatus = "No cobrado - pedido cancelado";
            await _finance.SaveChangesAsync(ct);
            return Map(metadata);
        }, cancellationToken);
    }

    public async Task<MarketplaceOrderResponse> RequestReturnAsync(
        string externalOrderCode, MarketplaceReturnRequest request, CancellationToken cancellationToken)
    {
        var code = NormalizeExternalCode(externalOrderCode);
        var returnCode = NormalizeReturnCode(request.ExternalReturnCode);
        var reason = NormalizeRequired(request.Reason, 500, "El motivo de devolución es obligatorio.");

        return await InSharedTransactionAsync(async ct =>
        {
            await LockAsync("RETURN:" + returnCode, ct);
            await LockAsync("ORDER:" + code, ct);
            var metadata = await FindTrackedAsync(code, ct);
            if (metadata is null)
                throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                    "No existe un pedido LOGIMKT con ese código.");

            if (await _finance.MarketplaceOrders.AnyAsync(
                    x => x.ExternalReturnCode == returnCode && x.OrderId != metadata.OrderId, ct))
                throw new MarketplaceIntegrationException(409, "RETURN_CODE_ALREADY_USED",
                    "El código externo de devolución ya fue utilizado.");

            if (metadata.ExternalReturnCode is not null)
            {
                if (metadata.ExternalReturnCode == returnCode && metadata.ReturnReason == reason)
                    return Map(metadata);
                throw new MarketplaceIntegrationException(409, "RETURN_ALREADY_REQUESTED",
                    "El pedido ya tiene una solicitud de devolución registrada.");
            }

            var order = metadata.Order;
            if (order.Estado != EstadoOrden.Entregada)
                throw new MarketplaceIntegrationException(409, "ORDER_NOT_DELIVERED",
                    "La devolución requiere que el ERP registre el pedido como entregado.");

            var priorRmas = await _catalog.DevolucionesRMA.AsNoTracking()
                .Where(x => x.OrdenId == order.Id && x.Estado != EstadoRMA.Rechazado)
                .AnyAsync(ct);
            if (priorRmas)
                throw new MarketplaceIntegrationException(409, "RETURN_ALREADY_EXISTS",
                    "El pedido ya contiene una devolución RMA anterior.");

            var rmaIds = new List<Guid>();
            foreach (var item in order.Items.GroupBy(x => x.ProductoId).Select(g => new
                     { ProductoId = g.Key, Cantidad = g.Sum(x => x.Cantidad) }))
            {
                var rma = await _rma.RegistrarSolicitudRmaAsync(
                    new RegistrarRmaRequest(order.Id, item.ProductoId, item.Cantidad, reason), ct);
                rmaIds.Add(rma.Id);
            }

            metadata.ExternalReturnCode = returnCode;
            metadata.ReturnReason = reason;
            metadata.ReturnRmaIdsJson = JsonSerializer.Serialize(rmaIds);
            metadata.ReturnStatus = "Solicitada";
            metadata.ReturnRequestedAtUtc = DateTime.UtcNow;
            await _finance.SaveChangesAsync(ct);
            return Map(metadata);
        }, cancellationToken);
    }

    public async Task<MarketplaceOrderResponse> VerifyTransferAsync(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        var code = NormalizeExternalCode(externalOrderCode);
        return await InSharedTransactionAsync(async ct =>
        {
            await LockAsync("ORDER:" + code, ct);
            var metadata = await FindTrackedAsync(code, ct);
            if (metadata is null)
                throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                    "No existe un pedido LOGIMKT con ese código.");
            if (metadata.RequestedPaymentMethod != "transferencia" ||
                metadata.Order.Estado != EstadoOrden.Pendiente)
                throw new MarketplaceIntegrationException(409, "TRANSFER_VERIFICATION_NOT_ALLOWED",
                    "Solo se puede verificar manualmente una transferencia de un pedido pendiente.");
            if (metadata.PaymentStatus == "Transferencia verificada manualmente")
                return Map(metadata);
            if (metadata.PaymentStatus != "Pendiente de verificación")
                throw new MarketplaceIntegrationException(409, "PAYMENT_STATE_CONFLICT",
                    "El estado de transferencia no permite esta verificación.");
            metadata.PaymentStatus = "Transferencia verificada manualmente";
            await _finance.SaveChangesAsync(ct);
            return Map(metadata);
        }, cancellationToken);
    }

    public async Task<MarketplaceOrderResponse> VerifyCreditAsync(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        var code = NormalizeExternalCode(externalOrderCode);
        return await InSharedTransactionAsync(async ct =>
        {
            await LockAsync("ORDER:" + code, ct);
            var metadata = await FindTrackedAsync(code, ct);
            if (metadata is null)
                throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                    "No existe un pedido LOGIMKT con ese código.");
            if (metadata.RequestedPaymentMethod != "credito_30" ||
                metadata.Order.Estado != EstadoOrden.Pendiente)
                throw new MarketplaceIntegrationException(409, "CREDIT_VERIFICATION_NOT_ALLOWED",
                    "Solo se puede autorizar crédito 30 días pendiente y aún no aprobado.");
            if (metadata.PaymentStatus == "Crédito 30 días autorizado manualmente")
                return Map(metadata);
            if (metadata.PaymentStatus != "Crédito 30 días pendiente de autorización")
                throw new MarketplaceIntegrationException(409, "PAYMENT_STATE_CONFLICT",
                    "El estado del crédito no permite esta autorización.");
            metadata.PaymentStatus = "Crédito 30 días autorizado manualmente";
            await _finance.SaveChangesAsync(ct);
            return Map(metadata);
        }, cancellationToken);
    }

    public async Task<MarketplaceOrderResponse> ConfirmCashOnDeliveryAsync(
        string externalOrderCode, CancellationToken cancellationToken)
    {
        var code = NormalizeExternalCode(externalOrderCode);
        return await InSharedTransactionAsync(async ct =>
        {
            await LockAsync("ORDER:" + code, ct);
            var metadata = await FindTrackedAsync(code, ct);
            if (metadata is null)
                throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                    "No existe un pedido LOGIMKT con ese código.");
            if (metadata.RequestedPaymentMethod != "contra_entrega" ||
                metadata.Order.Estado != EstadoOrden.Entregada)
                throw new MarketplaceIntegrationException(409, "CASH_ON_DELIVERY_NOT_ALLOWED",
                    "El cobro contra entrega solo puede confirmarse después de registrar la entrega.");
            if (metadata.PaymentStatus == "Cobro contra entrega verificado manualmente")
                return Map(metadata);
            if (metadata.PaymentStatus != "Cobro contra entrega pendiente")
                throw new MarketplaceIntegrationException(409, "PAYMENT_STATE_CONFLICT",
                    "El estado de cobro contra entrega no permite esta confirmación.");
            metadata.PaymentStatus = "Cobro contra entrega verificado manualmente";
            await _finance.SaveChangesAsync(ct);
            return Map(metadata);
        }, cancellationToken);
    }

    public async Task<MarketplaceOrderResponse> TransitionOrderAsync(
        string externalOrderCode, MarketplaceOrderTransitionRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeExternalCode(externalOrderCode);
        if (!Enum.TryParse<EstadoOrden>(request.NewStatus, true, out var target))
            throw new MarketplaceIntegrationException(400, "INVALID_ORDER_STATUS",
                "El estado solicitado no pertenece al flujo OMS del ERP.");

        return await InSharedTransactionAsync(async ct =>
        {
            await LockAsync("ORDER:" + code, ct);
            var metadata = await FindTrackedAsync(code, ct);
            if (metadata is null)
                throw new MarketplaceIntegrationException(404, "ORDER_NOT_FOUND",
                    "No existe un pedido LOGIMKT con ese código.");
            var order = metadata.Order;
            if (order.Estado == target)
                return Map(metadata);

            var allowed = (order.Estado, target) switch
            {
                (EstadoOrden.Pendiente, EstadoOrden.Aprobada) => true,
                (EstadoOrden.Pendiente, EstadoOrden.Cancelada) => true,
                (EstadoOrden.Aprobada, EstadoOrden.EnProceso) => true,
                (EstadoOrden.Aprobada, EstadoOrden.Cancelada) => true,
                (EstadoOrden.EnProceso, EstadoOrden.Enviada) => true,
                (EstadoOrden.EnProceso, EstadoOrden.Cancelada) => true,
                (EstadoOrden.Enviada, EstadoOrden.Entregada) => true,
                _ => false
            };
            if (!allowed)
                throw new MarketplaceIntegrationException(409, "INVALID_OMS_TRANSITION",
                    $"No se permite pasar de {order.Estado} a {target}.");

            if (target == EstadoOrden.Aprobada)
            {
                if (metadata.RequestedPaymentMethod == "tarjeta")
                    throw new MarketplaceIntegrationException(409, "PAYMENT_PROVIDER_DISABLED",
                        "No se puede aprobar una tarjeta mientras no exista una pasarela de pago habilitada.");
                if (metadata.RequestedPaymentMethod == "transferencia" &&
                    metadata.PaymentStatus != "Transferencia verificada manualmente")
                    throw new MarketplaceIntegrationException(409, "TRANSFER_NOT_VERIFIED",
                        "Un administrador debe verificar la transferencia antes de aprobar el pedido.");
                if (metadata.RequestedPaymentMethod == "credito_30" &&
                    metadata.PaymentStatus != "Crédito 30 días autorizado manualmente")
                    throw new MarketplaceIntegrationException(409, "CREDIT_NOT_AUTHORIZED",
                        "Un administrador debe autorizar el crédito de 30 días antes de aprobar el pedido.");

                await ConfirmReservationsAsync(order.Id, ct);
                order.FechaAprobacion = DateTime.UtcNow;
            }
            else if (target == EstadoOrden.Cancelada)
            {
                await EnsureNoPaymentArtifactsAsync(metadata, ct);
                await ReleaseReservationsAsync(order.Id, ct);
                order.FechaCancelacion = DateTime.UtcNow;
                metadata.PaymentStatus = "No cobrado - pedido cancelado";
            }
            else if (target == EstadoOrden.Enviada)
            {
                var tracking = request.TrackingCode?.Trim();
                if (string.IsNullOrWhiteSpace(tracking) || tracking.Length > 100)
                    throw new MarketplaceIntegrationException(400, "TRACKING_REQUIRED",
                        "Se requiere una guía de envío válida para despachar el pedido.");
                await _stock.ConsumirReservasOrdenAsync(order.Id);
                order.GuiaEnvio = tracking;
                order.FechaEnvio = DateTime.UtcNow;
            }
            else if (target == EstadoOrden.Entregada)
            {
                order.FechaEntregaReal = DateTime.UtcNow;
            }

            // These marketplace orders are proformas. Operational transitions
            // intentionally do not create sales journal entries or payment records.
            order.Estado = target;
            await _finance.SaveChangesAsync(ct);
            return Map(metadata);
        }, cancellationToken);
    }

    private async Task<MarketplaceOrderMetadata?> FindTrackedAsync(string code, CancellationToken ct) =>
        await _finance.MarketplaceOrders
            .Include(m => m.Order).ThenInclude(o => o.Items)
            .SingleOrDefaultAsync(m => m.ExternalOrderCode == code, ct);

    private async Task EnsureNoPaymentArtifactsAsync(MarketplaceOrderMetadata metadata, CancellationToken ct)
    {
        var orderId = metadata.OrderId;
        if (metadata.PaymentStatus is "Transferencia verificada manualmente" or
            "Crédito 30 días autorizado manualmente" or
            "Cobro contra entrega verificado manualmente" ||
            await _finance.IntentosPago.AnyAsync(x => x.OrdenId == orderId, ct) ||
            await _finance.SplitsPago.AnyAsync(x => x.OrdenId == orderId, ct) ||
            await _finance.ReembolsosPago.AnyAsync(x => x.OrdenId == orderId, ct))
            throw new MarketplaceIntegrationException(409, "PAYMENT_RECONCILIATION_REQUIRED",
                "La orden tiene un pago o una autorización financiera confirmada y requiere conciliación administrativa.");
    }

    private async Task ReleaseReservationsAsync(Guid orderId, CancellationToken ct)
    {
        var reservations = await _catalog.ReservasStock
            .Where(x => x.OrdenId == orderId)
            .OrderBy(x => x.ProductoId)
            .ToListAsync(ct);
        if (reservations.Count == 0)
            throw new MarketplaceIntegrationException(409, "RESERVATIONS_MISSING",
                "La orden no tiene reservas de inventario que puedan liberarse.");
        foreach (var reservation in reservations)
        {
            if (reservation.Estado is EstadoReserva.Pendiente or EstadoReserva.Confirmada)
                await _stock.LiberarStockAsync(reservation.Id);
            else if (reservation.Estado is not (EstadoReserva.Liberada or EstadoReserva.Expirada))
                throw new MarketplaceIntegrationException(409, "RESERVATION_STATE_CONFLICT",
                    "Una reserva ya fue consumida o finalizada; no se puede liberar automáticamente.");
        }
    }

    private async Task ConfirmReservationsAsync(Guid orderId, CancellationToken ct)
    {
        var reservations = await _catalog.ReservasStock
            .Where(x => x.OrdenId == orderId)
            .OrderBy(x => x.ProductoId)
            .ToListAsync(ct);
        if (reservations.Count == 0)
            throw new MarketplaceIntegrationException(409, "RESERVATIONS_MISSING",
                "La orden no tiene reservas de inventario en el ERP.");
        foreach (var reservation in reservations)
        {
            if (reservation.Estado == EstadoReserva.Pendiente)
                await _stock.ConfirmarReservaAsync(reservation.Id);
            else if (reservation.Estado != EstadoReserva.Confirmada)
                throw new MarketplaceIntegrationException(409, "RESERVATION_EXPIRED",
                    "Una o más reservas expiraron o cambiaron; no se puede aprobar el pedido.");
        }
    }

    private decimal CalculateShipping(TipoCliente type, decimal subtotal)
    {
        var shipping = type == TipoCliente.B2B
            ? _configuration.GetValue<decimal?>("Marketplace:ShippingB2B") ?? 0m
            : subtotal >= (_configuration.GetValue<decimal?>("Marketplace:FreeShippingFromB2C") ?? 50m)
                ? 0m
                : _configuration.GetValue<decimal?>("Marketplace:ShippingB2C") ?? 3.50m;
        if (shipping < 0m || shipping > MaxMoney || decimal.Round(shipping, 2) != shipping)
            throw new MarketplaceIntegrationException(503, "SHIPPING_CONFIGURATION_INVALID",
                "La configuración de envío del ERP no es válida.");
        return shipping;
    }

    private async Task<T> InSharedTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var financeConnection = _finance.Database.GetDbConnection();
        var originalCatalogConnection = _catalog.Database.GetDbConnection();
        var originalCoreConnection = _core.Database.GetDbConnection();

        _catalog.Database.SetDbConnection(financeConnection, contextOwnsConnection: false);
        _core.Database.SetDbConnection(financeConnection, contextOwnsConnection: false);
        try
        {
            await using var transaction = await _finance.Database.BeginTransactionAsync(ct);
            await using var catalogTransaction =
                await _catalog.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
            await using var coreTransaction =
                await _core.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);

            var result = await operation(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            _finance.ChangeTracker.Clear();
            _catalog.ChangeTracker.Clear();
            _core.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            _catalog.ChangeTracker.Clear();
            _core.ChangeTracker.Clear();
            _catalog.Database.SetDbConnection(originalCatalogConnection, contextOwnsConnection: true);
            _core.Database.SetDbConnection(originalCoreConnection, contextOwnsConnection: true);
        }
    }

    private Task LockAsync(string value, CancellationToken ct)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("LOGIMKT:" + value));
        var key = BitConverter.ToInt64(bytes, 0);
        return _finance.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({key})", ct);
    }

    private static NormalizedOrder Normalize(MarketplaceOrderRequest request)
    {
        if (request is null)
            throw new MarketplaceIntegrationException(400, "INVALID_REQUEST", "El cuerpo del pedido es obligatorio.");

        var code = NormalizeExternalCode(request.ExternalOrderCode);
        if (!Enum.TryParse<TipoCliente>(request.CustomerType, true, out var customerType))
            throw new MarketplaceIntegrationException(400, "INVALID_CUSTOMER_TYPE", "CustomerType debe ser B2B o B2C.");

        var name = NormalizeRequired(request.CustomerName, 300, "El nombre del cliente es obligatorio.");
        var email = NormalizeRequired(request.CustomerEmail, 254, "El correo del cliente es obligatorio.").ToLowerInvariant();
        try
        {
            var parsed = new MailAddress(email);
            if (!string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch
        {
            throw new MarketplaceIntegrationException(400, "INVALID_EMAIL", "El correo del cliente no es válido.");
        }

        var address = NormalizeRequired(request.DeliveryAddress, 500, "La dirección de entrega es obligatoria.");
        var phone = NormalizeOptional(request.CustomerPhone, 30);
        var company = NormalizeOptional(request.Company, 250);
        var taxId = NormalizeOptional(request.TaxId, 50);
        var city = NormalizeOptional(request.City, 100);
        if (customerType == TipoCliente.B2B &&
            (string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(taxId)))
            throw new MarketplaceIntegrationException(422, "B2B_FISCAL_DATA_REQUIRED",
                "Las compras B2B requieren empresa e identificación fiscal.");

        var payment = NormalizeRequired(request.RequestedPaymentMethod, 50, "El método de pago es obligatorio.")
            .ToLowerInvariant();
        var allowedPayments = customerType == TipoCliente.B2B
            ? new[] { "credito_30", "transferencia", "tarjeta" }
            : new[] { "tarjeta", "transferencia", "contra_entrega" };
        if (!allowedPayments.Contains(payment, StringComparer.Ordinal))
            throw new MarketplaceIntegrationException(422, "INVALID_PAYMENT_METHOD",
                "El método de pago no es compatible con el tipo de cliente.");

        if (request.Items is null || request.Items.Count is < 1 or > 20 ||
            request.Items.Any(x => x is null))
            throw new MarketplaceIntegrationException(422, "INVALID_CART_SIZE",
                "El carrito debe contener entre 1 y 20 productos y no incluir líneas vacías.");

        var items = request.Items.Select(x => new NormalizedItem(
                NormalizeRequired(x.Sku, 50, "Cada línea requiere un SKU.").ToUpperInvariant(),
                x.Quantity))
            .OrderBy(x => x.Sku, StringComparer.Ordinal)
            .ToList();
        if (items.Any(x => x.Quantity is < 1 or > 9999) ||
            items.Select(x => x.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count)
            throw new MarketplaceIntegrationException(422, "INVALID_CART_LINES",
                "Las cantidades deben ser positivas y cada SKU debe aparecer una sola vez.");

        return new NormalizedOrder(code, customerType, name, email, phone, company, taxId,
            address, city, payment, items);
    }

    private static string NormalizeExternalCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Regex.IsMatch(code, @"\A[A-Z0-9_-]{4,50}\z", RegexOptions.CultureInvariant))
            throw new MarketplaceIntegrationException(400, "INVALID_EXTERNAL_ORDER_CODE",
                "El código externo del pedido no tiene un formato válido.");
        return code;
    }

    private static string NormalizeReturnCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Regex.IsMatch(code, @"\A[A-Z0-9_-]{4,50}\z", RegexOptions.CultureInvariant))
            throw new MarketplaceIntegrationException(400, "INVALID_EXTERNAL_RETURN_CODE",
                "El código externo de devolución no tiene un formato válido.");
        return code;
    }

    private static string NormalizeRequired(string? value, int maxLength, string message)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maxLength)
            throw new MarketplaceIntegrationException(400, "INVALID_FIELD", message);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        if (normalized.Length > maxLength)
            throw new MarketplaceIntegrationException(400, "FIELD_TOO_LONG",
                "Uno de los campos opcionales excede la longitud permitida.");
        return normalized;
    }

    private static string HashPayload(NormalizedOrder request)
    {
        var canonical = new
        {
            request.ExternalOrderCode, CustomerType = request.CustomerType.ToString(),
            request.CustomerName, request.CustomerEmail, request.CustomerPhone,
            request.Company, request.TaxId, request.DeliveryAddress, request.City,
            request.RequestedPaymentMethod,
            Items = request.Items.Select(x => new { x.Sku, x.Quantity })
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }

    private static MarketplaceOrderResponse Map(MarketplaceOrderMetadata metadata)
    {
        var order = metadata.Order;
        var rmaIds = string.IsNullOrWhiteSpace(metadata.ReturnRmaIdsJson)
            ? Array.Empty<Guid>()
            : JsonSerializer.Deserialize<Guid[]>(metadata.ReturnRmaIdsJson) ?? Array.Empty<Guid>();
        return new MarketplaceOrderResponse(
            order.Id, order.NumeroOrden, metadata.ExternalOrderCode,
            order.Estado.ToString(), order.TipoCliente.ToString(),
            order.SubTotal, order.ImpuestoIva, order.CostoEnvio, order.MontoTotal,
            metadata.RequestedPaymentMethod, metadata.PaymentStatus,
            metadata.CreatedAtUtc, order.FechaEntregaEstimada, order.FechaEntregaReal,
            order.Transportista, order.GuiaEnvio, metadata.ReturnStatus, rmaIds,
            order.Items.OrderBy(x => x.SkuReferencia, StringComparer.Ordinal)
                .Select(x => new MarketplaceOrderLineResponse(
                    x.SkuReferencia, x.NombreProducto, x.Cantidad, x.PrecioUnitario,
                    x.Cantidad * x.PrecioUnitario)).ToArray());
    }

    private sealed record NormalizedItem(string Sku, int Quantity);
    private sealed record NormalizedOrder(
        string ExternalOrderCode, TipoCliente CustomerType, string CustomerName,
        string CustomerEmail, string? CustomerPhone, string? Company, string? TaxId,
        string DeliveryAddress, string? City, string RequestedPaymentMethod,
        IReadOnlyList<NormalizedItem> Items);
}
