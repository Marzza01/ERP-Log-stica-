namespace MarketplaceERP.Catalog.Services;

public record ImportResult(int TotalProcesados, int Exitosos, int Errores, List<string> MensajesError);

public interface ICatalogImportService
{
    Task<ImportResult> ImportarCatalogoCsvAsync(Stream csvStream, Guid defaultVendedorId, Guid defaultCategoriaId, CancellationToken cancellationToken = default);
    Task<ImportResult> ImportarCatalogoDesdeArchivoAsync(string rutaArchivo, Guid defaultVendedorId, Guid defaultCategoriaId, CancellationToken cancellationToken = default);
}
