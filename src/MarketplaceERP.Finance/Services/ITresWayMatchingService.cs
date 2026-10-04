/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Entities;

namespace MarketplaceERP.Finance.Services;

public record Validar3WayMatchRequest(Guid OrdenCompraId, Guid FacturaProveedorId, int CantidadRecibidaBodega);

public interface ITresWayMatchingService
{
    Task<MatchTridireccional> EjecutarMatchAsync(Validar3WayMatchRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<MatchTridireccional>> ListarHistorialMatchesAsync(CancellationToken cancellationToken = default);
}
