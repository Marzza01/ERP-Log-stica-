/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

public class RmaService : IRmaService
{
    private readonly CatalogDbContext _dbContext;
    private readonly ILogger<RmaService> _logger;

    public RmaService(CatalogDbContext dbContext, ILogger<RmaService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<DevolucionRMA> RegistrarSolicitudRmaAsync(RegistrarRmaRequest request, CancellationToken cancellationToken = default)
    {
        string numeroRma = $"RMA-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var rma = new DevolucionRMA
        {
            Id = Guid.NewGuid(),
            NumeroRMA = numeroRma,
            OrdenId = request.OrdenId,
            ProductoId = request.ProductoId,
            Cantidad = request.Cantidad,
            MotivoCliente = request.MotivoCliente,
            Estado = EstadoRMA.Solicitado,
            Clasificacion = ClasificacionRMA.Pendiente,
            FechaSolicitud = DateTime.UtcNow
        };

        _dbContext.DevolucionesRMA.Add(rma);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Devolución RMA {NumeroRMA} registrada para Producto={ProductoId}, Cantidad={Cantidad}",
            numeroRma, request.ProductoId, request.Cantidad);

        return rma;
    }

    public async Task<DevolucionRMA?> InspeccionarDevolucionAsync(
        Guid rmaId, DictaminarRmaRequest dictamen, CancellationToken cancellationToken = default)
    {
        var rma = await _dbContext.DevolucionesRMA
            .Include(d => d.Producto)
            .ThenInclude(p => p!.Stock)
            .FirstOrDefaultAsync(d => d.Id == rmaId, cancellationToken);

        if (rma is null)
        {
            return null;
        }

        rma.Estado = EstadoRMA.Dictaminado;
        rma.Clasificacion = dictamen.Clasificacion;
        rma.InspectorTecnico = dictamen.Inspector;
        rma.DictamenTecnico = dictamen.DictamenTecnico;
        rma.PorcentajeRemateDescuento = dictamen.DescuentoRemate;
        rma.FechaInspeccion = DateTime.UtcNow;

        // Lógica de decisión técnica (PDF 1 - Grupo A):
        switch (dictamen.Clasificacion)
        {
            case ClasificacionRMA.ReStock:
                // Reingresar al stock físico disponible
                if (rma.Producto?.Stock is not null)
                {
                    rma.Producto.Stock.CantidadTotal += rma.Cantidad;
                    rma.Producto.Stock.UltimaActualizacion = DateTime.UtcNow;
                }
                rma.Estado = EstadoRMA.Completado;
                break;

            case ClasificacionRMA.MermaScrap:
                // Pérdida total / no reingresa al inventario apto para venta
                rma.Estado = EstadoRMA.Completado;
                break;

            case ClasificacionRMA.Remate:
                // Se clasifica para lote de remate con precio reducido
                rma.Estado = EstadoRMA.Completado;
                break;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("RMA {NumeroRMA} dictaminado como {Clasificacion}. Accion tomada.",
            rma.NumeroRMA, dictamen.Clasificacion);

        return rma;
    }

    public async Task<IEnumerable<DevolucionRMA>> ListarRmaAsync(EstadoRMA? estado = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.DevolucionesRMA
            .Include(d => d.Producto)
            .AsQueryable();

        if (estado.HasValue)
        {
            query = query.Where(d => d.Estado == estado.Value);
        }

        return await query
            .OrderByDescending(d => d.FechaSolicitud)
            .ToListAsync(cancellationToken);
    }
}
