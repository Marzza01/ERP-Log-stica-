/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public sealed record AjusteInventarioDto(
    Guid Id,
    Guid StockFisicoId,
    Guid ProductoId,
    string Sku,
    string ProductoNombre,
    string UbicacionCodigo,
    TipoAjusteInventario Tipo,
    int Cantidad,
    string Motivo,
    string? Detalle,
    string? Referencia,
    EstadoAjusteInventario Estado,
    int StockActual,
    int DisponibleActual,
    string? CreadoPor,
    string? AprobadoPor,
    string? AplicadoPor,
    DateTime FechaCreacion,
    DateTime? FechaDecision,
    DateTime? FechaAplicacion);

public sealed record AjusteInventarioResult(bool Exitoso, AjusteInventarioDto? Ajuste, string Mensaje);

public interface IAjusteInventarioService
{
    Task<IReadOnlyList<AjusteInventarioDto>> ListarAsync(EstadoAjusteInventario? estado = null, CancellationToken cancellationToken = default);
    Task<AjusteInventarioResult> CrearAsync(Guid stockFisicoId, TipoAjusteInventario tipo, int cantidad, string motivo, string? detalle, string? referencia, string? usuario, CancellationToken cancellationToken = default);
    Task<AjusteInventarioResult> AprobarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default);
    Task<AjusteInventarioResult> RechazarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default);
    Task<AjusteInventarioResult> CancelarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default);
    Task<AjusteInventarioResult> AplicarAsync(Guid ajusteId, string? usuario, CancellationToken cancellationToken = default);
}
