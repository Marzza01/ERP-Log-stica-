/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

public class FulfillmentService : IFulfillmentService
{
    private readonly CatalogDbContext _db;
    private readonly IStockService _stockService;
    private readonly ILogger<FulfillmentService> _logger;

    public FulfillmentService(
        CatalogDbContext db,
        IStockService stockService,
        ILogger<FulfillmentService> logger)
    {
        _db = db;
        _stockService = stockService;
        _logger = logger;
    }

    public async Task<PickList> GenerarPickListAsync(
        GenerarPickListRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrdenId == Guid.Empty)
            throw new ArgumentException("OrdenId es obligatorio.", nameof(request));

        if (request.Items.Count == 0)
            throw new InvalidOperationException("La orden no contiene items para picking.");

        var pickList = new PickList
        {
            Id = Guid.NewGuid(),
            NumeroPickList = $"PKL-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            OrdenId = request.OrdenId,
            NumeroOrden = request.NumeroOrden,
            OperadorAsignado = string.IsNullOrWhiteSpace(request.OperadorAsignado)
                ? "Operador Bodega 1"
                : request.OperadorAsignado,
            Estado = EstadoPicking.Pendiente,
            FechaCreacion = DateTime.UtcNow
        };

        foreach (var item in request.Items)
        {
            // Fase 4D: la ruta de picking sale de la reserva confirmada, no de
            // una ubicación arbitraria del producto.
            var reserva = await _db.ReservasStock
                .AsNoTracking()
                .Include(r => r.Detalles)
                    .ThenInclude(d => d.StockFisico)
                        .ThenInclude(s => s.UbicacionAlmacen)
                .Where(r =>
                    r.OrdenId == request.OrdenId &&
                    r.ProductoId == item.ProductoId &&
                    (r.Estado == EstadoReserva.Confirmada ||
                     r.Estado == EstadoReserva.ParcialmenteConsumida))
                .OrderByDescending(r => r.FechaReserva)
                .FirstOrDefaultAsync(cancellationToken);

            if (reserva is null)
            {
                throw new InvalidOperationException(
                    $"No existe una reserva confirmada para el producto {item.ProductoId} " +
                    $"de la orden {request.OrdenId}.");
            }

            if (reserva.CantidadPendiente != item.Cantidad)
            {
                throw new InvalidOperationException(
                    $"La cantidad solicitada para picking ({item.Cantidad}) no coincide con " +
                    $"la cantidad reservada pendiente ({reserva.CantidadPendiente}) del producto {item.ProductoId}.");
            }

            var restante = item.Cantidad;

            foreach (var detalle in reserva.Detalles
                         .Where(d => d.CantidadPendiente > 0)
                         .OrderBy(d => d.StockFisico.UbicacionCodigo)
                         .ThenBy(d => d.StockFisicoId))
            {
                if (restante == 0)
                    break;

                var stock = detalle.StockFisico;
                var ubicacion = stock.UbicacionAlmacen;

                if (ubicacion is null)
                {
                    throw new InvalidOperationException(
                        $"El saldo {stock.Id} reservado para la orden {request.OrdenId} " +
                        "no tiene una ubicación WMS asociada.");
                }

                var recoger = Math.Min(restante, detalle.CantidadPendiente);
                if (recoger <= 0)
                    continue;

                pickList.Items.Add(new ItemPickList
                {
                    Id = Guid.NewGuid(),
                    PickListId = pickList.Id,
                    ProductoId = item.ProductoId,
                    Sku = item.Sku,
                    NombreProducto = item.NombreProducto,
                    CantidadSolicitada = recoger,
                    CantidadRecogida = 0,
                    Pasillo = ubicacion.Pasillo,
                    Rack = ubicacion.Rack,
                    Nivel = ubicacion.Nivel,
                    Posicion = ubicacion.Posicion,
                    UbicacionCodigo = stock.UbicacionCodigo
                });

                restante -= recoger;
            }

            if (restante != 0)
            {
                throw new InvalidOperationException(
                    $"La reserva del producto {item.ProductoId} no puede resolverse completamente " +
                    "contra sus ubicaciones WMS.");
            }
        }

        // Ordenar la ruta de recolección óptima (S-Shape por pasillo/rack/nivel/posición)
        pickList.Items = pickList.Items
            .OrderBy(i => i.Pasillo)
            .ThenBy(i => i.Rack)
            .ThenBy(i => i.Nivel)
            .ThenBy(i => i.Posicion)
            .ToList();

