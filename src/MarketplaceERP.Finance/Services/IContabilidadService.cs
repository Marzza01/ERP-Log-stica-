/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Entities;

namespace MarketplaceERP.Finance.Services;

public record LibroMayorCuenta(string CodigoCuenta, string NombreCuenta, decimal TotalDebe, decimal TotalHaber, decimal Saldo);
public record BalanceComprobacion(decimal TotalDebe, decimal TotalHaber, bool EstaCuadrado, List<LibroMayorCuenta> Cuentas);
public record LibroDiarioPaginado(int Total, int Page, int PageSize, List<PartidaContable> Partidas);
public record LineaAsientoInput(string CodigoCuenta, decimal Debe, decimal Haber, string? Glosa = null);

public interface IContabilidadService
{
    Task<PartidaContable> GenerarAsientoManualAsync(
        string concepto,
        string tipoOperacion,
        IEnumerable<LineaAsientoInput> lineas,
        Guid? referenciaOrigenId = null,
        string? documentoReferencia = null,
        CancellationToken cancellationToken = default);

    Task<PartidaContable> GenerarAsientoVentaAsync(
        Guid ordenId,
        decimal montoVenta,
        decimal iva,
        string tipoComprobante,
        CancellationToken cancellationToken = default);

    Task<PartidaContable> GenerarAsientoCompraAsync(
        Guid ordenCompraId,
        decimal montoCompra,
        decimal iva,
        CancellationToken cancellationToken = default);

    Task<LibroDiarioPaginado> ObtenerLibroDiarioPaginadoAsync(
        DateTime? desde = null,
        DateTime? hasta = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<PartidaContable>> ObtenerLibroDiarioAsync(
        DateTime? desde = null,
        DateTime? hasta = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<LibroMayorCuenta>> ObtenerLibroMayorAsync(
        string? codigoCuenta = null,
        CancellationToken cancellationToken = default);

    Task<BalanceComprobacion> ObtenerBalanceComprobacionAsync(
        CancellationToken cancellationToken = default);
}
