/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Shared.DTOs;

/// <summary>
/// Representa un ítem dentro de un pedido u orden de compra.
/// </summary>
public record ItemPedido(
    Guid ProductoId,
    int Cantidad,
    decimal PrecioUnitario
);
