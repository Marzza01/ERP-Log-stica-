namespace MarketplaceERP.API.Contracts;

// Vista del vendedor: deliberadamente no expone datos personales del comprador,
// intentos de pago, liquidaciones, direcciones ni entidad EF completa.
public sealed record OrdenVendedorResumenResponse(
    Guid Id,
    string NumeroOrden,
    string Estado,
    string TipoCliente,
    decimal MontoTotal,
    string Moneda,
    DateTime FechaCreacion,
    int TotalItems);

public sealed record ItemOrdenVendedorResponse(
    Guid ProductoId,
    string Sku,
    string Nombre,
    int Cantidad,
    decimal PrecioUnitario);

public sealed record OrdenVendedorDetalleResponse(
    Guid Id,
    string NumeroOrden,
    string Estado,
    string TipoCliente,
    decimal MontoTotal,
    string Moneda,
    DateTime FechaCreacion,
    IReadOnlyList<ItemOrdenVendedorResponse> Items);
