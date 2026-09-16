namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Producto publicado por un vendedor en el marketplace.
/// Esquema DB: catalog.productos
/// Nota: VendedorId y CategoriaId son referencias cruzadas (solo Guid, sin FK entre esquemas).
/// </summary>
public class Producto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal PrecioBase { get; set; }
    public string Moneda { get; set; } = "USD";

    /// <summary>
    /// Referencia al vendedor dueño del producto (Guid cross-schema, sin FK en DB).
    /// </summary>
    public Guid VendedorId { get; set; }

    /// <summary>
    /// Referencia a la categoría del producto (Guid cross-schema, sin FK en DB).
    /// </summary>
    public Guid CategoriaId { get; set; }

    public bool EstaPublicado { get; set; } = false;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaPublicacion { get; set; }

    // Navegación dentro del esquema catalog
    public StockFisico? Stock { get; set; }
    public ICollection<ReservaStock> Reservas { get; set; } = new List<ReservaStock>();
}
