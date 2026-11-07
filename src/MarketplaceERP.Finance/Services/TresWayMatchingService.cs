/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Finance.Services;

public class TresWayMatchingService : ITresWayMatchingService
{
    private readonly FinanceDbContext _dbContext;
    private readonly ILogger<TresWayMatchingService> _logger;

    public TresWayMatchingService(FinanceDbContext dbContext, ILogger<TresWayMatchingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<MatchTridireccional> EjecutarMatchAsync(Validar3WayMatchRequest request, CancellationToken cancellationToken = default)
    {
        var po = await _dbContext.OrdenesCompra
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == request.OrdenCompraId, cancellationToken)
            ?? throw new InvalidOperationException($"La Orden de Compra {request.OrdenCompraId} no existe.");

        var factura = await _dbContext.FacturasProveedor
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.Id == request.FacturaProveedorId, cancellationToken)
            ?? throw new InvalidOperationException($"La Factura de Proveedor {request.FacturaProveedorId} no existe.");

        // La cantidad confirmada debe proceder del estado persistido de la PO,
        // no de un número presentado por el cliente del endpoint de matching.
        if (request.OrdenCompraId == Guid.Empty || request.FacturaProveedorId == Guid.Empty)
            throw new ArgumentException("La orden y la factura deben ser válidas.");

        if (factura.OrdenCompraId != po.Id || factura.ProveedorId != po.ProveedorId)
            throw new InvalidOperationException(
                "La factura no pertenece a la orden de compra o al proveedor indicado.");

        if (po.Estado is not (EstadoOrdenCompra.RecibidaParcial or EstadoOrdenCompra.RecibidaCompleta))
            throw new InvalidOperationException(
                "La orden debe contar con una recepción WMS antes de ejecutar el 3-Way Match.");

        var cantidadOrdenada = po.Items.Sum(i => i.CantidadSolicitada);
        var cantidadRecibida = po.Items.Sum(i => i.CantidadRecibida);

        if (cantidadOrdenada <= 0 || cantidadRecibida < 0 ||
            po.Items.Any(i => i.CantidadSolicitada <= 0 ||
                              i.CantidadRecibida < 0 ||
                              i.CantidadRecibida > i.CantidadSolicitada))
            throw new InvalidOperationException("La orden tiene cantidades de inventario inconsistentes.");

        if (request.CantidadRecibidaBodega != cantidadRecibida)
            throw new ArgumentException(
                "La cantidad recibida enviada no coincide con las recepciones WMS persistidas. Recargue la orden.");

        if (po.MontoTotal <= 0m || factura.MontoSubTotal <= 0m ||
            factura.MontoIva < 0m || factura.MontoTotal <= 0m ||
            factura.MontoSubTotal + factura.MontoIva != factura.MontoTotal)
            throw new InvalidOperationException("La orden o factura contiene importes inválidos o descuadrados.");

        // No ejecutar dos autorizaciones sobre el mismo pedido: cada match
        // conforme disparaba otro asiento de compra desde ComprasController.
        if (factura.AprobadaParaPago || await _dbContext.MatchesTridireccionales
            .AnyAsync(m => m.OrdenCompraId == po.Id && m.AutorizadoParaPago,
                cancellationToken))
            throw new InvalidOperationException(
                "La orden ya cuenta con un match autorizado. No se puede autorizar nuevamente.");

        // FIN-07: jamás equiparar "facturado" con "recibido". Las facturas
        // históricas sin líneas se conservan, pero no pueden autorizar pagos.
        if (factura.Items.Count == 0)
            throw new InvalidOperationException(
                "La factura no tiene detalle documental por producto. Registre sus líneas antes de conciliar.");

        var poPorId = po.Items.ToDictionary(i => i.Id);
        if (factura.Items.Count > po.Items.Count ||
            factura.Items.GroupBy(i => i.ItemOrdenCompraId).Any(g => g.Count() != 1) ||
            factura.Items.Any(i => !poPorId.ContainsKey(i.ItemOrdenCompraId) ||
                i.CantidadFacturada <= 0 || i.PrecioUnitarioFacturado <= 0m ||
                decimal.Round(i.PrecioUnitarioFacturado, 2) != i.PrecioUnitarioFacturado))
            throw new InvalidOperationException(
                "El detalle facturado contiene líneas inválidas, duplicadas o ajenas a la orden.");

        int cantidadFacturada;
        decimal subtotalLineas;
        try
        {
            cantidadFacturada = 0;
            subtotalLineas = 0m;
            foreach (var linea in factura.Items)
            {
                cantidadFacturada = checked(cantidadFacturada + linea.CantidadFacturada);
                subtotalLineas = checked(subtotalLineas +
                    checked(linea.CantidadFacturada * linea.PrecioUnitarioFacturado));
            }
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("El detalle de factura excede la precisión admitida.");
        }

