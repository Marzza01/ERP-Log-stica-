/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public enum PrioridadAlertaInventario
{
    Medium,
    High,
    Critical
}

public enum EstadoAlertaInventario
{
    Open,
    Acknowledged
}

public sealed record AlertaInventarioOperativa(
    Guid ProductoId,
    string AlertaId,
    string Sku,
    string Producto,
    string Categoria,
    TipoAlertaInventario Tipo,
    PrioridadAlertaInventario Prioridad,
    EstadoAlertaInventario Estado,
    int CantidadDisponible,
    int CantidadFisica,
    int CantidadReservada,
    int CantidadBloqueada,
    int StockMinimo,
    int PuntoReorden,
    int FaltanteAReorden,
    string Ubicaciones,
    DateTime? ReconocidaEn,
    string? ReconocidaPor);

public interface IAlertaInventarioService
{
    Task<IReadOnlyList<AlertaInventarioOperativa>> ObtenerActivasAsync(
        CancellationToken cancellationToken = default);

    Task<bool> ReconocerAsync(
        Guid productoId,
        TipoAlertaInventario tipo,
        string usuario,
        CancellationToken cancellationToken = default);
}