        _db.PickLists.Add(pickList);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PickList {NumeroPickList} generada desde reservas confirmadas con {Count} posiciones para orden {NumeroOrden}",
            pickList.NumeroPickList,
            pickList.Items.Count,
            pickList.NumeroOrden);

        return pickList;
    }

    public async Task<PickList?> ConfirmarPickingAsync(
        Guid pickListId,
        List<ConfirmarItemPickingRequest> confirmaciones,
        CancellationToken cancellationToken = default)
    {
        var pickList = await _db.PickLists
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == pickListId, cancellationToken);

        if (pickList is null) return null;

        if (pickList.Estado == EstadoPicking.Pendiente)
        {
            pickList.Estado = EstadoPicking.EnProceso;
            pickList.FechaInicio = DateTime.UtcNow;
        }

        foreach (var conf in confirmaciones)
        {
            var item = pickList.Items.FirstOrDefault(i => i.Id == conf.ItemPickListId);
            if (item != null)
            {
                if (conf.CantidadRecogida < 0 || conf.CantidadRecogida > item.CantidadSolicitada)
                {
                    throw new InvalidOperationException(
                        $"Cantidad de picking inválida para el item {item.Id}. " +
                        $"Solicitada: {item.CantidadSolicitada}; recibida: {conf.CantidadRecogida}.");
                }

                item.CantidadRecogida = conf.CantidadRecogida;
            }
        }

        bool todosRecogidos = pickList.Items.All(i => i.Recogido);
        if (todosRecogidos)
        {
            pickList.Estado = EstadoPicking.Completado;
            pickList.FechaFin = DateTime.UtcNow;
            _logger.LogInformation(
                "PickList {NumeroPickList} completada al 100%.",
                pickList.NumeroPickList);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return pickList;
    }

    public async Task<PaqueteDespacho> EmpacarYRotularAsync(
        EmpacarDespachoRequest request,
        CancellationToken cancellationToken = default)
    {
        var pickListCompletada = await _db.PickLists
            .AsNoTracking()
            .AnyAsync(p => p.OrdenId == request.OrdenId && p.Estado == EstadoPicking.Completado, cancellationToken);

        if (!pickListCompletada)
        {
            throw new InvalidOperationException(
                $"La orden {request.OrdenId} no tiene una PickList completada y no puede empacarse.");
        }

        var paqueteExistente = await _db.PaquetesDespacho
            .FirstOrDefaultAsync(
                p => p.OrdenId == request.OrdenId &&
                     p.Estado != EstadoPacking.Despachado,
                cancellationToken);

        if (paqueteExistente is not null)
            return paqueteExistente;

        var paquete = new PaqueteDespacho
        {
            Id = Guid.NewGuid(),
            NumeroGuia = $"SV-LOGI-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(10000, 99999)}",
            OrdenId = request.OrdenId,
            NumeroOrden = request.NumeroOrden,
            Estado = EstadoPacking.ListoParaDespacho,
            PesoRealKg = request.PesoRealKg,
            LargoCm = request.LargoCm,
            AnchoCm = request.AnchoCm,
            AltoCm = request.AltoCm,
            Transportista = string.IsNullOrWhiteSpace(request.Transportista)
                ? "LOGIMKT Express"
                : request.Transportista,
            DepartamentoDestino = request.DepartamentoDestino,
            MunicipioDestino = request.MunicipioDestino,
            DireccionEntrega = request.DireccionEntrega,
            ContactoReceptor = request.ContactoReceptor,
            TelefonoReceptor = request.TelefonoReceptor,
            FechaEmpaque = DateTime.UtcNow
        };

        _db.PaquetesDespacho.Add(paquete);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Paquete empaquetado para orden {Orden}. Guía emitida: {Guia}",
            paquete.NumeroOrden,
            paquete.NumeroGuia);

        return paquete;
    }

    public async Task<PaqueteDespacho?> DespacharPaqueteAsync(
        Guid paqueteId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        try
        {
            var paquete = await _db.PaquetesDespacho
                .FirstOrDefaultAsync(p => p.Id == paqueteId, cancellationToken);

            if (paquete is null)
            {
                await CommitIfNeededAsync(transaction, cancellationToken);
                return null;
            }

            // Idempotencia: un segundo clic/solicitud no vuelve a consumir stock.
            if (paquete.Estado == EstadoPacking.Despachado && paquete.FechaDespacho.HasValue)
            {
                await CommitIfNeededAsync(transaction, cancellationToken);
                return paquete;
            }

            if (paquete.Estado != EstadoPacking.ListoParaDespacho)
            {
                throw new InvalidOperationException(
                    $"El paquete {paquete.Id} está en estado {paquete.Estado} y no puede despacharse.");
            }

            var pickingCompletado = await _db.PickLists
                .AsNoTracking()
                .AnyAsync(
                    p => p.OrdenId == paquete.OrdenId &&
                         p.Estado == EstadoPicking.Completado,
                    cancellationToken);

            if (!pickingCompletado)
            {
                throw new InvalidOperationException(
                    $"La orden {paquete.OrdenId} no tiene picking completado.");
            }

            // La llamada usa el mismo CatalogDbContext del scope. Si Fulfillment
            // abrió una transacción relacional, StockService la reutiliza.
            await _stockService.ConsumirReservasOrdenAsync(paquete.OrdenId);

            paquete.Estado = EstadoPacking.Despachado;
            paquete.FechaDespacho = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            await CommitIfNeededAsync(transaction, cancellationToken);

            _logger.LogInformation(
                "Paquete con guía {Guia} despachado. Reserva consumida y stock físico descontado. Transportista={Transportista}",
                paquete.NumeroGuia,
                paquete.Transportista);

            return paquete;
        }
        catch
        {
            await RollbackIfNeededAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<IEnumerable<PickList>> ListarPickListsAsync(
        EstadoPicking? estado = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.PickLists.Include(p => p.Items).AsQueryable();

        if (estado.HasValue)
            query = query.Where(p => p.Estado == estado.Value);

        return await query
            .OrderByDescending(p => p.FechaCreacion)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<PaqueteDespacho>> ListarPaquetesAsync(
        EstadoPacking? estado = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.PaquetesDespacho.AsQueryable();

        if (estado.HasValue)
            query = query.Where(p => p.Estado == estado.Value);

        return await query
            .OrderByDescending(p => p.FechaEmpaque)
            .ToListAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync(
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational() || _db.Database.CurrentTransaction is not null)
            return null;

        return await _db.Database.BeginTransactionAsync(cancellationToken);
    }

    private static async Task CommitIfNeededAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RollbackIfNeededAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(cancellationToken);
    }
}
