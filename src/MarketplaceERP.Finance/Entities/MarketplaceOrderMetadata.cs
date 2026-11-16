namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Snapshot of the customer and payment intent received from LOGIMKT.
/// It is deliberately separate from the ERP order: the marketplace never
/// supplies authoritative prices, tax, inventory, or payment confirmations.
/// </summary>
public sealed class MarketplaceOrderMetadata
{
    public Guid OrderId { get; set; }

    public string ExternalOrderCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? Company { get; set; }
    public string? TaxId { get; set; }

    public string RequestedPaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = "Pendiente de cobro";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // LOGIMKT supports a single full-order return request. Each sold line is
    // represented by an ERP RMA; this stores their IDs for idempotent retries.
    public string? ExternalReturnCode { get; set; }
    public string? ReturnReason { get; set; }
    public string? ReturnRmaIdsJson { get; set; }
    public string? ReturnStatus { get; set; }
    public DateTime? ReturnRequestedAtUtc { get; set; }

    public Orden Order { get; set; } = null!;
}
