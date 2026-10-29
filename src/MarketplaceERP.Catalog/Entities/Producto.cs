/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Producto del catálogo maestro de LOGIMKT S.A. de C.V.
/// Esquema DB: catalog.productos
/// Incluye los 16 campos obligatorios de datos maestros de logística digital.
/// </summary>
public class Producto
{
    public Guid Id { get; set; }

    // 1. SKU (Código único alfanumérico)
    public string Sku { get; set; } = string.Empty;

    // 2. Nombre del Producto
    public string Nombre { get; set; } = string.Empty;

    // 3. Categoría (Nombre oficial de la categoría)
    public string CategoriaNombre { get; set; } = string.Empty;

    // 4. Subcategoría
    public string Subcategoria { get; set; } = string.Empty;

    // 5. Precio B2C ($) (Consumidor final)
    public decimal PrecioB2C { get; set; }

    // 6. Precio B2B ($) (Mayoreo / distribuidores)
    public decimal PrecioB2B { get; set; }

    // PrecioBase para retrocompatibilidad
    public decimal PrecioBase
    {
        get => PrecioB2C;
        set => PrecioB2C = value;
    }

    public string Moneda { get; set; } = "USD";

    // 7. Stock Inicial (Mapeado o inicializado para trazabilidad)
    public int StockInicial { get; set; }

    // 8. Stock Mínimo (Punto de reorden para alertas logísticas)
    public int StockMinimo { get; set; }

    // 9. Proveedor (Razón social)
    public string ProveedorNombre { get; set; } = string.Empty;

    // 10. País de Origen
    public string PaisOrigen { get; set; } = "El Salvador";

    // 11. Peso (kg)
    public decimal PesoKg { get; set; }

    // 12. Dimensiones (cm) (Largo x Ancho x Alto)
    public string DimensionesCm { get; set; } = string.Empty;

    // 13. Unidad de Medida (Unidad, Caja, Pack, etc.)
    public string UnidadMedida { get; set; } = "Unidad";

    // 14. Estado del Producto (Activo / Inactivo)
    public string EstadoProducto { get; set; } = "Activo";

    // 15. Código EAN (EAN-13 para escaneo óptico)
    public string CodigoEAN { get; set; } = string.Empty;

    // 16. Descripción Corta / Ficha técnica
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>
    /// Referencia al vendedor dueño del producto (Guid cross-schema, sin FK en DB).
    /// </summary>
    public Guid VendedorId { get; set; }

    /// <summary>
    /// Referencia a la categoría del producto (Guid cross-schema, sin FK en DB).
    /// </summary>
    public Guid CategoriaId { get; set; }

    public bool EstaPublicado { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaPublicacion { get; set; }

    // Navegación dentro del esquema catalog
    // Un producto puede tener varios saldos, uno por ubicación WMS.
    [JsonIgnore]
    public ICollection<StockFisico> Stocks { get; set; } = new List<StockFisico>();

    /// <summary>
    /// Compatibilidad temporal con el contrato frontend previo a Fase 4A.
    /// Devuelve el primer saldo cargado. No representa el total multiubicación.
    /// Los endpoints operativos migrarán a DTOs agregados en las siguientes fases.
    /// </summary>
    [NotMapped]
    public StockFisico? Stock
    {
        get => Stocks.FirstOrDefault();
        set
        {
            Stocks.Clear();
            if (value is not null)
            {
                Stocks.Add(value);
            }
        }
    }

    public ICollection<ReservaStock> Reservas { get; set; } = new List<ReservaStock>();
    public ICollection<DevolucionRMA> Devoluciones { get; set; } = new List<DevolucionRMA>();
    public ICollection<MovimientoMWIP> MovimientosMWIP { get; set; } = new List<MovimientoMWIP>();
    public ICollection<MovimientoInventario> MovimientosInventario { get; set; } = new List<MovimientoInventario>();
}
