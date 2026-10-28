/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Implementación de INumeracionService que genera secuencias correlativas en PostgreSQL.
/// Mantiene compatibilidad con proveedores no relacionales (InMemory) para pruebas unitarias.
/// Si la llamada a la secuencia relacional falla, la excepción no se silencia y sube en la pila.
/// </summary>
public class NumeracionService : INumeracionService
{
    private readonly FinanceDbContext _financeDb;
    private readonly CatalogDbContext _catalogDb;

    // Contadores en memoria para tests que usan InMemoryDatabase
    private static long _memOrdenCounter = 0;
    private static long _memPOCounter = 0;
    private static long _memPickListCounter = 0;
    private static long _memGuiaCounter = 0;

    public NumeracionService(FinanceDbContext financeDb, CatalogDbContext catalogDb)
    {
        _financeDb = financeDb;
        _catalogDb = catalogDb;
    }

    public async Task<string> GenerarNumeroOrdenAsync(CancellationToken ct = default)
    {
        long correlativo;
        if (_financeDb.Database.IsRelational())
        {
            correlativo = await ObtenerNextValAsync(_financeDb, "finance.\"NumeroOrdenSeq\"", ct);
        }
        else
        {
            correlativo = Interlocked.Increment(ref _memOrdenCounter);
        }

        // Formato ORD-yyyyMMdd-NNNNN (mínimo 5 dígitos, crece si el correlativo lo supera)
        return $"ORD-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D5}";
    }

    public async Task<string> GenerarNumeroPOAsync(CancellationToken ct = default)
    {
        long correlativo;
        if (_financeDb.Database.IsRelational())
        {
            correlativo = await ObtenerNextValAsync(_financeDb, "finance.\"NumeroPOSeq\"", ct);
        }
        else
        {
            correlativo = Interlocked.Increment(ref _memPOCounter);
        }

        // Formato PO-yyyyMMdd-NNNN (mínimo 4 dígitos, crece si el correlativo lo supera)
        return $"PO-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D4}";
    }

    public async Task<string> GenerarNumeroPickListAsync(CancellationToken ct = default)
    {
        long correlativo;
        if (_catalogDb.Database.IsRelational())
        {
            correlativo = await ObtenerNextValAsync(_catalogDb, "catalog.\"NumeroPickListSeq\"", ct);
        }
        else
        {
            correlativo = Interlocked.Increment(ref _memPickListCounter);
        }

        // Formato PKL-yyyyMMdd-NNNN (mínimo 4 dígitos, crece si el correlativo lo supera)
        return $"PKL-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D4}";
    }

    public async Task<string> GenerarNumeroGuiaAsync(CancellationToken ct = default)
    {
        long correlativo;
        if (_catalogDb.Database.IsRelational())
        {
            correlativo = await ObtenerNextValAsync(_catalogDb, "catalog.\"NumeroGuiaSeq\"", ct);
        }
        else
        {
            correlativo = Interlocked.Increment(ref _memGuiaCounter);
        }

        // Formato SV-LOGI-yyyyMMdd-NNNNN (mínimo 5 dígitos, crece si el correlativo lo supera)
        return $"SV-LOGI-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D5}";
    }

    private static async Task<long> ObtenerNextValAsync(DbContext dbContext, string nombreSecuencia, CancellationToken ct)
    {
        await using var cmd = dbContext.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT nextval('{nombreSecuencia}');";

        if (dbContext.Database.CurrentTransaction != null)
        {
            cmd.Transaction = dbContext.Database.CurrentTransaction.GetDbTransaction();
        }

        if (cmd.Connection!.State != ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync(ct);
        }

        var scalar = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(scalar);
    }
}

