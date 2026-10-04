/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;

namespace MarketplaceERP.Catalog.Services;

public record RegistrarRmaRequest(Guid OrdenId, Guid ProductoId, int Cantidad, string MotivoCliente);
public record DictaminarRmaRequest(ClasificacionRMA Clasificacion, string Inspector, string DictamenTecnico, decimal DescuentoRemate = 0m);

public interface IRmaService
{
    Task<DevolucionRMA> RegistrarSolicitudRmaAsync(RegistrarRmaRequest request, CancellationToken cancellationToken = default);
    Task<DevolucionRMA?> InspeccionarDevolucionAsync(Guid rmaId, DictaminarRmaRequest dictamen, CancellationToken cancellationToken = default);
    Task<IEnumerable<DevolucionRMA>> ListarRmaAsync(EstadoRMA? estado = null, CancellationToken cancellationToken = default);
}
