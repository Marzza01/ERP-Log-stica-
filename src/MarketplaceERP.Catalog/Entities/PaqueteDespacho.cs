/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Text.Json.Serialization;

namespace MarketplaceERP.Catalog.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoPacking
{
    Pendiente = 0,
    Empacando = 1,
    ListoParaDespacho = 2
}

public class PaqueteDespacho
{
    public Guid Id { get; set; }
    public string NumeroGuia { get; set; } = string.Empty;
    public Guid OrdenId { get; set; }
    public string NumeroOrden { get; set; } = string.Empty;
    public EstadoPacking Estado { get; set; } = EstadoPacking.Pendiente;

    // Cubicaje y Pesaje
    public decimal PesoRealKg { get; set; }
    public decimal LargoCm { get; set; }
    public decimal AnchoCm { get; set; }
    public decimal AltoCm { get; set; }
    public decimal VolumenM3 => Math.Round((LargoCm * AnchoCm * AltoCm) / 1_000_000m, 4);

    // Operador Logístico y Destino
    public string Transportista { get; set; } = "LOGIMKT Express";
    public string DepartamentoDestino { get; set; } = "San Salvador";
    public string MunicipioDestino { get; set; } = "San Salvador Centro";
    public string DireccionEntrega { get; set; } = string.Empty;
    public string ContactoReceptor { get; set; } = string.Empty;
    public string TelefonoReceptor { get; set; } = string.Empty;

    public DateTime FechaEmpaque { get; set; } = DateTime.UtcNow;
    public DateTime? FechaDespacho { get; set; }
}
