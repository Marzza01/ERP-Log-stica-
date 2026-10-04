/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Shared.DTOs;

/// <summary>
/// Resultado de reservar stock para una orden.
/// </summary>
public record ReservaResult(
    Guid ReservaId,
    bool Exitoso,
    string? MensajeError
);
