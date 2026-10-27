/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public sealed record ConteoCiclicoDto(
    Guid Id,
    Guid StockFisicoId,
    Guid ProductoId,
    string Sku,
    string ProductoNombre,
    string UbicacionCodigo,
    EstadoConteoCiclico Estado,
    int? CantidadSistema,
    int? PrimerConteo,
    int? SegundoConteo,
    int StockActual,
    Guid? AjusteInventarioId,
    EstadoAjusteInventario? EstadoAjuste,
    string? Observaciones,
    string? CreadoPor,
    string? IniciadoPor,
    string? ContadoPor,
    string? RecontadoPor,
    DateTime FechaCreacion,
    DateTime? FechaInicio,
    DateTime? FechaPrimerConteo,
    DateTime? FechaReconteo,
    DateTime? FechaFinalizacion);

public sealed record ConteoCiclicoResult(bool Exitoso, ConteoCiclicoDto? Conteo, string Mensaje);

public interface IConteoCiclicoService
{
    Task<IReadOnlyList<ConteoCiclicoDto>> ListarAsync(EstadoConteoCiclico? estado = null, CancellationToken cancellationToken = default);
    Task<ConteoCiclicoResult> PlanificarAsync(Guid stockFisicoId, string? observaciones, string? usuario, CancellationToken cancellationToken = default);
    Task<ConteoCiclicoResult> IniciarAsync(Guid conteoId, string? usuario, CancellationToken cancellationToken = default);
    Task<ConteoCiclicoResult> RegistrarPrimerConteoAsync(Guid conteoId, int cantidadContada, string? usuario, CancellationToken cancellationToken = default);
    Task<ConteoCiclicoResult> RegistrarReconteoAsync(Guid conteoId, int cantidadContada, string? usuario, CancellationToken cancellationToken = default);
    Task<ConteoCiclicoResult> CancelarAsync(Guid conteoId, string? usuario, CancellationToken cancellationToken = default);
}
