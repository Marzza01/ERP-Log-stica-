using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// PROTOTIPO QA AISLADO. No registrar en DI ni exponer por HTTP.
/// Refusa por diseño cualquier base que no sea ci_checkout_http_* en localhost:5432.
/// Exige autorización explícita en el llamador de ensayo. No llama a pasarelas.
/// </summary>
public sealed class CierreCheckoutAdminQaService
{
    private readonly FinanceDbContext _finance;

    public CierreCheckoutAdminQaService(FinanceDbContext finance) =>
        _finance = finance;

    public async Task<string> CerrarAsync(
        Guid ordenId, bool autorizacionExplicitaQa,
        CancellationToken ct = default)
    {
        if (!autorizacionExplicitaQa)
            throw new InvalidOperationException("Cierre QA sin autorización explícita.");
        if (ordenId == Guid.Empty)
            throw new InvalidOperationException("Orden inválida.");

        var conexion = _finance.Database.GetDbConnection();
        var c = new NpgsqlConnectionStringBuilder(conexion.ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(c.Host, StringComparer.OrdinalIgnoreCase) ||
            c.Port != 5432 || c.Username != "postgres" ||
            string.IsNullOrWhiteSpace(c.Database) ||
            !c.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "PROHIBIDO: cierre QA fuera de una base efímera ci_checkout_http_ en localhost.");

        // Mismo PostgreSQL, misma conexión, UNA transacción para consultar
        // ambas tablas de Finance y Catalog y actualizar finance.ordenes.
        await using var transaccion = await _finance.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // MISMO candado por orden de FulfillmentService.GenerarPickListAsync:
        // evita aprobar basándose en una ausencia de PickList mientras WMS
        // todavía mantiene sin confirmar su transacción de creación.
        long llaveOrden = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
        await _finance.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({llaveOrden})", ct);

        // El controlador HTTP de RMA utiliza OTRA clave consultiva por orden.
        // Compartir también esta clave hace visibles los RMA confirmados por
        // esa ruta antes de evaluar la elegibilidad de cierre QA.
        int llaveRma = BitConverter.ToInt32(ordenId.ToByteArray(), 0);
        await _finance.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(6173153, {llaveRma})", ct);

        // Serializa dos cierres de la misma orden y cualquier inserción con
        // FK a la orden que requiera KEY SHARE mientras se verifica.
        await _finance.Database.ExecuteSqlInterpolatedAsync(
            $@"SELECT ""Id"" FROM finance.ordenes WHERE ""Id"" = {ordenId} FOR UPDATE", ct);
        var orden = await _finance.Ordenes
            .SingleOrDefaultAsync(o => o.Id == ordenId, ct)
            ?? throw new InvalidOperationException("Orden inexistente.");

        await _finance.Database.ExecuteSqlInterpolatedAsync(
            $@"SELECT ""Id"" FROM finance.intentos_pago WHERE ""OrdenId"" = {ordenId} FOR UPDATE", ct);
        await _finance.Database.ExecuteSqlInterpolatedAsync(
            $@"SELECT ""Id"" FROM finance.splits_pago WHERE ""OrdenId"" = {ordenId} FOR UPDATE", ct);

        // NO abre una segunda transacción: el contexto Catalog adopta
        // la misma conexión y transacción PostgreSQL de Finance.
        var opcionesCatalog = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(conexion).Options;
        await using var catalog = new CatalogDbContext(opcionesCatalog);
        await catalog.Database.UseTransactionAsync(
            transaccion.GetDbTransaction(), ct);

        var idsProductos = await _finance.ItemsOrden.AsNoTracking()
            .Where(i => i.OrdenId == ordenId)
            .Select(i => i.ProductoId)
            .Distinct().ToListAsync(ct);
        foreach (var productoId in idsProductos.OrderBy(x => x))
            await catalog.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT ""Id"" FROM catalog.stock_fisico WHERE ""ProductoId"" = {productoId} FOR UPDATE", ct);
        await catalog.Database.ExecuteSqlInterpolatedAsync(
            $@"SELECT ""Id"" FROM catalog.reservas_stock WHERE ""OrdenId"" = {ordenId} FOR UPDATE", ct);

        var revision = await new CierreCheckoutPreflight(_finance, catalog)
            .EvaluarAsync(ordenId, ct);

        // Idempotencia: segunda solicitud no actualiza timestamps ni crea registros.
        if (orden.Estado == EstadoOrden.Aprobada &&
            revision.Bloqueos.Count == 1 &&
            revision.Bloqueos[0] == "ORDEN_NO_PENDIENTE")
        {
            await transaccion.CommitAsync(ct);
            return "YA_APROBADA";
        }

        if (!revision.DatosCoherentes)
            throw new InvalidOperationException(
                "Cierre QA bloqueado por invariantes: " +
                string.Join(",", revision.Bloqueos));

        // La recuperación administrativa QA reproduce el cierre normal:
        // asiento y aprobación se confirman en la misma transacción.
        var contabilidad = new ContabilidadService(
            _finance, NullLogger<ContabilidadService>.Instance);
        await contabilidad.GenerarAsientoVentaAsync(
            orden.Id, orden.SubTotal, orden.ImpuestoIva,
            orden.TipoComprobante, ct);

        orden.Estado = EstadoOrden.Aprobada;
        orden.FechaAprobacion = DateTime.UtcNow;
        await _finance.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return "APROBADA";
    }
}
