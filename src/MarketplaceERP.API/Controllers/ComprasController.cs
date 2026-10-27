/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Core.Data;
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
    private readonly IInventoryService _inventory;
    private readonly CoreDbContext _coreDb;
    private readonly ILogger<ComprasController> _logger;

    public ComprasController(
        MarketplaceERP.Finance.Data.FinanceDbContext db,
        ITresWayMatchingService matching,
        IContabilidadService contabilidad,
        IInventoryService inventory,
        CoreDbContext coreDb,
        ILogger<ComprasController> logger)
    {
        _db = db;
        _matching = matching;
        _contabilidad = contabilidad;
        _inventory = inventory;
        _coreDb = coreDb;
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
        if (req.ProveedorId == Guid.Empty)
            return BadRequest(new { mensaje = "Debe seleccionar un proveedor del maestro corporativo." });

        var proveedor = await _coreDb.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.ProveedorId && p.Activo, cancellationToken);
        if (proveedor is null)
            return BadRequest(new { mensaje = "El proveedor seleccionado no existe o está inactivo en el maestro corporativo." });

        var nombreProveedor = !string.IsNullOrWhiteSpace(proveedor.RazonSocial)
            ? proveedor.RazonSocial
            : proveedor.NombreComercial;
        var terminosProveedor = proveedor.PlazoCreditoDias <= 0
            ? "Contado"
            : $"Crédito {proveedor.PlazoCreditoDias} días";

        var numeroPO = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

        var po = new OrdenCompra
        {
            Id = Guid.NewGuid(),
            NumeroPO = numeroPO,
            ProveedorId = req.ProveedorId,
            ProveedorNombre = nombreProveedor,
            Estado = EstadoOrdenCompra.Borrador,
            TerminosPago = string.IsNullOrWhiteSpace(req.TerminosPago) ? terminosProveedor : req.TerminosPago,
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
            numeroPO, po.ProveedorNombre, po.MontoTotal);

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
    /// <summary>
    /// Registra recepción real por ítems en WMS y actualiza el stock físico del módulo Catalog.
    /// La operación usa OperacionId + CantidadAcumuladaEsperada para soportar reintentos
    /// sin duplicar inventario ni cantidades recibidas de la PO.
    /// </summary>
    [HttpPut("po/{id:guid}/recibir", Name = "RecibirMercaderia")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecibirMercaderia(
        Guid id, [FromBody] RecibirMercaderiaRequest req, CancellationToken cancellationToken)
    {
        if (req.Items is null || req.Items.Count == 0)
            return BadRequest(new { mensaje = "Debe indicar al menos un ítem a recibir." });

        if (req.Items.Select(x => x.ItemOrdenCompraId).Distinct().Count() != req.Items.Count)
            return BadRequest(new { mensaje = "No puede enviar el mismo ítem de PO más de una vez en la misma recepción." });

        var po = await _db.OrdenesCompra
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (po == null) return NotFound(new { mensaje = $"PO {id} no encontrada." });
        if (po.Estado != EstadoOrdenCompra.Enviada &&
            po.Estado != EstadoOrdenCompra.RecibidaParcial &&
            po.Estado != EstadoOrdenCompra.RecibidaCompleta)
            return BadRequest(new { mensaje = $"La PO en estado {po.Estado} no puede recibir mercadería." });

        // Validar toda la solicitud antes de mutar Catalog o Finance.
        foreach (var recibido in req.Items)
        {
            var item = po.Items.FirstOrDefault(i => i.Id == recibido.ItemOrdenCompraId);
            if (item is null)
                return BadRequest(new { mensaje = $"El ítem {recibido.ItemOrdenCompraId} no pertenece a la PO {po.NumeroPO}." });
            if (recibido.CantidadRecibida <= 0)
                return BadRequest(new { mensaje = $"La cantidad recibida para {item.Sku} debe ser mayor que cero." });
            if (recibido.OperacionId == Guid.Empty)
                return BadRequest(new { mensaje = $"OperacionId es obligatorio para {item.Sku}." });
            if (recibido.UbicacionAlmacenId == Guid.Empty)
                return BadRequest(new { mensaje = $"Debe seleccionar una ubicación WMS para {item.Sku}." });
            if (recibido.CantidadAcumuladaEsperada < item.CantidadRecibida)
                return BadRequest(new { mensaje = $"La recepción de {item.Sku} está desactualizada. Refresque la PO." });
            if (recibido.CantidadAcumuladaEsperada > item.CantidadSolicitada)
                return BadRequest(new { mensaje = $"La recepción de {item.Sku} excede la cantidad solicitada ({item.CantidadSolicitada})." });

            // Si Finance ya alcanzó el acumulado esperado, este ítem corresponde a
            // un reintento posterior a una respuesta perdida: se considera idempotente.
            if (item.CantidadRecibida == recibido.CantidadAcumuladaEsperada)
                continue;

            var deltaEsperado = recibido.CantidadAcumuladaEsperada - item.CantidadRecibida;
            if (deltaEsperado != recibido.CantidadRecibida)
            {
                return BadRequest(new
                {
                    mensaje = $"La cantidad para {item.Sku} cambió desde que abrió la recepción. " +
                              $"Pendiente a aplicar para este acumulado: {deltaEsperado}. Refresque la PO."
                });
            }
        }

        var resultados = new List<object>();
        var huboCambios = false;

        foreach (var recibido in req.Items)
        {
            var item = po.Items.Single(i => i.Id == recibido.ItemOrdenCompraId);

            if (item.CantidadRecibida == recibido.CantidadAcumuladaEsperada)
            {
                resultados.Add(new
                {
                    item.Id,
                    item.Sku,
                    cantidadAplicada = 0,
                    cantidadAcumulada = item.CantidadRecibida,
                    yaProcesada = true
                });
                continue;
            }

            var inventario = await _inventory.RecibirInboundAsync(
                item.Sku,
                recibido.CantidadRecibida,
                recibido.UbicacionAlmacenId,
                po.Id,
                item.Id,
                recibido.OperacionId,
                req.Notas,
                cancellationToken);

            if (!inventario.Exitoso)
                return BadRequest(new { mensaje = inventario.Mensaje, sku = item.Sku });

            // Si Catalog ya había aplicado el movimiento pero Finance no logró guardar,
            // el mismo request repara Finance sin duplicar stock.
            item.ProductoId = inventario.ProductoId;
            item.CantidadRecibida = recibido.CantidadAcumuladaEsperada;
            huboCambios = true;

            resultados.Add(new
            {
                item.Id,
                item.Sku,
                cantidadAplicada = recibido.CantidadRecibida,
                cantidadAcumulada = item.CantidadRecibida,
                yaProcesada = inventario.YaProcesada,
                stockFisicoId = inventario.StockFisicoId,
                stockUbicacion = inventario.CantidadTotalUbicacion
            });
        }

        var totalOrdenado = po.Items.Sum(i => i.CantidadSolicitada);
        var totalRecibido = po.Items.Sum(i => i.CantidadRecibida);

        po.Estado = totalRecibido >= totalOrdenado
            ? EstadoOrdenCompra.RecibidaCompleta
            : EstadoOrdenCompra.RecibidaParcial;

        if (huboCambios)
            po.FechaRecepcionReal = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recepción Inbound PO {NumeroPO}. Recibido={Recibido}/{Ordenado}, Estado={Estado}",
            po.NumeroPO, totalRecibido, totalOrdenado, po.Estado);

        return Ok(new
        {
            po.Id,
            po.NumeroPO,
            Estado = po.Estado.ToString(),
            totalOrdenado,
            totalRecibido,
            items = resultados
        });
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

public record RecibirMercaderiaItemRequest(
    Guid ItemOrdenCompraId,
    int CantidadRecibida,
    int CantidadAcumuladaEsperada,
    Guid UbicacionAlmacenId,
    Guid OperacionId);

public record RecibirMercaderiaRequest(
    List<RecibirMercaderiaItemRequest> Items,
    string? Notas);

public record RegistrarFacturaRequest(
    Guid OrdenCompraId,
    string NumeroFactura,
    decimal SubTotal,
    decimal MontoTotal,
    DateTime? FechaEmision,
    DateTime? FechaVencimiento);
