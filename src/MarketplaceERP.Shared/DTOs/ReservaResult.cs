namespace MarketplaceERP.Shared.DTOs;

/// <summary>
/// Resultado de reservar stock para una orden.
/// </summary>
public record ReservaResult(
    Guid ReservaId,
    bool Exitoso,
    string? MensajeError
);
