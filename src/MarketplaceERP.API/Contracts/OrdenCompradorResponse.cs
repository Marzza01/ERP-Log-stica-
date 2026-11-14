namespace MarketplaceERP.API.Contracts;

// Explicit buyer view: no payment split, transaction IDs, checkout keys or navigation objects.
public record OrdenCompradorResponse(
    Guid Id, string NumeroOrden, string Estado, string TipoCliente,
    decimal SubTotal, decimal ImpuestoIva, decimal CostoEnvio, decimal MontoTotal, string Moneda,
    string TipoComprobante, string NumeroComprobanteFiscal, string CondicionPago,
    string Transportista, string GuiaEnvio, string ZonaEnvio, string DireccionEntrega,
    DateTime FechaCreacion, DateTime? FechaAprobacion, DateTime? FechaEnvio,
    DateTime? FechaEntregaEstimada, DateTime? FechaEntregaReal, DateTime? FechaCancelacion,
    IReadOnlyList<ItemOrdenCompradorResponse> Items);

public record ItemOrdenCompradorResponse(
    Guid ProductoId, string SkuReferencia, string NombreProducto,
    int Cantidad, decimal PrecioUnitario, decimal Subtotal);
