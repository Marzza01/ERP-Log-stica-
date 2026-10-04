/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public record MapaPasilloDto(string Pasillo, int TotalRacks, int TotalPosiciones, int PosicionesOcupadas, List<UbicacionAlmacen> Ubicaciones);
public record ResumenBodegaDto(int TotalUbicaciones, int Ocupadas, int Disponibles, decimal PorcentajeOcupacion, List<MapaPasilloDto> Pasillos);

public interface IWmsService
{
    Task<ResumenBodegaDto> ObtenerMapaBodegaAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<UbicacionAlmacen>> ListarUbicacionesAsync(string? pasillo = null, bool? soloDisponibles = null, CancellationToken cancellationToken = default);
    Task<bool> ReubicarStockAsync(Guid stockFisicoId, Guid nuevaUbicacionId, CancellationToken cancellationToken = default);
}
