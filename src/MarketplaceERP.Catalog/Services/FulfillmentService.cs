/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Catalog.Services;

public class FulfillmentService : IFulfillmentService
{
    private readonly CatalogDbContext _db;
    private readonly INumeracionService _numeracionService;
    private readonly ILogger<FulfillmentService> _logger;

    public FulfillmentService(CatalogDbContext db, INumeracionService numeracionService, ILogger<FulfillmentService> logger)
    {
        _db = db;
        _numeracionService = numeracionService;
        _logger = logger;
    }

    /// <summary>Constructor de conveniencia para tests unitarios</summary>
    public FulfillmentService(CatalogDbContext db, ILogger<FulfillmentService> logger)
        : this(db, new TestInMemoryNumeracionService(), logger)
    {
    }

    public async Task<PickList> GenerarPickListAsync(GenerarPickListRequest request, CancellationToken cancellationToken = default)
    {
        var numeroPickList = await _numeracionService.GenerarNumeroPickListAsync(cancellationToken);
        var pickList = new PickList
        {
            Id = Guid.NewGuid(),
            NumeroPickList = numeroPickList,
            OrdenId = request.OrdenId,
            NumeroOrden = request.NumeroOrden,
            OperadorAsignado = string.IsNullOrWhiteSpace(request.OperadorAsignado) ? "Operador Bodega 1" : request.OperadorAsignado,
            Estado = EstadoPicking.Pendiente,
            FechaCreacion = DateTime.UtcNow
        };

        foreach (var item in request.Items)
        {
            // Localizar stock físico para determinar pasillo y nivel WMS óptimo
            var stock = await _db.StockFisico
                .Include(s => s.UbicacionAlmacen)
                .FirstOrDefaultAsync(s => s.ProductoId == item.ProductoId, cancellationToken);

            var itemPick = new ItemPickList
            {
                Id = Guid.NewGuid(),
                PickListId = pickList.Id,
                ProductoId = item.ProductoId,
                Sku = item.Sku,
                NombreProducto = item.NombreProducto,
                CantidadSolicitada = item.Cantidad,
                CantidadRecogida = 0,
                Pasillo = stock?.UbicacionAlmacen?.Pasillo ?? "A",
                Rack = stock?.UbicacionAlmacen?.Rack ?? "01",
                Nivel = stock?.UbicacionAlmacen?.Nivel ?? 1,
                Posicion = stock?.UbicacionAlmacen?.Posicion ?? "01",
                UbicacionCodigo = stock?.UbicacionCodigo ?? "PAS-A-RCK-01-NIV-01-POS-01"
            };

            pickList.Items.Add(itemPick);
        }

        // Ordenar la ruta de recolección óptima (S-Shape routing por pasillo y nivel)
        pickList.Items = pickList.Items
            .OrderBy(i => i.Pasillo)
            .ThenBy(i => i.Rack)
            .ThenBy(i => i.Nivel)
            .ThenBy(i => i.Posicion)
            .ToList();

        _db.PickLists.Add(pickList);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PickList {NumeroPickList} generada con {Count} items para orden {NumeroOrden}",
            pickList.NumeroPickList, pickList.Items.Count, pickList.NumeroOrden);

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
                item.CantidadRecogida = conf.CantidadRecogida;
            }
        }

        bool todosRecogidos = pickList.Items.All(i => i.Recogido);
        if (todosRecogidos)
        {
            pickList.Estado = EstadoPicking.Completado;
            pickList.FechaFin = DateTime.UtcNow;
            _logger.LogInformation("PickList {NumeroPickList} completada al 100%.", pickList.NumeroPickList);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return pickList;
    }

    public async Task<PaqueteDespacho> EmpacarYRotularAsync(EmpacarDespachoRequest request, CancellationToken cancellationToken = default)
    {
        var numeroGuia = await _numeracionService.GenerarNumeroGuiaAsync(cancellationToken);
        var paquete = new PaqueteDespacho
        {
            Id = Guid.NewGuid(),
            NumeroGuia = numeroGuia,
            OrdenId = request.OrdenId,
            NumeroOrden = request.NumeroOrden,
            Estado = EstadoPacking.ListoParaDespacho,
            PesoRealKg = request.PesoRealKg,
            LargoCm = request.LargoCm,
            AnchoCm = request.AnchoCm,
            AltoCm = request.AltoCm,
            Transportista = string.IsNullOrWhiteSpace(request.Transportista) ? "LOGIMKT Express" : request.Transportista,
            DepartamentoDestino = request.DepartamentoDestino,
            MunicipioDestino = request.MunicipioDestino,
            DireccionEntrega = request.DireccionEntrega,
            ContactoReceptor = request.ContactoReceptor,
            TelefonoReceptor = request.TelefonoReceptor,
            FechaEmpaque = DateTime.UtcNow
        };

        _db.PaquetesDespacho.Add(paquete);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Paquete empaquetado para orden {Orden}. Guía emitida: {Guia}",
            paquete.NumeroOrden, paquete.NumeroGuia);

        return paquete;
    }

    public async Task<PaqueteDespacho?> DespacharPaqueteAsync(Guid paqueteId, CancellationToken cancellationToken = default)
    {
        var paquete = await _db.PaquetesDespacho.FindAsync(new object[] { paqueteId }, cancellationToken);
        if (paquete is null) return null;

        paquete.FechaDespacho = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Paquete con guía {Guia} despachado a transportista {Transportista}",
            paquete.NumeroGuia, paquete.Transportista);

        return paquete;
    }

    public async Task<IEnumerable<PickList>> ListarPickListsAsync(EstadoPicking? estado = null, CancellationToken cancellationToken = default)
    {
        var query = _db.PickLists.Include(p => p.Items).AsQueryable();
        if (estado.HasValue)
        {
            query = query.Where(p => p.Estado == estado.Value);
        }

        return await query.OrderByDescending(p => p.FechaCreacion).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<PaqueteDespacho>> ListarPaquetesAsync(EstadoPacking? estado = null, CancellationToken cancellationToken = default)
    {
        var query = _db.PaquetesDespacho.AsQueryable();
        if (estado.HasValue)
        {
            query = query.Where(p => p.Estado == estado.Value);
        }

        return await query.OrderByDescending(p => p.FechaEmpaque).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Implementación de INumeracionService exclusiva para tests unitarios / InMemory en Catalog.
/// </summary>
internal class TestInMemoryNumeracionService : INumeracionService
{
    private static long _pickCounter = 0;
    private static long _guiaCounter = 0;

    public Task<string> GenerarNumeroOrdenAsync(CancellationToken ct = default)
        => Task.FromResult($"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..25]);

    public Task<string> GenerarNumeroPOAsync(CancellationToken ct = default)
        => Task.FromResult($"PO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20]);

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
}
