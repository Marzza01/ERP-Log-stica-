namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Motivos permitidos en la compensación supervisada de QA antes del envío.
/// No reflejan respuesta bancaria ni decisión de proveedor.
/// </summary>
public enum MotivoCancelacionPreEnvioQa
{
    FallaTrasFinance = 1,
    FallaTrasReservas = 2,
    FallaTrasMapa = 3,
    VerificacionManualQa = 4
}

/// <summary>
/// Auditoría persistida EN EL MISMO COMMIT que la cancelación de una orden
/// QA y la liberación de reservas. No es una conciliación bancaria.
/// Se aplica únicamente en BD ci_checkout_http_* de PostgreSQL localhost.
/// </summary>
public sealed class CancelacionPreEnvioQa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrdenId { get; set; }
    public Guid IntentoPagoId { get; set; }
    public Guid OperadorQaId { get; set; }
    public MotivoCancelacionPreEnvioQa Motivo { get; set; }
    public int ReservasLiberadas { get; set; }
    public DateTime RegistradoUtc { get; set; } = DateTime.UtcNow;
}
