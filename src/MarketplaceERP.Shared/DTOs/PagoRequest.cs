/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Shared.DTOs;

/// <summary>
/// Request para procesar un pago a través de la pasarela de pagos.
/// </summary>
public record PagoRequest(
    Guid OrdenId,
    Guid CompradorId,
    decimal MontoTotal,
    string Moneda
);
