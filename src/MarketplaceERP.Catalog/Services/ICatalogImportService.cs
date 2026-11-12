/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Services;

public record ImportResult(int TotalProcesados, int Exitosos, int Errores, List<string> MensajesError);

public interface ICatalogImportService
{
    Task<ImportResult> ImportarCatalogoCsvAsync(Stream csvStream, Guid defaultVendedorId, Guid defaultCategoriaId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Simulación de la validación del archivo. No crea ni modifica productos,
    /// ubicaciones ni saldos WMS; no garantiza integridad referencial en BD.
    /// </summary>
    Task<ImportResult> ValidarCatalogoCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportarCatalogoDesdeArchivoAsync(string rutaArchivo, Guid defaultVendedorId, Guid defaultCategoriaId, CancellationToken cancellationToken = default);
}
