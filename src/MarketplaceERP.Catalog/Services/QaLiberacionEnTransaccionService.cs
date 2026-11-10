using MarketplaceERP.Catalog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Adaptador estrictamente LOCAL QA. Reutiliza StockService sin cambiar su
/// visibilidad internal, y solo cuando Catalog ya participa en transaccion
/// PostgreSQL externa que comparte Finance. No registra endpoints ni DI.
/// </summary>
public sealed class QaLiberacionEnTransaccionService
{
    private readonly CatalogDbContext _catalog;
    private readonly ILoggerFactory _logs;

    public QaLiberacionEnTransaccionService(
        CatalogDbContext catalog, ILoggerFactory logs)
    {
        _catalog = catalog;
        _logs = logs;
    }

    public async Task LiberarAsync(
        Guid ordenId, IReadOnlyList<Guid> ids, bool autorizadoQa,
        int? falloTrasLiberarNumeroQa = null)
    {
        if (!autorizadoQa || ordenId == Guid.Empty ||
            ids is null || ids.Count is < 2 or > 20 ||
            ids.Any(id => id == Guid.Empty) ||
            ids.Distinct().Count() != ids.Count ||
            _catalog.Database.CurrentTransaction is null)
            throw new InvalidOperationException("LIBERACION_QA_TRANSACCION_REQUERIDA");

        var cs = new NpgsqlConnectionStringBuilder(
            _catalog.Database.GetDbConnection().ConnectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(cs.Host, StringComparer.OrdinalIgnoreCase) ||
            cs.Port != 5432 || cs.Username != "postgres" ||
            string.IsNullOrWhiteSpace(cs.Database) ||
            !cs.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal))
            throw new InvalidOperationException("LIBERACION_QA_NO_LOCAL");

        var stock = new StockService(_catalog, _logs.CreateLogger<StockService>());
        for (var i = 0; i < ids.Count; i++)
        {
            await stock.LiberarStockAsync(ids[i]);
            if (falloTrasLiberarNumeroQa == i + 1)
                throw new InvalidOperationException("QA_FALLO_DURANTE_COMPENSACION");
        }
    }
}
