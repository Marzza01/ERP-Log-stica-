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
    /// Busca y libera reservas expiradas en estado Pendiente, y reconcilia reservas huérfanas de órdenes Canceladas.
    /// </summary>
    public async Task<int> PurgarReservasExpiradasAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stockService = scope.ServiceProvider.GetRequiredService<IStockService>();

        var ahora = DateTime.UtcNow;
        var reservasExpiradas = await dbContext.ReservasStock
            .Where(r => r.Estado == EstadoReserva.Pendiente && r.FechaExpiracion != null && r.FechaExpiracion <= ahora)
            .ToListAsync(cancellationToken);

        int liberadas = 0;
        foreach (var reserva in reservasExpiradas)
        {
            try
            {
                // Si la reserva está ligada a una orden, verificar si la orden fue aprobada/pagada en Finance
                bool ordenPagada = false;
                if (reserva.OrdenId != Guid.Empty && dbContext.Database.IsRelational())
                {
                    try
                    {
                        await using var cmd = dbContext.Database.GetDbConnection().CreateCommand();
                        cmd.CommandText = @"
                            SELECT 1 
                            FROM information_schema.tables 
                            WHERE table_schema = 'finance' AND table_name = 'ordenes'
                            LIMIT 1;";

                        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
                        {
                            await cmd.Connection.OpenAsync(cancellationToken);
                        }

                        var tableExists = await cmd.ExecuteScalarAsync(cancellationToken);
                        if (tableExists != null)
                        {
                            await using var checkCmd = dbContext.Database.GetDbConnection().CreateCommand();
                            checkCmd.CommandText = @"
                                SELECT ""Estado"" 
                                FROM finance.ordenes 
                                WHERE ""Id"" = @ordenId 
                                LIMIT 1;";
                            var p = checkCmd.CreateParameter();
                            p.ParameterName = "@ordenId";
                            p.Value = reserva.OrdenId;
                            checkCmd.Parameters.Add(p);

                            var estadoObj = await checkCmd.ExecuteScalarAsync(cancellationToken);
                            if (estadoObj != null)
                            {
                                string estadoStr = estadoObj.ToString() ?? string.Empty;
                                if (string.Equals(estadoStr, "Aprobada", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(estadoStr, "EnProceso", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(estadoStr, "Enviada", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(estadoStr, "Entregada", StringComparison.OrdinalIgnoreCase) ||
                                    (int.TryParse(estadoStr, out var num) && num >= 1 && num <= 4))
                                {
                                    ordenPagada = true;
                                }
                            }
                        }
                    }
                    catch (Exception exCheck)
                    {
                        _logger.LogWarning(exCheck, "No se pudo verificar estado de la orden {OrdenId} en Finance. Continuando flujo...", reserva.OrdenId);
                    }
                }

                if (ordenPagada)
                {
                    // Reconciliación: la orden ya se pagó pero el proceso murió antes de ComprometerReservasOrdenAsync.
                    // Se pasa a Comprometida y se retira la expiración.
                    reserva.Estado = EstadoReserva.Comprometida;
                    reserva.FechaExpiracion = null;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Reserva expirada {ReservaId} reconciliada a Comprometida porque la orden {OrdenId} ya está aprobada/pagada.",
                        reserva.Id, reserva.OrdenId);
                }
                else
                {
                    // Orden inexistente, cancelada o aún pendiente de pago -> Liberar stock
                    await stockService.LiberarStockAsync(reserva.Id);
                    liberadas++;
                    _logger.LogInformation("Reserva expirada {ReservaId} liberada con éxito por ReservaStockCleanupService.", reserva.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar reserva expirada {ReservaId}.", reserva.Id);
            }
        }

        // Reconciliación ventana post-commit: si la orden fue Cancelada en Finance pero LiberarStockAsync falló o el proceso murió
        if (dbContext.Database.IsRelational())
        {
            try
            {
                var reservasVivasDeOrdenes = await dbContext.ReservasStock
                    .Where(r => r.OrdenId != Guid.Empty && (r.Estado == EstadoReserva.Comprometida || r.Estado == EstadoReserva.Pendiente))
                    .ToListAsync(cancellationToken);

                foreach (var res in reservasVivasDeOrdenes)
                {
                    try
                    {
                        await using var checkCancelCmd = dbContext.Database.GetDbConnection().CreateCommand();
                        checkCancelCmd.CommandText = @"
                            SELECT ""Estado"" 
                            FROM finance.ordenes 
                            WHERE ""Id"" = @ordenId 
                            LIMIT 1;";
                        var p = checkCancelCmd.CreateParameter();
                        p.ParameterName = "@ordenId";
                        p.Value = res.OrdenId;
                        checkCancelCmd.Parameters.Add(p);

                        if (checkCancelCmd.Connection!.State != System.Data.ConnectionState.Open)
                            await checkCancelCmd.Connection.OpenAsync(cancellationToken);

                        var estadoObj = await checkCancelCmd.ExecuteScalarAsync(cancellationToken);
                        if (estadoObj != null)
                        {
                            string estadoStr = estadoObj.ToString() ?? string.Empty;
                            if (string.Equals(estadoStr, "Cancelada", StringComparison.OrdinalIgnoreCase) ||
                                (int.TryParse(estadoStr, out var num) && num == 5)) // EstadoOrden.Cancelada = 5
                            {
                                _logger.LogInformation("Reconciliador: Liberando reserva huérfana {ReservaId} de orden {OrdenId} que ya fue Cancelada.", res.Id, res.OrdenId);
                                await stockService.LiberarStockAsync(res.Id);
                                liberadas++;
                            }
                            else if ((string.Equals(estadoStr, "Enviada", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(estadoStr, "Entregada", StringComparison.OrdinalIgnoreCase) ||
                                      (int.TryParse(estadoStr, out var numEnv) && (numEnv == 3 || numEnv == 4))) // Enviada=3, Entregada=4
                                     && res.Estado == EstadoReserva.Comprometida)
                            {
                                _logger.LogInformation("Reconciliador: Confirmando despacho para reserva {ReservaId} de orden {OrdenId} en estado {Estado}.", res.Id, res.OrdenId, estadoStr);
                                await stockService.ConfirmarDespachoAsync(res.Id);
                            }
                        }
                    }
                    catch (Exception exRes)
                    {
                        _logger.LogWarning(exRes, "Error al verificar cancelación de orden para reserva {ReservaId}.", res.Id);
                    }
                }
            }
            catch (Exception exReconcile)
            {
                _logger.LogWarning(exReconcile, "Error al reconciliar reservas de órdenes canceladas.");
            }
        }

        return liberadas;
    }
}
