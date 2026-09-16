namespace MarketplaceERP.Shared.DTOs;

/// <summary>
/// Resultado de procesar un pago a través de la pasarela de pagos.
/// </summary>
public record PagoResult(
    bool Exitoso,
    string? TransaccionId,
    string? MensajeError
);
