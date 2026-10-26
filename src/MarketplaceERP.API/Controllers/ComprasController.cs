/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Compras / Procurement — Célula B.
/// Gestiona Órdenes de Compra (PO), Facturas de Proveedor y Match Tridireccional.
/// Requisito: PDF1 R-COMP-01 a R-COMP-06
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ComprasController : ControllerBase
{
    private readonly MarketplaceERP.Finance.Data.FinanceDbContext _db;
    private readonly ITresWayMatchingService _matching;
    private readonly IContabilidadService _contabilidad;
    private readonly ILogger<ComprasController> _logger;

    public ComprasController(
        MarketplaceERP.Finance.Data.FinanceDbContext db,
        ITresWayMatchingService matching,
        IContabilidadService contabilidad,
        ILogger<ComprasController> logger)
    {
        _db = db;
        _matching = matching;
        _contabilidad = contabilidad;
        _logger = logger;
    }

    // ── GET /api/compras/po ───────────────────────────────────────────────────
    /// <summary>Lista todas las Órdenes de Compra paginadas.</summary>
    [HttpGet("po", Name = "ListarOrdenesCompra")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarPO(
        [FromQuery] string? estado = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.OrdenesCompra
            .Include(p => p.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado) && Enum.TryParse<EstadoOrdenCompra>(estado, true, out var est))
            query = query.Where(p => p.Estado == est);

        var total = await query.CountAsync(cancellationToken);
        var pos = await query
            .OrderByDescending(p => p.FechaEmision)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(cancellationToken);

        return Ok(new { total, pagina, tamano, ordenesCompra = pos });
    }

    // ── GET /api/compras/po/{id} ──────────────────────────────────────────────
    [HttpGet("po/{id:guid}", Name = "ObtenerOrdenCompra")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerPO(Guid id, CancellationToken cancellationToken)
    {
        var po = await _db.OrdenesCompra
            .Include(p => p.Items)
            .Include(p => p.Facturas)
            .Include(p => p.Matches3Way)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (po == null) return NotFound(new { mensaje = $"PO {id} no encontrada." });
        return Ok(po);
    }

    // ── POST /api/compras/po ──────────────────────────────────────────────────
    /// <summary>Emite una nueva Orden de Compra al proveedor.</summary>
    [HttpPost("po", Name = "CrearOrdenCompra")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CrearPO([FromBody] CrearPORequest req, CancellationToken cancellationToken)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { mensaje = "La PO debe tener al menos un ítem." });

        var numeroPO = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var po = new OrdenCompra
        {
            Id = Guid.NewGuid(),
            NumeroPO = numeroPO,
            ProveedorId = req.ProveedorId,
            ProveedorNombre = req.ProveedorNombre,
            Estado = EstadoOrdenCompra.Borrador,
            TerminosPago = req.TerminosPago ?? "Crédito 30 días",
            Observaciones = req.Observaciones ?? string.Empty,
            FechaEmision = DateTime.UtcNow,
            FechaEntregaEsperada = req.FechaEntregaEsperada ?? DateTime.UtcNow.AddDays(15),
        };

        decimal subtotal = 0m;
        foreach (var item in req.Items)
        {
            po.Items.Add(new ItemOrdenCompra
            {
                Id = Guid.NewGuid(),
                OrdenCompraId = po.Id,
                Sku = item.Sku,
                NombreProducto = item.NombreProducto,
                CantidadSolicitada = item.Cantidad,
                PrecioUnitarioAcordado = item.PrecioUnitario,
                CantidadRecibida = 0,
            });
            subtotal += item.Cantidad * item.PrecioUnitario;
        }

        po.SubTotal = subtotal;
        po.ImpuestoIva = Math.Round(subtotal * 0.13m, 2);
        po.MontoTotal = po.SubTotal + po.ImpuestoIva;

        _db.OrdenesCompra.Add(po);

        // Aprobar automáticamente si es un borrador enviado
        if (req.EnviarInmediatamente == true)
        {
            po.Estado = EstadoOrdenCompra.Enviada;
            po.FechaEnvioProveedor = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("PO {NumeroPO} creada. Proveedor={Proveedor}, Total={Total:C}",
            numeroPO, req.ProveedorNombre, po.MontoTotal);

        return CreatedAtAction(nameof(ObtenerPO), new { id = po.Id },
            new { po.Id, po.NumeroPO, po.Estado, po.MontoTotal });
    }

    // ── POST /api/compras/po/{id}/enviar ─────────────────────────────────────
    /// <summary>Envía una Orden de Compra en borrador al proveedor.</summary>
    [HttpPost("po/{id:guid}/enviar", Name = "EnviarOrdenCompra")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnviarPO(Guid id, CancellationToken cancellationToken)
    {
        var po = await _db.OrdenesCompra.FindAsync([id], cancellationToken);
        if (po == null) return NotFound(new { mensaje = $"PO {id} no encontrada." });
        if (po.Estado != EstadoOrdenCompra.Borrador)
            return BadRequest(new { mensaje = $"Solo una PO en Borrador puede enviarse. Estado actual: {po.Estado}." });

        po.Estado = EstadoOrdenCompra.Enviada;
        po.FechaEnvioProveedor = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { po.Id, po.NumeroPO, Estado = po.Estado.ToString(), po.FechaEnvioProveedor });
    }

    // ── PUT /api/compras/po/{id}/recibir ──────────────────────────────────────
    /// <summary>Registra recepción de mercadería en bodega WMS.</summary>
    [HttpPut("po/{id:guid}/recibir", Name = "RecibirMercaderia")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecibirMercaderia(
        Guid id, [FromBody] RecibirMercaderiaRequest req, CancellationToken cancellationToken)
    {
        var po = await _db.OrdenesCompra
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (po == null) return NotFound(new { mensaje = $"PO {id} no encontrada." });
        if (po.Estado != EstadoOrdenCompra.Enviada && po.Estado != EstadoOrdenCompra.RecibidaParcial)
            return BadRequest(new { mensaje = $"La PO en estado {po.Estado} no puede recibir mercadería." });

        int totalOrdenado = po.Items.Sum(i => i.CantidadSolicitada);
        int totalRecibido = req.CantidadRecibida;

        foreach (var item in po.Items)
            item.CantidadRecibida = Math.Min(item.CantidadSolicitada, totalRecibido);

        po.Estado = totalRecibido >= totalOrdenado
            ? EstadoOrdenCompra.RecibidaCompleta
            : EstadoOrdenCompra.RecibidaParcial;

        po.FechaRecepcionReal = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { po.Id, po.NumeroPO, Estado = po.Estado.ToString(), totalOrdenado, totalRecibido });
    }

    // ── POST /api/compras/facturas ────────────────────────────────────────────
    /// <summary>Registra una factura de proveedor asociada a una PO.</summary>
    [HttpPost("facturas", Name = "RegistrarFacturaProveedor")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegistrarFactura(
        [FromBody] RegistrarFacturaRequest req, CancellationToken cancellationToken)
    {
        var po = await _db.OrdenesCompra.FindAsync([req.OrdenCompraId], cancellationToken);
        if (po == null) return NotFound(new { mensaje = $"PO {req.OrdenCompraId} no encontrada." });

        var factura = new FacturaProveedor
        {
            Id = Guid.NewGuid(),
            OrdenCompraId = req.OrdenCompraId,
            ProveedorId = po.ProveedorId,
            NumeroFactura = req.NumeroFactura,
            MontoSubTotal = req.SubTotal,
            MontoIva = Math.Round(req.SubTotal * 0.13m, 2),
            MontoTotal = req.MontoTotal,
            FechaEmision = req.FechaEmision ?? DateTime.UtcNow,
            FechaVencimiento = req.FechaVencimiento ?? DateTime.UtcNow.AddDays(30),
            AprobadaParaPago = false,
        };

        _db.FacturasProveedor.Add(factura);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(ObtenerPO), new { id = po.Id },
            new { factura.Id, factura.NumeroFactura, factura.MontoTotal, factura.AprobadaParaPago });
    }

    // ── POST /api/compras/match ───────────────────────────────────────────────
    /// <summary>Ejecuta el Match Tridireccional (PO - Factura - Bodega WMS).</summary>
    [HttpPost("match", Name = "EjecutarMatch3Way")]
    [ProducesResponseType(typeof(MatchTridireccional), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EjecutarMatch(
        [FromBody] Validar3WayMatchRequest req, CancellationToken cancellationToken)
    {
        var match = await _matching.EjecutarMatchAsync(req, cancellationToken);

        // Si el match es conforme, generar asiento contable automático
        if (match.Resultado == Estado3WayMatch.Conforme && match.AutorizadoParaPago)
        {
            var po = await _db.OrdenesCompra.FindAsync([req.OrdenCompraId], cancellationToken);
            var factura = await _db.FacturasProveedor.FindAsync([req.FacturaProveedorId], cancellationToken);
            if (po != null && factura != null)
            {
                await _contabilidad.GenerarAsientoCompraAsync(
                    po.Id, factura.MontoSubTotal, factura.MontoIva, cancellationToken);
            }
        }

        return Ok(match);
    }

    // ── GET /api/compras/match/historial ──────────────────────────────────────
    /// <summary>Historial de todos los matches tridireccionales.</summary>
    [HttpGet("match/historial", Name = "HistorialMatch3Way")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> HistorialMatch(CancellationToken cancellationToken)
    {
        var historial = await _matching.ListarHistorialMatchesAsync(cancellationToken);
        return Ok(historial);
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────────
public record ItemPORequest(string Sku, string NombreProducto, int Cantidad, decimal PrecioUnitario);

public record CrearPORequest(
    Guid ProveedorId,
    string ProveedorNombre,
    string? TerminosPago,
    string? Observaciones,
    DateTime? FechaEntregaEsperada,
    bool? EnviarInmediatamente,
    List<ItemPORequest> Items);

public record RecibirMercaderiaRequest(int CantidadRecibida, string? UbicacionBodega, string? Notas);

public record RegistrarFacturaRequest(
    Guid OrdenCompraId,
    string NumeroFactura,
    decimal SubTotal,
    decimal MontoTotal,
    DateTime? FechaEmision,
    DateTime? FechaVencimiento);
