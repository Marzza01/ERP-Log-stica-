/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

/// <summary>
/// Implementación real de IStockService con control atómico de concurrencia en base de datos.
/// Soporta ejecución con PostgreSQL real y fallback en memoria para tests.
/// </summary>
public class StockService : IStockService
{
    private readonly CatalogDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StockService> _logger;

    public StockService(
        CatalogDbContext dbContext,
        IConfiguration configuration,
        ILogger<StockService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReservaResult> ReservarStockAsync(Guid productoId, int cantidad, Guid ordenId)
    {
        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad a reservar debe ser mayor a 0.", nameof(cantidad));
        }

        int minutosExpiracion = 15;
        if (int.TryParse(_configuration["Stock:ReservationExpirationMinutes"], out var exp) && exp > 0)
        {
            minutosExpiracion = exp;
        }

        DateTime fechaExp = DateTime.UtcNow.AddMinutes(minutosExpiracion);

        // Si estamos ejecutando contra una base de datos relacional (ej: PostgreSQL)
        if (_dbContext.Database.IsRelational())
        {
            var conn = _dbContext.Database.GetDbConnection();
            bool wasClosed = conn.State != ConnectionState.Open;
            if (wasClosed) await conn.OpenAsync();

            try
            {
                // Primero verificar si existe registro de stock físico para el producto
                await using (var checkCmd = conn.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT \"CantidadTotal\", \"CantidadReservada\" FROM \"catalog\".\"stock_fisico\" WHERE \"ProductoId\" = @prodId;";
                    var pId = checkCmd.CreateParameter();
                    pId.ParameterName = "@prodId";
                    pId.Value = productoId;
                    checkCmd.Parameters.Add(pId);

                    await using var reader = await checkCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                    {
                        // No existe registro de stock físico: se considera agotado (409)
                        _logger.LogWarning("No existe registro de StockFisico para ProductoId={ProductoId}. Stock agotado.", productoId);
                        throw new StockInsuficienteException(productoId, cantidad, 0);
                    }

                    int total = reader.GetInt32(0);
                    int reservada = reader.GetInt32(1);
                    int disponible = total - reservada;

                    if (disponible < cantidad)
                    {
                        _logger.LogWarning("Stock insuficiente para ProductoId={ProductoId}. Solicitado={Solicitado}, Disponible={Disponible}.",
                            productoId, cantidad, disponible);
                        throw new StockInsuficienteException(productoId, cantidad, disponible);
                    }
                }

                // Actualización atómica con WHERE condicional
                await using (var updateCmd = conn.CreateCommand())
                {
                    updateCmd.CommandText = @"
                        UPDATE ""catalog"".""stock_fisico"" 
                        SET ""CantidadReservada"" = ""CantidadReservada"" + @cant,
                            ""UltimaActualizacion"" = NOW()
                        WHERE ""ProductoId"" = @prodId 
                          AND (""CantidadTotal"" - ""CantidadReservada"") >= @cant;";

                    var pCant = updateCmd.CreateParameter();
                    pCant.ParameterName = "@cant";
                    pCant.Value = cantidad;
                    updateCmd.Parameters.Add(pCant);

                    var pId = updateCmd.CreateParameter();
                    pId.ParameterName = "@prodId";
                    pId.Value = productoId;
                    updateCmd.Parameters.Add(pId);

                    int filasAfectadas = await updateCmd.ExecuteNonQueryAsync();
                    if (filasAfectadas == 0)
                    {
                        // Concurrencia: otro proceso reservó las unidades disponibles
                        _logger.LogWarning("Fallo atómico al reservar stock para ProductoId={ProductoId} por concurrencia.", productoId);
                        throw new StockInsuficienteException(productoId, cantidad, 0);
                    }
                }

                // Insertar registro de reserva
                var reserva = new ReservaStock
                {
                    Id = Guid.NewGuid(),
                    ProductoId = productoId,
                    OrdenId = ordenId,
                    Cantidad = cantidad,
                    Estado = EstadoReserva.Pendiente,
                    FechaReserva = DateTime.UtcNow,
                    FechaExpiracion = fechaExp
                };

                _dbContext.ReservasStock.Add(reserva);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Reserva creada exitosamente: ReservaId={ReservaId}, ProductoId={ProductoId}, Cantidad={Cantidad}.",
                    reserva.Id, productoId, cantidad);

                return new ReservaResult(reserva.Id, true, null);
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }
        }
        else
        {
            // Ejecución InMemory / proveedor no relacional
            var stock = await _dbContext.StockFisico.FirstOrDefaultAsync(s => s.ProductoId == productoId);
            if (stock == null)
            {
                throw new StockInsuficienteException(productoId, cantidad, 0);
            }

            if (stock.CantidadDisponible < cantidad)
            {
                throw new StockInsuficienteException(productoId, cantidad, stock.CantidadDisponible);
            }

            stock.CantidadReservada += cantidad;
            stock.UltimaActualizacion = DateTime.UtcNow;

            var reserva = new ReservaStock
            {
                Id = Guid.NewGuid(),
                ProductoId = productoId,
                OrdenId = ordenId,
                Cantidad = cantidad,
                Estado = EstadoReserva.Pendiente,
                FechaReserva = DateTime.UtcNow,
                FechaExpiracion = fechaExp
            };

            _dbContext.ReservasStock.Add(reserva);
            await _dbContext.SaveChangesAsync();

            return new ReservaResult(reserva.Id, true, null);
        }
    }

    /// <inheritdoc />
    public async Task ConfirmarDespachoAsync(Guid reservaId)
    {
        var reserva = await _dbContext.ReservasStock.FirstOrDefaultAsync(r => r.Id == reservaId);
        if (reserva == null)
        {
            _logger.LogWarning("ConfirmarDespacho: ReservaId={ReservaId} no encontrada.", reservaId);
            return;
        }

        if (reserva.Estado == EstadoReserva.Confirmada || reserva.Estado == EstadoReserva.Despachada)
        {
            return;
        }

        if (_dbContext.Database.IsRelational())
        {
            var conn = _dbContext.Database.GetDbConnection();
            bool wasClosed = conn.State != ConnectionState.Open;
            if (wasClosed) await conn.OpenAsync();

            try
            {
                await using var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE ""catalog"".""stock_fisico""
                    SET ""CantidadTotal"" = GREATEST(0, ""CantidadTotal"" - @cant),
                        ""CantidadReservada"" = GREATEST(0, ""CantidadReservada"" - @cant),
                        ""UltimaActualizacion"" = NOW()
                    WHERE ""ProductoId"" = @prodId;";

                var pCant = updateCmd.CreateParameter();
                pCant.ParameterName = "@cant";
                pCant.Value = reserva.Cantidad;
                updateCmd.Parameters.Add(pCant);

                var pId = updateCmd.CreateParameter();
                pId.ParameterName = "@prodId";
                pId.Value = reserva.ProductoId;
                updateCmd.Parameters.Add(pId);

                await updateCmd.ExecuteNonQueryAsync();

                reserva.Estado = EstadoReserva.Confirmada;
                reserva.FechaConfirmacion = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Reserva {ReservaId} confirmada. Stock total y reservado decrementados en {Cantidad}.",
                    reservaId, reserva.Cantidad);
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }
        }
        else
        {
            var stock = await _dbContext.StockFisico.FirstOrDefaultAsync(s => s.ProductoId == reserva.ProductoId);
            if (stock != null)
            {
                stock.CantidadTotal = Math.Max(0, stock.CantidadTotal - reserva.Cantidad);
                stock.CantidadReservada = Math.Max(0, stock.CantidadReservada - reserva.Cantidad);
                stock.UltimaActualizacion = DateTime.UtcNow;
            }

            reserva.Estado = EstadoReserva.Confirmada;
            reserva.FechaConfirmacion = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    /// <inheritdoc />
    public async Task LiberarStockAsync(Guid reservaId)
    {
        var reserva = await _dbContext.ReservasStock.FirstOrDefaultAsync(r => r.Id == reservaId);
        if (reserva == null)
        {
            _logger.LogWarning("LiberarStock: ReservaId={ReservaId} no encontrada.", reservaId);
            return;
        }

        if (reserva.Estado == EstadoReserva.Liberada)
        {
            return;
        }

        if (_dbContext.Database.IsRelational())
        {
            var conn = _dbContext.Database.GetDbConnection();
            bool wasClosed = conn.State != ConnectionState.Open;
            if (wasClosed) await conn.OpenAsync();

            try
            {
                await using var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE ""catalog"".""stock_fisico""
                    SET ""CantidadReservada"" = GREATEST(0, ""CantidadReservada"" - @cant),
                        ""UltimaActualizacion"" = NOW()
                    WHERE ""ProductoId"" = @prodId;";

                var pCant = updateCmd.CreateParameter();
                pCant.ParameterName = "@cant";
                pCant.Value = reserva.Cantidad;
                updateCmd.Parameters.Add(pCant);

                var pId = updateCmd.CreateParameter();
                pId.ParameterName = "@prodId";
                pId.Value = reserva.ProductoId;
                updateCmd.Parameters.Add(pId);

                await updateCmd.ExecuteNonQueryAsync();

                reserva.Estado = EstadoReserva.Liberada;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Reserva {ReservaId} liberada. Stock reservado decrementado en {Cantidad}.",
                    reservaId, reserva.Cantidad);
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }
        }
        else
        {
            var stock = await _dbContext.StockFisico.FirstOrDefaultAsync(s => s.ProductoId == reserva.ProductoId);
            if (stock != null)
            {
                stock.CantidadReservada = Math.Max(0, stock.CantidadReservada - reserva.Cantidad);
                stock.UltimaActualizacion = DateTime.UtcNow;
            }

            reserva.Estado = EstadoReserva.Liberada;
            await _dbContext.SaveChangesAsync();
        }
    }
}
