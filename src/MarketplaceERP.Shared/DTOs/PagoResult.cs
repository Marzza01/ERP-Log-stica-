/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Shared.DTOs;

/// <summary>
/// Resultado de procesar un pago a través de la pasarela de pagos.
/// </summary>
public record PagoResult(
    bool Exitoso,
    string? TransaccionId,
    string? MensajeError
);
