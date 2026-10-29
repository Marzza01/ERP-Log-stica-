/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public record MapaPasilloDto(string Pasillo, int TotalRacks, int TotalPosiciones, int PosicionesOcupadas, List<UbicacionAlmacen> Ubicaciones);
public record ResumenBodegaDto(int TotalUbicaciones, int Ocupadas, int Disponibles, decimal PorcentajeOcupacion, List<MapaPasilloDto> Pasillos);

public sealed record StockWmsDto(
    Guid Id,
    Guid ProductoId,
    string Sku,
    string ProductoNombre,
    int CantidadTotal,
    int CantidadReservada,
    int CantidadBloqueada,
    int CantidadDisponible,
    Guid? UbicacionAlmacenId,
    string UbicacionCodigo,
    string Zona,
    DateTime UltimaActualizacion);

public interface IWmsService
{
    Task<ResumenBodegaDto> ObtenerMapaBodegaAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<UbicacionAlmacen>> ListarUbicacionesAsync(string? pasillo = null, bool? soloDisponibles = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<StockWmsDto>> ListarStocksAsync(Guid? ubicacionId = null, string? sku = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transferencia parcial o total de stock disponible entre ubicaciones WMS.
    /// </summary>
    Task<TransferenciaInventarioResult> TransferirStockAsync(
        Guid stockFisicoId,
        Guid nuevaUbicacionId,
        int cantidad,
        Guid operacionId,
        string? observaciones = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compatibilidad con el endpoint antiguo: intenta mover todo el saldo cuando no existen reservas ni bloqueos.
    /// La UI nueva utiliza TransferirStockAsync.
    /// </summary>
    Task<bool> ReubicarStockAsync(Guid stockFisicoId, Guid nuevaUbicacionId, CancellationToken cancellationToken = default);
}
