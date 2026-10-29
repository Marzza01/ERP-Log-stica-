/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Text.Json.Serialization;

namespace MarketplaceERP.Catalog.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoAjusteInventario
{
    Positivo = 0,
    Negativo = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoAjusteInventario
{
    PendienteAprobacion = 0,
    Aprobado = 1,
    Rechazado = 2,
    Aplicado = 3,
    Cancelado = 4
}

/// <summary>
/// Solicitud auditable de corrección de inventario. El stock solo cambia al aplicar un ajuste aprobado.
/// </summary>
public class AjusteInventario
{
    public Guid Id { get; set; }
    public Guid StockFisicoId { get; set; }
    public Guid ProductoId { get; set; }
    public TipoAjusteInventario Tipo { get; set; }
    public int Cantidad { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public string? Referencia { get; set; }
    public EstadoAjusteInventario Estado { get; set; } = EstadoAjusteInventario.PendienteAprobacion;

    public string? CreadoPor { get; set; }
    public string? AprobadoPor { get; set; }
    public string? AplicadoPor { get; set; }
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaDecision { get; set; }
    public DateTime? FechaAplicacion { get; set; }

    public StockFisico StockFisico { get; set; } = null!;
    public Producto Producto { get; set; } = null!;
}
