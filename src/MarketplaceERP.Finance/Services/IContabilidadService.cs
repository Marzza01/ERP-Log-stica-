using MarketplaceERP.Finance.Entities;

namespace MarketplaceERP.Finance.Services;

public record LibroMayorCuenta(string CodigoCuenta, string NombreCuenta, decimal TotalDebe, decimal TotalHaber, decimal Saldo);
public record BalanceComprobacion(decimal TotalDebe, decimal TotalHaber, bool EstaCuadrado, List<LibroMayorCuenta> Cuentas);

public interface IContabilidadService
{
    Task<PartidaContable> GenerarAsientoVentaAsync(Guid ordenId, decimal montoVenta, decimal iva, string tipoComprobante, CancellationToken cancellationToken = default);
    Task<PartidaContable> GenerarAsientoCompraAsync(Guid ordenCompraId, decimal montoCompra, decimal iva, CancellationToken cancellationToken = default);
    Task<IEnumerable<PartidaContable>> ObtenerLibroDiarioAsync(DateTime? desde = null, DateTime? hasta = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<LibroMayorCuenta>> ObtenerLibroMayorAsync(string? codigoCuenta = null, CancellationToken cancellationToken = default);
    Task<BalanceComprobacion> ObtenerBalanceComprobacionAsync(CancellationToken cancellationToken = default);
}
