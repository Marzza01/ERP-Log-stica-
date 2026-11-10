/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Claims;
using MarketplaceERP.API.Contracts;
using MarketplaceERP.API.Services;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador OMS (Order Management System) — Célula B.
/// Gestiona el ciclo de vida de órdenes con máquina de estados de 6 fases.
/// Requisito: PDF1 R-OMS-01 a R-OMS-08
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
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
    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin,Comprador")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(Guid id, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();
        var actorClaims = User.FindAll(ClaimTypes.NameIdentifier).Select(c => c.Value).ToArray();
        if (actorClaims.Length == 0 || actorClaims.Any(value =>
                !Guid.TryParse(value, out var actorId) || actorId == Guid.Empty) ||
            actorClaims.Select(Guid.Parse).Distinct().Count() != 1)
            return Unauthorized();

        if (!User.IsInRole("Admin"))
        {
            if (!User.IsInRole("Comprador")) return Forbid();
            var compradorId = Guid.Parse(actorClaims[0]);
            // Apply ownership to the database query, before loading items or returning any data.
            var propia = await _db.Ordenes.AsNoTracking()
                .Where(o => o.Id == id && o.CompradorId == compradorId)
                .Include(o => o.Items)
                .SingleOrDefaultAsync(cancellationToken);
            // Same response for another buyer's order and a nonexistent order.
            if (propia is null) return NotFound();
            return Ok(new OrdenCompradorResponse(
                propia.Id, propia.NumeroOrden, propia.Estado.ToString(), propia.TipoCliente.ToString(),
                propia.SubTotal, propia.ImpuestoIva, propia.CostoEnvio, propia.MontoTotal, propia.Moneda,
                propia.TipoComprobante, propia.NumeroComprobanteFiscal, propia.CondicionPago,
                propia.Transportista, propia.GuiaEnvio, propia.ZonaEnvio, propia.DireccionEntrega,
                propia.FechaCreacion, propia.FechaAprobacion, propia.FechaEnvio,
                propia.FechaEntregaEstimada, propia.FechaEntregaReal, propia.FechaCancelacion,
                propia.Items.Select(i => new ItemOrdenCompradorResponse(
                    i.ProductoId, i.SkuReferencia, i.NombreProducto, i.Cantidad, i.PrecioUnitario, i.Subtotal)).ToArray()));
        }

        var orden = await _db.Ordenes
            .Include(o => o.Items)
            .Include(o => o.SplitPago)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (orden == null) return NotFound(new { mensaje = $"Orden {id} no encontrada." });
        return Ok(orden);
    }

    // ── POST /api/ordenes ─────────────────────────────────────────────────────
    /// <summary>Crea una nueva orden en estado Pendiente.</summary>
    [HttpPost(Name = "CrearOrden")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Crear([FromBody] CrearOrdenRequest req, CancellationToken cancellationToken)
    {
        if (req.Items == null || req.Items.Count == 0)
            return BadRequest(new { mensaje = "Una orden debe tener al menos un ítem." });

        // La creación manual sigue reservada al rol Admin, pero nunca debe
        // aceptar cantidades negativas, dinero inválido o totales que no
        // pueden almacenarse en numeric(18,2). Esto NO habilita un pago.
        const decimal montoMaximo = 9999999999999999.99m;
        static bool ImporteValido(decimal valor) =>
            valor >= 0m && valor <= montoMaximo &&
            decimal.Round(valor, 2) == valor;

        if (req.CompradorId == Guid.Empty || req.VendedorId == Guid.Empty ||
            !Enum.IsDefined(req.TipoCliente) ||
            req.Items.Any(item => item.ProductoId == Guid.Empty ||
                item.Cantidad <= 0 || !ImporteValido(item.PrecioUnitario)) ||
            (req.CostoEnvio.HasValue && !ImporteValido(req.CostoEnvio.Value)))
            return BadRequest(new { mensaje = "Datos de orden, cantidades o importes no válidos." });

        decimal subtotalValidado = 0m;
        try
        {
            foreach (var item in req.Items)
                subtotalValidado = checked(subtotalValidado + item.Cantidad * item.PrecioUnitario);
        }
        catch (OverflowException)
        {
            return BadRequest(new { mensaje = "Importe total excede los límites permitidos." });
        }

        var ivaValidado = decimal.Round(subtotalValidado * 0.13m, 2);
        var envioValidado = req.CostoEnvio ?? 5m;
        if (!ImporteValido(subtotalValidado) ||
            !ImporteValido(ivaValidado) ||
            !ImporteValido(subtotalValidado + ivaValidado + envioValidado))
            return BadRequest(new { mensaje = "Importe total excede los límites permitidos." });

        // El identificador visible usa el mismo GUID que la PK de la orden.
        // No usar un rango aleatorio de 90,000 valores compartido por dia.
        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{ordenId:N}",
            CompradorId = req.CompradorId,
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
        }

        orden.SubTotal = subtotalValidado;
        orden.ImpuestoIva = ivaValidado;
        orden.CostoEnvio = envioValidado;
        orden.MontoTotal = orden.SubTotal + orden.ImpuestoIva + orden.CostoEnvio;

        _db.Ordenes.Add(orden);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Orden {NumeroOrden} creada. Total={Total:C}", orden.NumeroOrden, orden.MontoTotal);

        return CreatedAtAction(nameof(Obtener), new { id = orden.Id }, new { orden.Id, orden.NumeroOrden, orden.MontoTotal });
    }

    // ── PUT /api/ordenes/{id}/estado ──────────────────────────────────────────
    /// <summary>Transiciona la orden entre estados del OMS.</summary>
    [HttpPut("{id:guid}/estado", Name = "TransicionarEstadoOrden")]
    [PaymentOperationsGuard]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TransicionarEstado(
        Guid id, [FromBody] TransicionEstadoRequest req, CancellationToken cancellationToken)
    {
        // Una transición OMS y su asiento contable se deben confirmar juntos.
        // PostgreSQL bloquea la fila ANTES de leer su estado, de forma que un
        // segundo request concurrente observe el estado ya confirmado.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var orden = _db.Database.IsRelational()
            ? (await _db.Ordenes.FromSqlInterpolated(
                $@"SELECT * FROM finance.ordenes WHERE ""Id"" = {id} FOR UPDATE")
                .AsTracking().ToListAsync(cancellationToken)).SingleOrDefault()
            : await _db.Ordenes.FindAsync([id], cancellationToken);
        if (orden == null) return NotFound(new { mensaje = $"Orden {id} no encontrada." });

        if (!Enum.TryParse<EstadoOrden>(req.NuevoEstado, true, out var nuevoEstado))
            return BadRequest(new { mensaje = $"Estado '{req.NuevoEstado}' no es válido." });

        if (orden.Estado == nuevoEstado)
            return Conflict(new
            {
                mensaje = "La orden ya se encuentra en el estado solicitado.",
                codigo = "ESTADO_YA_APLICADO"
            });

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

        // Un error del asiento deja intacto el estado en PostgreSQL.
        try
        {
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
                // Si OMS carece de guia externa, usar un identificador estable
                // derivado del Id de la orden, no un valor aleatorio corto.
                orden.GuiaEnvio = req.GuiaEnvio ?? $"GU-{orden.Id:N}";
                break;
            case EstadoOrden.Entregada:
                orden.FechaEntregaReal = DateTime.UtcNow;
                break;
        }

            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException pg &&
            pg.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation &&
            pg.ConstraintName == "UX_FIN08_asiento_venta_orden")
        {
            return Conflict(new
            {
                mensaje = "La venta ya fue contabilizada por otra solicitud; consulte el estado de la orden.",
                codigo = "ASIENTO_VENTA_DUPLICADO"
            });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("La orden ya tiene un asiento Venta", StringComparison.Ordinal))
        {
            return Conflict(new
            {
                mensaje = "La orden ya contiene un asiento de Venta con datos incompatibles; requiere conciliación.",
                codigo = "ASIENTO_VENTA_INCONSISTENTE"
            });
        }

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
    [Authorize(Roles = "Admin")]
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
