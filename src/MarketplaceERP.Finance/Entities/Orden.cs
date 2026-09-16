namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Estados posibles de una orden de compra.
/// </summary>
public enum EstadoOrden
{
    Creada = 0,
    StockReservado = 1,
    PagoProcesado = 2,
    Completada = 3,
    Cancelada = 4,
    PagoFallido = 5
}

/// <summary>
/// Orden de compra generada por un comprador en el marketplace.
/// Esquema DB: finance.ordenes
/// Nota: CompradorId y VendedorId son referencias cruzadas (Guid, sin FK entre esquemas).
/// </summary>
public class Orden
{
    public Guid Id { get; set; }

    /// <summary>
    /// Referencia al comprador (cross-schema, sin FK en DB).
    /// </summary>
    public Guid CompradorId { get; set; }

    /// <summary>
    /// Referencia al vendedor principal de la orden (cross-schema, sin FK en DB).
    /// </summary>
    public Guid VendedorId { get; set; }

    public decimal MontoTotal { get; set; }
    public string Moneda { get; set; } = "USD";
    public EstadoOrden Estado { get; set; } = EstadoOrden.Creada;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCompletada { get; set; }

    // Navegación
    public ICollection<ItemOrden> Items { get; set; } = new List<ItemOrden>();
    public SplitPago? SplitPago { get; set; }
}
