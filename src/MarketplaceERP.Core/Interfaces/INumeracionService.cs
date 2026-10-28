/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Core.Interfaces;

/// <summary>
/// Servicio centralizado de numeración única y correlativa basada en secuencias de base de datos.
/// </summary>
public interface INumeracionService
{
    Task<string> GenerarNumeroOrdenAsync(CancellationToken ct = default);
    Task<string> GenerarNumeroPOAsync(CancellationToken ct = default);
    Task<string> GenerarNumeroPickListAsync(CancellationToken ct = default);
    Task<string> GenerarNumeroGuiaAsync(CancellationToken ct = default);
    Task<string> GenerarNumeroRmaAsync(CancellationToken ct = default);
    Task<string> GenerarCodigoAuditoria3WayAsync(CancellationToken ct = default);
}

