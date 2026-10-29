namespace MarketplaceERP.Finance.Entities;

/// <summary>Un único reembolso íntegro por orden. Los estados inciertos nunca reenvían el cargo inverso.</summary>
public enum EstadoReembolsoPago
{
    Preparado,
    Enviado,
    Pendiente,
    Incierto,
    Rechazado,
    Confirmado,
    Finalizado
}

public sealed class ReembolsoPago
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrdenId { get; set; }
    public Guid ReservaId { get; set; }
    public string ClaveIdempotencia { get; set; } = string.Empty;
    public string TransaccionId { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string Moneda { get; set; } = "USD";
    public EstadoReembolsoPago Estado { get; set; } = EstadoReembolsoPago.Preparado;
    public string? ReembolsoProveedorId { get; set; }
    // CAS optimista: solamente una instancia puede pasar Preparado -> Enviado.
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}
