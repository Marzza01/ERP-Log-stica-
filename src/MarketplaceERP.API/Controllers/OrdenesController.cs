/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Claims;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador OMS (Order Management System) — Célula B.
/// Gestiona el ciclo de vida de órdenes con máquina de estados de 6 fases.
/// Requisito: PDF1 R-OMS-01 a R-OMS-08
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class OrdenesController : ControllerBase
{
    private readonly MarketplaceERP.Finance.Data.FinanceDbContext _db;
    private readonly IContabilidadService _contabilidad;
    private readonly ILogger<OrdenesController> _logger;

    public OrdenesController(
        MarketplaceERP.Finance.Data.FinanceDbContext db,
        IContabilidadService contabilidad,
        ILogger<OrdenesController> logger)
    {
        _db = db;
        _contabilidad = contabilidad;
        _logger = logger;
    }

    // ── GET /api/ordenes ──────────────────────────────────────────────────────
    /// <summary>Lista todas las órdenes con KPIs de tiempo de ciclo.</summary>
    [HttpGet(Name = "ListarOrdenes")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? estado = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Ordenes
            .Include(o => o.Items)
            .AsQueryable();

        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorIdClaim = User.FindFirstValue("VendedorId");
            if (Guid.TryParse(vendedorIdClaim, out var vendedorId))
            {
                query = query.Where(o => o.VendedorId == vendedorId);
            }
            else
            {
                return Ok(new { total = 0, pagina, tamano, ordenes = Array.Empty<object>() });
            }
        }

        if (!string.IsNullOrWhiteSpace(estado) && Enum.TryParse<EstadoOrden>(estado, true, out var estadoEnum))
            query = query.Where(o => o.Estado == estadoEnum);

        var total = await query.CountAsync(cancellationToken);
        var ordenes = await query
            .OrderByDescending(o => o.FechaCreacion)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .Select(o => new
            {
                o.Id,
                o.NumeroOrden,
                Estado = o.Estado.ToString(),
                TipoCliente = o.TipoCliente.ToString(),
                o.MontoTotal,
                o.Moneda,
                o.TipoComprobante,
                o.Transportista,
                o.GuiaEnvio,
                o.FechaCreacion,
                o.FechaAprobacion,
                o.FechaEnvio,
                o.FechaEntregaEstimada,
                TotalItems = o.Items.Count,
                // KPIs de ciclo
                MinutosEnEstado = (int)(DateTime.UtcNow - o.FechaCreacion).TotalMinutes,
            })
            .ToListAsync(cancellationToken);

        return Ok(new { total, pagina, tamano, ordenes });
    }

    // ── GET /api/ordenes/{id} ─────────────────────────────────────────────────
    [HttpGet("{id:guid}", Name = "ObtenerOrden")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken cancellationToken)
    {
        var query = _db.Ordenes
            .Include(o => o.Items)
            .Include(o => o.SplitPago)
            .AsQueryable();

        if (User.IsInRole("Vendedor") && !User.IsInRole("Admin"))
        {
            var vendedorIdClaim = User.FindFirstValue("VendedorId");
            if (!Guid.TryParse(vendedorIdClaim, out var vendedorId))
            {
                return NotFound(new { mensaje = $"Orden {id} no encontrada." });
            }
            query = query.Where(o => o.VendedorId == vendedorId);
        }

        var orden = await query.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (orden == null) return NotFound(new { mensaje = $"Orden {id} no encontrada." });
        return Ok(orden);
    }

    // ── POST /api/ordenes ─────────────────────────────────────────────────────
    /// <summary>Crea una nueva orden en estado Pendiente.</summary>
    [HttpPost(Name = "CrearOrden")]
    [Authorize(Roles = "Admin,Comprador")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Crear([FromBody] CrearOrdenRequest req, CancellationToken cancellationToken)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { mensaje = "Una orden debe tener al menos un ítem." });

        Guid compradorId = req.CompradorId;
        if (User.IsInRole("Comprador"))
        {
            var compradorClaim = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);

            if (Guid.TryParse(compradorClaim, out var parsedCompradorId))
            {
                compradorId = parsedCompradorId;
            }
        }

        var orden = new Orden
        {
            Id = Guid.NewGuid(),
            NumeroOrden = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(10000, 99999)}",
            CompradorId = compradorId,
            VendedorId = req.VendedorId,
            Estado = EstadoOrden.Pendiente,
            TipoCliente = req.TipoCliente,
            TipoComprobante = req.TipoComprobante ?? "FacturaConsumidorFinal",
            CondicionPago = req.CondicionPago ?? "Inmediato",
            Transportista = req.Transportista ?? "LOGIMKT Express",
            ZonaEnvio = req.ZonaEnvio ?? "San Salvador y AMSS",
            DireccionEntrega = req.DireccionEntrega ?? "",
            FechaCreacion = DateTime.UtcNow,
            Moneda = "USD",
        };

        decimal subtotal = 0m;
        foreach (var item in req.Items)
        {
            var lineaOrden = new ItemOrden
            {
                Id = Guid.NewGuid(),
                OrdenId = orden.Id,
                ProductoId = item.ProductoId,
                Cantidad = item.Cantidad,
                PrecioUnitario = item.PrecioUnitario,
                SkuReferencia = item.Sku ?? "",
                NombreProducto = item.Nombre ?? "Producto",
            };
            orden.Items.Add(lineaOrden);
            subtotal += item.Cantidad * item.PrecioUnitario;
        }

        orden.SubTotal = subtotal;
        orden.ImpuestoIva = Math.Round(subtotal * 0.13m, 2);
        orden.CostoEnvio = req.CostoEnvio ?? 5m;
        orden.MontoTotal = orden.SubTotal + orden.ImpuestoIva + orden.CostoEnvio;

        _db.Ordenes.Add(orden);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Orden {NumeroOrden} creada. Total={Total:C}", orden.NumeroOrden, orden.MontoTotal);

        return CreatedAtAction(nameof(Obtener), new { id = orden.Id }, new { orden.Id, orden.NumeroOrden, orden.MontoTotal });
    }

    // ── PUT /api/ordenes/{id}/estado ──────────────────────────────────────────
    /// <summary>Transiciona la orden entre estados del OMS.</summary>
    [HttpPut("{id:guid}/estado", Name = "TransicionarEstadoOrden")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TransicionarEstado(
        Guid id, [FromBody] TransicionEstadoRequest req, CancellationToken cancellationToken)
    {
        var orden = await _db.Ordenes.FindAsync([id], cancellationToken);
        if (orden == null) return NotFound(new { mensaje = $"Orden {id} no encontrada." });

        if (!Enum.TryParse<EstadoOrden>(req.NuevoEstado, true, out var nuevoEstado))
            return BadRequest(new { mensaje = $"Estado '{req.NuevoEstado}' no es válido." });

        // Validación de máquina de estados
        var transicionesValidas = new Dictionary<EstadoOrden, EstadoOrden[]>
        {
            [EstadoOrden.Pendiente]    = [EstadoOrden.Aprobada, EstadoOrden.Cancelada],
            [EstadoOrden.Aprobada]     = [EstadoOrden.EnProceso, EstadoOrden.Cancelada],
            [EstadoOrden.EnProceso]    = [EstadoOrden.Enviada, EstadoOrden.Cancelada],
            [EstadoOrden.Enviada]      = [EstadoOrden.Entregada],
            [EstadoOrden.Entregada]    = [],
            [EstadoOrden.Cancelada]    = [],
        };

        if (!transicionesValidas.TryGetValue(orden.Estado, out var permitidos) || !permitidos.Contains(nuevoEstado))
            return BadRequest(new { mensaje = $"No se puede pasar de {orden.Estado} a {nuevoEstado}." });

        var estadoAnterior = orden.Estado;
        orden.Estado = nuevoEstado;

        // Timestamps del ciclo OMS
        switch (nuevoEstado)
        {
            case EstadoOrden.Aprobada:
                orden.FechaAprobacion = DateTime.UtcNow;
                // Generar asiento contable automático al aprobar
                await _contabilidad.GenerarAsientoVentaAsync(
                    orden.Id, orden.SubTotal, orden.ImpuestoIva, orden.TipoComprobante, cancellationToken);
                break;
            case EstadoOrden.Enviada:
                orden.FechaEnvio = DateTime.UtcNow;
                orden.GuiaEnvio = req.GuiaEnvio ?? $"GU-{Random.Shared.Next(100000, 999999)}";
                break;
            case EstadoOrden.Entregada:
                orden.FechaEntregaReal = DateTime.UtcNow;
                break;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Orden {NumeroOrden}: {De} → {A}", orden.NumeroOrden, estadoAnterior, nuevoEstado);

        return Ok(new
        {
            orden.Id,
            orden.NumeroOrden,
            EstadoAnterior = estadoAnterior.ToString(),
            NuevoEstado = nuevoEstado.ToString(),
            orden.FechaAprobacion,
            orden.FechaEnvio,
            orden.FechaEntregaReal
        });
    }

    // ── GET /api/ordenes/kpis ─────────────────────────────────────────────────
    /// <summary>KPIs del OMS: fill rate, tiempos de ciclo, semáforo.</summary>
    [HttpGet("kpis", Name = "KpisOms")]
    [Authorize(Roles = "Admin,Vendedor")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ObtenerKpis(CancellationToken cancellationToken)
    {
        var hoy = DateTime.UtcNow.Date;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var ordenes = await _db.Ordenes.ToListAsync(cancellationToken);

        var totalMes    = ordenes.Count(o => o.FechaCreacion >= inicioMes);
        var entregadas  = ordenes.Count(o => o.Estado == EstadoOrden.Entregada && o.FechaCreacion >= inicioMes);
        var canceladas  = ordenes.Count(o => o.Estado == EstadoOrden.Cancelada && o.FechaCreacion >= inicioMes);

        double fillRate  = totalMes > 0 ? (double)entregadas / totalMes * 100.0 : 0;
        double cancelRate = totalMes > 0 ? (double)canceladas / totalMes * 100.0 : 0;

        // Semáforo: Verde ≥95%, Amarillo ≥85%, Rojo <85%
        string semaforoFillRate = fillRate >= 95 ? "Verde" : fillRate >= 85 ? "Amarillo" : "Rojo";

        var entregadasConFecha = ordenes
            .Where(o => o.Estado == EstadoOrden.Entregada && o.FechaEntregaReal.HasValue)
            .ToList();

        double tiempoCicloPromedioHoras = entregadasConFecha.Count > 0
            ? entregadasConFecha.Average(o => (o.FechaEntregaReal!.Value - o.FechaCreacion).TotalHours)
            : 0;

        return Ok(new
        {
            periodo = $"{inicioMes:yyyy-MM-dd} a {hoy:yyyy-MM-dd}",
            totalOrdenesMes = totalMes,
            ordenesEntregadas = entregadas,
            ordenesCanceladas = canceladas,
            fillRatePorcentaje = Math.Round(fillRate, 2),
            tasaCancelacionPorcentaje = Math.Round(cancelRate, 2),
            semaforoFillRate,
            tiempoCicloPromedioHoras = Math.Round(tiempoCicloPromedioHoras, 1),
            kpiMeta = new { fillRateObjetivo = 95.0, semaforo = "Verde si ≥95%, Amarillo ≥85%, Rojo <85%" }
        });
    }
}

// ── Request DTOs ───────────────────────────────────────────────────────────────
public record ItemOrdenRequest(Guid ProductoId, string? Sku, string? Nombre, int Cantidad, decimal PrecioUnitario);

public record CrearOrdenRequest(
    Guid CompradorId,
    Guid VendedorId,
    TipoCliente TipoCliente,
    string? TipoComprobante,
    string? CondicionPago,
    string? Transportista,
    string? ZonaEnvio,
    string? DireccionEntrega,
    decimal? CostoEnvio,
    List<ItemOrdenRequest> Items);

public record TransicionEstadoRequest(string NuevoEstado, string? GuiaEnvio);
