namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Prototipo aislado P0 de relación durable 1 intento de pago → N reservas.
/// La reserva física vive en Catalog; aquí solo se guarda el ID externo.
/// Procedencia LegacySinVerificar NO equivale a reserva físicamente conciliada.
/// </summary>
public sealed class IntentoReservaPago
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntentoPagoId { get; set; }
    public Guid ReservaId { get; set; }
    public Guid ProductoId { get; set; }
    public int Cantidad { get; set; }
    public string Procedencia { get; set; } = "MultiSkuQa";
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
}
