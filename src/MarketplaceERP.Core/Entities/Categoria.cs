/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Core.Entities;

/// <summary>
/// Categoría de producto en el marketplace.
/// Esquema DB: core.categorias
/// </summary>
public class Categoria
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public Guid? CategoriaPadreId { get; set; }

    // Auto-referencia para jerarquía de categorías
    public Categoria? CategoriaPadre { get; set; }
    public ICollection<Categoria> SubCategorias { get; set; } = new List<Categoria>();
}
