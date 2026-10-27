/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Text.Json.Serialization;

namespace MarketplaceERP.Catalog.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoConteoCiclico
{
    Planificado = 0,
    EnConteo = 1,
    RequiereReconteo = 2,
    Completado = 3,
    CompletadoConAjuste = 4,
    Cancelado = 5
}

/// <summary>
/// Conteo cíclico sobre un saldo físico concreto. El conteo no modifica stock directamente;
/// una diferencia confirmada en reconteo genera un AjusteInventario pendiente de aprobación.
/// </summary>
public class ConteoCiclico
{
    public Guid Id { get; set; }
    public Guid StockFisicoId { get; set; }
    public Guid ProductoId { get; set; }
    public EstadoConteoCiclico Estado { get; set; } = EstadoConteoCiclico.Planificado;

    /// <summary>Fotografía del stock físico al iniciar el conteo.</summary>
    public int? CantidadSistema { get; set; }
    public int? PrimerConteo { get; set; }
    public int? SegundoConteo { get; set; }
    public Guid? AjusteInventarioId { get; set; }

    public string? Observaciones { get; set; }
    public string? CreadoPor { get; set; }
    public string? IniciadoPor { get; set; }
    public string? ContadoPor { get; set; }
    public string? RecontadoPor { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaPrimerConteo { get; set; }
    public DateTime? FechaReconteo { get; set; }
    public DateTime? FechaFinalizacion { get; set; }

    public StockFisico StockFisico { get; set; } = null!;
    public Producto Producto { get; set; } = null!;
    public AjusteInventario? AjusteInventario { get; set; }
}
