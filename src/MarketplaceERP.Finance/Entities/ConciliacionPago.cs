namespace MarketplaceERP.Finance.Entities;

/// <summary>Auditoría append-only de la consulta al proveedor. No cambia el estado comercial.</summary>
public sealed class ConciliacionPago
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntentoPagoId { get; set; }
    public Guid OrdenId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? TransaccionId { get; set; }
    public DateTime ConsultadoUtc { get; set; } = DateTime.UtcNow;
}
