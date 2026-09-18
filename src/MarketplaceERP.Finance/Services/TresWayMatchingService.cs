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
            .FirstOrDefaultAsync(f => f.Id == request.FacturaProveedorId, cancellationToken)
            ?? throw new InvalidOperationException($"La Factura de Proveedor {request.FacturaProveedorId} no existe.");

        int cantidadOrdenada = po.Items.Sum(i => i.CantidadSolicitada);
        int cantidadRecibida = request.CantidadRecibidaBodega;
        int cantidadFacturada = cantidadRecibida; // En facturación standard

        decimal montoPO = po.MontoTotal;
        decimal montoFactura = factura.MontoTotal;

        var discrepancias = new List<string>();
        bool errorCantidad = cantidadOrdenada != cantidadRecibida;
        bool errorPrecio = Math.Abs(montoPO - montoFactura) > 0.05m;

        Estado3WayMatch resultado;
        bool autorizado = false;

        if (errorCantidad && errorPrecio)
        {
            resultado = Estado3WayMatch.DiscrepanciaTotal;
            discrepancias.Add($"Cantidad solicitada ({cantidadOrdenada}) difiere de recibida ({cantidadRecibida}).");
            discrepancias.Add($"Monto PO (${montoPO:F2}) difiere del facturado (${montoFactura:F2}).");
        }
        else if (errorCantidad)
        {
            resultado = Estado3WayMatch.DiscrepanciaCantidad;
            discrepancias.Add($"Discrepancia física: PO solicitó {cantidadOrdenada} unidades, bodega WMS recibió {cantidadRecibida}.");
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

        string codigoAuditoria = $"3WAY-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var match = new MatchTridireccional
        {
            Id = Guid.NewGuid(),
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
            DetalleDiscrepancias = discrepancias.Count > 0 ? string.Join(" | ", discrepancias) : "Conciliación tridireccional 100% conforme sin discrepancias.",
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
