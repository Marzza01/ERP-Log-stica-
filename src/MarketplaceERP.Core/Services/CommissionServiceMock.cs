/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Core.Services;

/// <summary>
/// Mock del servicio de comisiones para el arranque inicial del proyecto.
/// Retorna un 10% fijo de comisión independientemente del vendedor o categoría.
/// REEMPLAZAR por CommissionService real cuando Equipo 1 implemente las reglas de negocio.
/// </summary>
internal sealed class CommissionServiceMock : ICommissionService
{
    private const decimal ComisionPorDefecto = 0.10m; // 10%
    private readonly ILogger<CommissionServiceMock> _logger;

    public CommissionServiceMock(ILogger<CommissionServiceMock> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<decimal> CalcularComisionAsync(Guid vendedorId, decimal montoTotal)
    {
        var comision = Math.Round(montoTotal * ComisionPorDefecto, 2);

        _logger.LogInformation(
            "[MOCK] CalcularComision: VendedorId={VendedorId}, MontoTotal={MontoTotal:C}, Comision={Comision:C} ({Pct}%)",
            vendedorId, montoTotal, comision, ComisionPorDefecto * 100);

        return Task.FromResult(comision);
    }
}
