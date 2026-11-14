namespace MarketplaceERP.API.Contracts;

/// <summary>
/// DTO público de producto para Comprador.
/// No expone ProveedorNombre, PrecioB2B, VendedorId ni cantidades de stock.
/// </summary>
public record ProductoCompradorResponse(
    Guid Id,
    string Sku,
    string Nombre,
    string Descripcion,
    string CategoriaNombre,
    string Subcategoria,
    decimal PrecioB2C,
    string Moneda,
    string UnidadMedida,
    string EstadoProducto,
    bool EstaPublicado,
    string? ImagenUrl,
    bool Disponibilidad);
