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
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<RmaService> _logger;

    public RmaService(
        CatalogDbContext dbContext,
        IInventoryService inventoryService,
        ILogger<RmaService> logger)
    {
        _dbContext = dbContext;
        _inventoryService = inventoryService;
        _logger = logger;
    }

    public async Task<DevolucionRMA> RegistrarSolicitudRmaAsync(RegistrarRmaRequest request, CancellationToken cancellationToken = default)
    {
        if (request.OrdenId == Guid.Empty)
            throw new InvalidOperationException("OrdenId es obligatorio.");
        if (request.ProductoId == Guid.Empty)
            throw new InvalidOperationException("ProductoId es obligatorio.");
        if (request.Cantidad <= 0)
            throw new InvalidOperationException("La cantidad devuelta debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(request.MotivoCliente))
            throw new InvalidOperationException("El motivo de devolución es obligatorio.");

        var productoExiste = await _dbContext.Productos
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.ProductoId, cancellationToken);
        if (!productoExiste)
            throw new InvalidOperationException($"El producto {request.ProductoId} no existe en catálogo.");

        string numeroRma = $"RMA-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var rma = new DevolucionRMA
        {
            Id = Guid.NewGuid(),
            NumeroRMA = numeroRma,
            OrdenId = request.OrdenId,
            ProductoId = request.ProductoId,
            Cantidad = request.Cantidad,
            MotivoCliente = request.MotivoCliente.Trim(),
            Estado = EstadoRMA.Solicitado,
            Clasificacion = ClasificacionRMA.Pendiente,
            FechaSolicitud = DateTime.UtcNow
        };

        _dbContext.DevolucionesRMA.Add(rma);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Devolución RMA {NumeroRMA} registrada para Producto={ProductoId}, Cantidad={Cantidad}",
            numeroRma, request.ProductoId, request.Cantidad);

        return rma;
    }

    public async Task<DevolucionRMA?> InspeccionarDevolucionAsync(
        Guid rmaId,
        DictaminarRmaRequest dictamen,
        CancellationToken cancellationToken = default)
    {
        var rma = await _dbContext.DevolucionesRMA
            .Include(d => d.Producto)
            .FirstOrDefaultAsync(d => d.Id == rmaId, cancellationToken);

        if (rma is null)
            return null;

        // Reintentos sobre un RMA ya completado no deben duplicar inventario ni cambiar dictamen.
        if (rma.Estado == EstadoRMA.Completado)
        {
            if (rma.Clasificacion == dictamen.Clasificacion)
                return rma;

            throw new InvalidOperationException(
                $"El RMA {rma.NumeroRMA} ya fue completado con clasificación {rma.Clasificacion}.");
        }

        if (string.IsNullOrWhiteSpace(dictamen.Inspector))
            throw new InvalidOperationException("El inspector técnico es obligatorio.");
        if (string.IsNullOrWhiteSpace(dictamen.DictamenTecnico))
            throw new InvalidOperationException("El dictamen técnico es obligatorio.");

        // En ReStock, primero se aplica el movimiento idempotente de inventario. Si el guardado
        // posterior del RMA fallara, un reintento reconocerá el movimiento por RmaId y no duplicará stock.
        if (dictamen.Clasificacion == ClasificacionRMA.ReStock)
        {
            if (!dictamen.UbicacionDestinoId.HasValue || dictamen.UbicacionDestinoId == Guid.Empty)
                throw new InvalidOperationException("Debe seleccionar una ubicación WMS para un RMA ReStock.");
            if (!dictamen.OperacionId.HasValue || dictamen.OperacionId == Guid.Empty)
                throw new InvalidOperationException("OperacionId es obligatorio para procesar un RMA ReStock.");

            var inventario = await _inventoryService.RecibirRmaRestockAsync(
                rma.Id,
                rma.ProductoId,
                rma.Cantidad,
                dictamen.UbicacionDestinoId.Value,
                dictamen.OperacionId.Value,
                $"RMA {rma.NumeroRMA}: {dictamen.DictamenTecnico.Trim()}",
                cancellationToken);

            if (!inventario.Exitoso)
                throw new InvalidOperationException(inventario.Mensaje);
        }

        rma.Estado = EstadoRMA.Dictaminado;
        rma.Clasificacion = dictamen.Clasificacion;
        rma.InspectorTecnico = dictamen.Inspector.Trim();
        rma.DictamenTecnico = dictamen.DictamenTecnico.Trim();
        rma.PorcentajeRemateDescuento = dictamen.DescuentoRemate;
        rma.FechaInspeccion = DateTime.UtcNow;

        switch (dictamen.Clasificacion)
        {
            case ClasificacionRMA.ReStock:
                rma.Estado = EstadoRMA.Completado;
                break;

            case ClasificacionRMA.MermaScrap:
                // No reingresa a inventario disponible. La baja/merma contable puede ser tratada
                // en una fase posterior sin alterar stock vendible aquí.
                rma.Estado = EstadoRMA.Completado;
                break;

            case ClasificacionRMA.Remate:
                // Queda clasificado para flujo de remate/reacondicionado. No se mezcla con stock
                // disponible normal en esta fase.
                rma.Estado = EstadoRMA.Completado;
                break;

            default:
                throw new InvalidOperationException("Debe seleccionar una clasificación técnica válida.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "RMA {NumeroRMA} dictaminado como {Clasificacion}. Estado={Estado}",
            rma.NumeroRMA, dictamen.Clasificacion, rma.Estado);

        return rma;
    }

    public async Task<IEnumerable<DevolucionRMA>> ListarRmaAsync(
        EstadoRMA? estado = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.DevolucionesRMA
            .Include(d => d.Producto)
            .AsQueryable();

        if (estado.HasValue)
            query = query.Where(d => d.Estado == estado.Value);

        return await query
            .OrderByDescending(d => d.FechaSolicitud)
            .ToListAsync(cancellationToken);
    }
}
