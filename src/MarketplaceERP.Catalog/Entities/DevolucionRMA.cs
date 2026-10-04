/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Clasificación técnica para resolución de devoluciones RMA (PDF 1 - Grupo A).
/// </summary>
public enum ClasificacionRMA
{
    Pendiente = 0,
    ReStock = 1,     // Producto en perfecto estado: reingresa al inventario disponible
    MermaScrap = 2,  // Producto dañado irreparable: se da de baja
    Remate = 3       // Producto con detalle estético/caja dañada: se vende con descuento especial
}

/// <summary>
/// Estado del flujo de inspección técnica.
/// </summary>
public enum EstadoRMA
{
    Solicitado = 0,
    RecibidoEnBodega = 1,
    EnInspeccion = 2,
    Dictaminado = 3,
    Completado = 4,
    Rechazado = 5
}

/// <summary>
/// Entidad de Logística Inversa y Devoluciones (RMA).
/// Esquema DB: catalog.devoluciones_rma
/// </summary>
public class DevolucionRMA
{
    public Guid Id { get; set; }
    public string NumeroRMA { get; set; } = string.Empty;

    public Guid OrdenId { get; set; }
    public Guid ProductoId { get; set; }
    public int Cantidad { get; set; }

    public string MotivoCliente { get; set; } = string.Empty;
    public EstadoRMA Estado { get; set; } = EstadoRMA.Solicitado;
    public ClasificacionRMA Clasificacion { get; set; } = ClasificacionRMA.Pendiente;

    public string? InspectorTecnico { get; set; }
    public string? DictamenTecnico { get; set; }
    public decimal PorcentajeRemateDescuento { get; set; } = 0m;

    public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;
    public DateTime? FechaInspeccion { get; set; }

    // Navegación
    public Producto? Producto { get; set; }
}
