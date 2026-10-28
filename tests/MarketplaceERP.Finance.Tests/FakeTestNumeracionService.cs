/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Implementación de INumeracionService exclusiva para pruebas unitarias de Finance.
/// </summary>
public class FakeTestNumeracionService : INumeracionService
{
    private static long _ordenCounter = 0;
    private static long _poCounter = 0;
    private static long _pickCounter = 0;
    private static long _guiaCounter = 0;

    public Task<string> GenerarNumeroOrdenAsync(CancellationToken ct = default)
    {
        var correlativo = Interlocked.Increment(ref _ordenCounter);
        return Task.FromResult($"ORD-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D5}");
    }

    public Task<string> GenerarNumeroPOAsync(CancellationToken ct = default)
    {
        var correlativo = Interlocked.Increment(ref _poCounter);
        return Task.FromResult($"PO-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D4}");
    }

    public Task<string> GenerarNumeroPickListAsync(CancellationToken ct = default)
    {
        var correlativo = Interlocked.Increment(ref _pickCounter);
        return Task.FromResult($"PKL-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D4}");
    }

    public Task<string> GenerarNumeroGuiaAsync(CancellationToken ct = default)
    {
        var correlativo = Interlocked.Increment(ref _guiaCounter);
        return Task.FromResult($"SV-LOGI-{DateTime.UtcNow:yyyyMMdd}-{correlativo:D5}");
    }

    public Task<string> GenerarNumeroRmaAsync(CancellationToken ct = default)
        => Task.FromResult($"RMA-{DateTime.UtcNow:yyyyMMdd}-0001");

    public Task<string> GenerarCodigoAuditoria3WayAsync(CancellationToken ct = default)
        => Task.FromResult($"3WAY-{DateTime.UtcNow:yyyyMMdd}-0001");
}

