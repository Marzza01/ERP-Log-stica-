/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

public enum EstadoPicking
{
    Pendiente = 0,
    EnProceso = 1,
    Completado = 2,
    Cancelado = 3
}

public class PickList
{
    public Guid Id { get; set; }
    public string NumeroPickList { get; set; } = string.Empty;
    public Guid OrdenId { get; set; }
    public string NumeroOrden { get; set; } = string.Empty;
    public EstadoPicking Estado { get; set; } = EstadoPicking.Pendiente;
    public string OperadorAsignado { get; set; } = "Operador Bodega 1";
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }

    public ICollection<ItemPickList> Items { get; set; } = new List<ItemPickList>();
}

public class ItemPickList
{
    public Guid Id { get; set; }
    public Guid PickListId { get; set; }
    public Guid ProductoId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string NombreProducto { get; set; } = string.Empty;
    public int CantidadSolicitada { get; set; }
    public int CantidadRecogida { get; set; }

    // Ubicación WMS para ruta óptima
    public string Pasillo { get; set; } = string.Empty;
    public string Rack { get; set; } = string.Empty;
    public int Nivel { get; set; }
    public string Posicion { get; set; } = string.Empty;
    public string UbicacionCodigo { get; set; } = string.Empty;

    public bool Recogido => CantidadRecogida >= CantidadSolicitada;

    public PickList PickList { get; set; } = null!;
}
