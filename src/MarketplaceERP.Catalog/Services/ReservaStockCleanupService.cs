/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// BackgroundService que ejecuta periódicamente la limpieza de reservas de stock expiradas.
/// Libera el stock reservado de aquellas reservas en estado Pendiente cuya FechaExpiracion sea anterior a UtcNow.
/// </summary>
public class ReservaStockCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReservaStockCleanupService> _logger;

    public ReservaStockCleanupService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ReservaStockCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool enableJob = !bool.TryParse(_configuration["Stock:EnableCleanupJob"], out var ej) || ej;
        if (!enableJob)
        {
            _logger.LogInformation("ReservaStockCleanupService deshabilitado por configuración (Stock:EnableCleanupJob=false).");
            return;
        }

        _logger.LogInformation("Iniciando servicio en segundo plano ReservaStockCleanupService...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgarReservasExpiradasAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error durante la purga de reservas de stock expiradas.");
            }

            int intervaloSegundos = int.TryParse(_configuration["Stock:CleanupIntervalSeconds"], out var sec) && sec > 0 ? sec : 60;

            await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos), stoppingToken);
        }
    }

    /// <summary>
    /// Busca y libera reservas expiradas en estado Pendiente.
    /// </summary>
    public async Task<int> PurgarReservasExpiradasAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stockService = scope.ServiceProvider.GetRequiredService<IStockService>();

        var ahora = DateTime.UtcNow;
        var reservasExpiradas = await dbContext.ReservasStock
            .Where(r => r.Estado == EstadoReserva.Pendiente && r.FechaExpiracion != null && r.FechaExpiracion <= ahora)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        int liberadas = 0;
        foreach (var reservaId in reservasExpiradas)
        {
            try
            {
                await stockService.LiberarStockAsync(reservaId);
                liberadas++;
                _logger.LogInformation("Reserva expirada {ReservaId} liberada con éxito por ReservaStockCleanupService.", reservaId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al liberar reserva expirada {ReservaId}.", reservaId);
            }
        }

        return liberadas;
    }
}
