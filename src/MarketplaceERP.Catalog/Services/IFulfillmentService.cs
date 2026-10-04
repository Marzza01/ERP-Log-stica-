/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public record ItemPickingSolicitud(Guid ProductoId, string Sku, string NombreProducto, int Cantidad);

public record GenerarPickListRequest(
    Guid OrdenId,
    string NumeroOrden,
    string OperadorAsignado,
    List<ItemPickingSolicitud> Items);

public record ConfirmarItemPickingRequest(
    Guid ItemPickListId,
    int CantidadRecogida);

public record EmpacarDespachoRequest(
    Guid OrdenId,
    string NumeroOrden,
    decimal PesoRealKg,
    decimal LargoCm,
    decimal AnchoCm,
    decimal AltoCm,
    string Transportista,
    string DepartamentoDestino,
    string MunicipioDestino,
    string DireccionEntrega,
    string ContactoReceptor,
    string TelefonoReceptor);

public interface IFulfillmentService
{
    Task<PickList> GenerarPickListAsync(GenerarPickListRequest request, CancellationToken cancellationToken = default);
    Task<PickList?> ConfirmarPickingAsync(Guid pickListId, List<ConfirmarItemPickingRequest> confirmaciones, CancellationToken cancellationToken = default);
    Task<PaqueteDespacho> EmpacarYRotularAsync(EmpacarDespachoRequest request, CancellationToken cancellationToken = default);
    Task<PaqueteDespacho?> DespacharPaqueteAsync(Guid paqueteId, CancellationToken cancellationToken = default);
    Task<IEnumerable<PickList>> ListarPickListsAsync(EstadoPicking? estado = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaqueteDespacho>> ListarPaquetesAsync(EstadoPacking? estado = null, CancellationToken cancellationToken = default);
}
