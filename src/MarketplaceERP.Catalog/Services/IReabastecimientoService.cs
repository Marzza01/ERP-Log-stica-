/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Services;

public sealed record NecesidadReabastecimiento(
    Guid ProductoId,
    string Sku,
    string Nombre,
    string CategoriaNombre,
    string ProveedorNombre,
    int StockMinimo,
    int CantidadFisica,
    int CantidadReservada,
    int CantidadBloqueada,
    int CantidadDisponible,
    int FaltanteHastaMinimo,
    int CantidadParaSalirAlerta,
    string NivelCriticidad);

public interface IReabastecimientoService
{
    Task<IReadOnlyList<NecesidadReabastecimiento>> ObtenerNecesidadesAsync(
        CancellationToken cancellationToken = default);
}
