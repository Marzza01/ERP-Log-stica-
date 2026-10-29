namespace MarketplaceERP.Finance.Entities;

/// <summary>Estado observado del intento. INCIERTO no significa pago rechazado.</summary>
public enum EstadoIntentoPago
{
    Preparado = 0,
    Enviado = 1,
    Rechazado = 2,
    Aprobado = 3,
    Incierto = 4,
    RequiereConciliacion = 5,
    Completado = 6
}

/// <summary>
/// Registro durable por orden: se inserta antes de invocar a cualquier proveedor.
/// No almacenar datos de tarjetas, tokens, secretos ni errores sin sanitizar.
/// </summary>
public sealed class IntentoPago
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrdenId { get; set; }
    public string ReferenciaOperacion { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string Moneda { get; set; } = "USD";
    public EstadoIntentoPago Estado { get; set; } = EstadoIntentoPago.Preparado;
    public string? TransaccionId { get; set; }
    public DateTime CreadoUtc { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoUtc { get; set; } = DateTime.UtcNow;
}