        if (subtotalLineas != factura.MontoSubTotal)
            throw new InvalidOperationException(
                "La suma de las líneas de factura no coincide con el subtotal guardado.");

        decimal montoPO = po.MontoTotal;
        decimal montoFactura = factura.MontoTotal;

        var discrepancias = new List<string>();
        bool errorCantidad = cantidadOrdenada != cantidadRecibida ||
            factura.Items.Count != po.Items.Count ||
            po.Items.Any(item => factura.Items.FirstOrDefault(f =>
                f.ItemOrdenCompraId == item.Id)?.CantidadFacturada != item.CantidadRecibida);

        // Importes monetarios decimal(18,2): incluso un centavo de diferencia
        // no puede declararse conforme ni generar un asiento de compra.
        bool errorPrecio = montoPO != montoFactura ||
            po.Items.Any(item => factura.Items.Any(f =>
                f.ItemOrdenCompraId == item.Id &&
                f.PrecioUnitarioFacturado != item.PrecioUnitarioAcordado));

        foreach (var item in po.Items)
        {
            var linea = factura.Items.FirstOrDefault(f => f.ItemOrdenCompraId == item.Id);
            if (linea is null)
                discrepancias.Add($"Factura sin línea para SKU {item.Sku}.");
            else
            {
                if (linea.CantidadFacturada != item.CantidadRecibida)
                    discrepancias.Add($"SKU {item.Sku}: facturado {linea.CantidadFacturada}, recibido WMS {item.CantidadRecibida}.");
                if (linea.PrecioUnitarioFacturado != item.PrecioUnitarioAcordado)
                    discrepancias.Add($"SKU {item.Sku}: precio factura {linea.PrecioUnitarioFacturado:F2}, PO {item.PrecioUnitarioAcordado:F2}.");
            }
        }

        Estado3WayMatch resultado;
        bool autorizado = false;

        if (errorCantidad && errorPrecio)
        {
            resultado = Estado3WayMatch.DiscrepanciaTotal;
            discrepancias.Add($"Cantidad: PO {cantidadOrdenada}, WMS {cantidadRecibida}, factura {cantidadFacturada}.");
            discrepancias.Add($"Monto PO (${montoPO:F2}) difiere del facturado (${montoFactura:F2}).");
        }
        else if (errorCantidad)
        {
            resultado = Estado3WayMatch.DiscrepanciaCantidad;
            discrepancias.Add($"Cantidad: PO {cantidadOrdenada}, WMS {cantidadRecibida}, factura {cantidadFacturada}.");
        }
        else if (errorPrecio)
        {
            resultado = Estado3WayMatch.DiscrepanciaPrecio;
            discrepancias.Add($"Discrepancia financiera: PO aprobó ${montoPO:F2}, proveedor facturó ${montoFactura:F2}.");
        }
        else
        {
            resultado = Estado3WayMatch.Conforme;
            autorizado = true;
            factura.AprobadaParaPago = true;
            po.Estado = EstadoOrdenCompra.RecibidaCompleta;
            po.FechaRecepcionReal = DateTime.UtcNow;
        }

        var auditoriaId = Guid.NewGuid();
        string codigoAuditoria = $"3WAY-{DateTime.UtcNow:yyyyMMdd}-{auditoriaId:N}";

        var match = new MatchTridireccional
        {
            Id = auditoriaId,
            CodigoAuditoria = codigoAuditoria,
            OrdenCompraId = po.Id,
            FacturaProveedorId = factura.Id,
            CantidadOrdenadaPO = cantidadOrdenada,
            CantidadRecibidaBodega = cantidadRecibida,
            CantidadFacturada = cantidadFacturada,
            MontoTotalPO = montoPO,
            MontoTotalFactura = montoFactura,
            Resultado = resultado,
            AutorizadoParaPago = autorizado,
            DetalleDiscrepancias = discrepancias.Count > 0
                ? string.Join(" | ", discrepancias)[..Math.Min(
                    string.Join(" | ", discrepancias).Length, 1000)]
                : "Conciliación tridireccional 100% conforme sin discrepancias.",
            Auditor = "Sistema Automático de Conciliación 3-Way Match",
            FechaAuditoria = DateTime.UtcNow
        };

        _dbContext.MatchesTridireccionales.Add(match);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Match 3-Way completado para PO={NumeroPO}, Resultado={Resultado}, Autorizado={Autorizado}",
            po.NumeroPO, resultado, autorizado);

        return match;
    }

    public async Task<IEnumerable<MatchTridireccional>> ListarHistorialMatchesAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.MatchesTridireccionales
            .Include(m => m.OrdenCompra)
            .Include(m => m.FacturaProveedor)
            .OrderByDescending(m => m.FechaAuditoria)
            .ToListAsync(cancellationToken);
    }
}
