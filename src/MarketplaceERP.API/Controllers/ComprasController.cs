/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Controlador de Compras / Procurement — Célula B.
/// Gestiona Órdenes de Compra (PO), Facturas de Proveedor y Match Tridireccional.
/// Requisito: PDF1 R-COMP-01 a R-COMP-06
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
[Produces("application/json")]
public class ComprasController : ControllerBase
{
    // finance numeric(18,2): no persistir importes negativos, fracciones de
    // centavo ni montos que PostgreSQL no pueda representar.
    private const decimal MontoMaximo = 9999999999999999.99m;
    private static bool ImporteValido(decimal monto) =>
        monto >= 0m && monto <= MontoMaximo && decimal.Round(monto, 2) == monto;

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

        if (req.TerminosPago?.Length > 100 || req.Observaciones?.Length > 1000)
            return BadRequest(new { mensaje = "Términos de pago u observaciones exceden la longitud permitida." });

        decimal subtotal = 0m;
        // Una PO no puede contener el mismo SKU en varias líneas: al recibir
        // mercancía se debe poder conciliar una cantidad única por producto.
        var skusEnOrden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in req.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Sku) || item.Sku.Trim().Length > 50 ||
                string.IsNullOrWhiteSpace(item.NombreProducto) || item.NombreProducto.Trim().Length > 300)
                return BadRequest(new { mensaje = "Cada ítem requiere SKU (máximo 50) y nombre (máximo 300)." });

            if (!skusEnOrden.Add(item.Sku.Trim()))
                return BadRequest(new { mensaje = "Una orden de compra no puede repetir el mismo SKU en varias líneas." });

            if (item.Cantidad <= 0 || !ImporteValido(item.PrecioUnitario) || item.PrecioUnitario == 0m)
                return BadRequest(new { mensaje = "La cantidad y el precio unitario deben ser positivos; solo dos decimales." });

            try
            {
                var totalLinea = checked(item.Cantidad * item.PrecioUnitario);
                subtotal = checked(subtotal + totalLinea);
            }
            catch (OverflowException)
            {
                return BadRequest(new { mensaje = "El importe de la orden supera la precisión monetaria admitida." });
            }

            if (!ImporteValido(subtotal))
                return BadRequest(new { mensaje = "El importe de la orden supera el máximo monetario admitido." });
        }

        decimal iva;
        decimal montoTotal;
        try
        {
            iva = decimal.Round(checked(subtotal * 0.13m), 2);
            montoTotal = checked(subtotal + iva);
        }
        catch (OverflowException)
        {
            return BadRequest(new { mensaje = "El total de la orden supera la precisión monetaria admitida." });
        }

        if (!ImporteValido(montoTotal))
            return BadRequest(new { mensaje = "El total de la orden supera el máximo monetario admitido." });

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

        // El número de PO no debe depender de cuatro cifras aleatorias:
        // existe índice UNIQUE y las colisiones son probables bajo carga.
        // La referencia pública incluye el mismo GUID persistente de la PO.
        var poId = Guid.NewGuid();
        var numeroPO = $"PO-{DateTime.UtcNow:yyyyMMdd}-{poId:N}";

        var po = new OrdenCompra
        {
            Id = poId,
            NumeroPO = numeroPO,
            ProveedorId = req.ProveedorId,
            ProveedorNombre = nombreProveedor,
            Estado = EstadoOrdenCompra.Borrador,
            TerminosPago = string.IsNullOrWhiteSpace(req.TerminosPago) ? terminosProveedor : req.TerminosPago,
            Observaciones = req.Observaciones ?? string.Empty,
            FechaEmision = DateTime.UtcNow,
            FechaEntregaEsperada = req.FechaEntregaEsperada ?? DateTime.UtcNow.AddDays(15),
        };

        foreach (var item in req.Items)
        {
            po.Items.Add(new ItemOrdenCompra
            {
                Id = Guid.NewGuid(),
                OrdenCompraId = po.Id,
                Sku = item.Sku.Trim(),
                NombreProducto = item.NombreProducto.Trim(),
                CantidadSolicitada = item.Cantidad,
                PrecioUnitarioAcordado = item.PrecioUnitario,
                CantidadRecibida = 0,
            });
        }

        po.SubTotal = subtotal;
        po.ImpuestoIva = iva;
        po.MontoTotal = montoTotal;

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

        // Una PO se procesa por una sola petición a la vez en todas las
        // instancias del API. Abrir explícitamente la conexión fija el candado
        // de sesión PostgreSQL a la misma conexión hasta terminar la recepción.
        // Los SaveChanges de cada ítem conservan su commit individual.
        if (!_db.Database.IsRelational())
            return await RecibirMercaderiaCore(id, req, cancellationToken);

        await _db.Database.OpenConnectionAsync(cancellationToken);
        var candadoObtenido = false;
        try
        {
            candadoObtenido = await _db.Database.SqlQueryRaw<bool>(
                    "SELECT pg_try_advisory_lock(hashtext('erp-po-recibir'), hashtext({0})) AS \\"Value\\"",
                    id.ToString("N"))
                .SingleAsync(cancellationToken);

            if (!candadoObtenido)
                return Conflict(new
                {
                    mensaje = "Otra recepción de esta orden está en curso. Refresque la PO e intente nuevamente."
                });

            return await RecibirMercaderiaCore(id, req, cancellationToken);
        }
        finally
        {
            try
            {
                if (candadoObtenido)
                    await _db.Database.SqlQueryRaw<bool>(
                            "SELECT pg_advisory_unlock(hashtext('erp-po-recibir'), hashtext({0})) AS \\"Value\\"",
                            id.ToString("N"))
                        .SingleAsync(CancellationToken.None);
            }
            finally
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }

    private async Task<IActionResult> RecibirMercaderiaCore(
        Guid id, RecibirMercaderiaRequest req, CancellationToken cancellationToken)
    {
        // Se consulta Finance después de obtener el candado, nunca antes.
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
                // Un acumulado igual no prueba por sí solo que este
                // OperacionId se haya procesado con los mismos datos.
                // Consultar el ledger WMS sin generar una segunda entrada.
                var confirmado = await _inventory.ConfirmarInboundProcesadoAsync(
                    item.Sku,
                    recibido.CantidadRecibida,
                    recibido.UbicacionAlmacenId,
                    po.Id,
                    item.Id,
                    recibido.OperacionId,
                    cancellationToken);

                if (!confirmado)
                    return Conflict(new
                    {
                        mensaje = "El acumulado de la PO ya coincide, pero el OperacionId o los datos del reintento no corresponden a una recepción confirmada. Refresque la PO.",
                        sku = item.Sku
                    });

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
            {
                // Las recepciones anteriores de este lote ya se confirmaron
                // individualmente en Finance. Informar recepción parcial y
                // permitir reintentar únicamente las líneas pendientes.
                if (huboCambios)
                    return Conflict(new
                    {
                        mensaje = "Recepción parcialmente aplicada. Refresque la PO y reintente solo los ítems pendientes.",
                        detalle = inventario.Mensaje,
                        sku = item.Sku,
                        totalRecibido = po.Items.Sum(i => i.CantidadRecibida),
                        itemsAplicados = resultados
                    });

                return BadRequest(new { mensaje = inventario.Mensaje, sku = item.Sku });
            }

            // Si Catalog ya había aplicado el movimiento pero Finance no logró guardar,
            // el mismo request repara Finance sin duplicar stock.
            item.ProductoId = inventario.ProductoId;
            item.CantidadRecibida = recibido.CantidadAcumuladaEsperada;
            huboCambios = true;

            // Catalog confirma cada movimiento en su propia transacción.
            // Guardar inmediatamente el acumulado en Finance antes de
            // procesar el siguiente ítem evita dejar varias líneas sin
            // reflejar cuando una recepción posterior falla.
            var recibidoHastaAhora = po.Items.Sum(i => i.CantidadRecibida);
            var solicitadoTotal = po.Items.Sum(i => i.CantidadSolicitada);
            po.Estado = recibidoHastaAhora >= solicitadoTotal
                ? EstadoOrdenCompra.RecibidaCompleta
                : EstadoOrdenCompra.RecibidaParcial;
            po.FechaRecepcionReal = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

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
        var po = await _db.OrdenesCompra
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == req.OrdenCompraId, cancellationToken);
        if (po == null) return NotFound(new { mensaje = $"PO {req.OrdenCompraId} no encontrada." });

        var numeroFactura = req.NumeroFactura?.Trim();
        if (string.IsNullOrWhiteSpace(numeroFactura) || numeroFactura.Length > 50)
            return BadRequest(new { mensaje = "La factura requiere un número válido de máximo 50 caracteres." });

        if (req.SubTotal <= 0m || !ImporteValido(req.SubTotal) ||
            !ImporteValido(req.MontoTotal) || req.MontoTotal <= 0m)
            return BadRequest(new { mensaje = "Los importes de factura deben ser positivos, con máximo dos decimales y dentro de la precisión admitida." });

        decimal montoIva;
        decimal totalCalculado;
        try
        {
            montoIva = decimal.Round(checked(req.SubTotal * 0.13m), 2);
            totalCalculado = checked(req.SubTotal + montoIva);
        }
        catch (OverflowException)
        {
            return BadRequest(new { mensaje = "El IVA o total de factura supera la precisión monetaria admitida." });
        }

        if (!ImporteValido(totalCalculado) || req.MontoTotal != totalCalculado)
            return BadRequest(new { mensaje = "El total de la factura debe coincidir exactamente con el subtotal más IVA (13%)." });

        var fechaEmision = req.FechaEmision ?? DateTime.UtcNow;
        var fechaVencimiento = req.FechaVencimiento ?? fechaEmision.AddDays(30);
        if (fechaVencimiento < fechaEmision)
            return BadRequest(new { mensaje = "El vencimiento no puede ser anterior a la emisión de la factura." });

        // Comprobar antes de insertar para dar un error legible. La garantía
        // real bajo concurrencia reside en el índice UNIQUE de PostgreSQL FIN-06.
        if (await _db.FacturasProveedor.AnyAsync(f =>
                f.ProveedorId == po.ProveedorId &&
                f.NumeroFactura.ToUpper() == numeroFactura.ToUpper(),
                cancellationToken))
            return Conflict(new { mensaje = "El proveedor ya tiene registrada una factura con ese número." });

        // Por compatibilidad, las solicitudes anteriores sin líneas aún
        // registran la factura, pero FIN-07 impide autorizarla con 3-Way.
        List<ItemFacturaProveedor> detalle;
        try
        {
            detalle = ValidarDetalleFactura(req.Lineas, po, req.SubTotal);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }

        var factura = new FacturaProveedor
        {
            Id = Guid.NewGuid(),
            Items = detalle,
            OrdenCompraId = req.OrdenCompraId,
            ProveedorId = po.ProveedorId,
            NumeroFactura = numeroFactura,
            MontoSubTotal = req.SubTotal,
            MontoIva = montoIva,
            MontoTotal = totalCalculado,
            FechaEmision = fechaEmision,
            FechaVencimiento = fechaVencimiento,
            AprobadaParaPago = false,
        };

        _db.FacturasProveedor.Add(factura);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation &&
            pg.ConstraintName == "UX_FIN06_facturas_proveedor_numero")
        {
            return Conflict(new { mensaje = "La factura ya fue registrada por otra solicitud. Consulte el historial." });
        }

        return CreatedAtAction(nameof(ObtenerPO), new { id = po.Id },
            new { factura.Id, factura.NumeroFactura, factura.MontoTotal, factura.AprobadaParaPago,
                detalleRegistrado = detalle.Count > 0, cantidadLineas = detalle.Count });
    }

    // ── POST /api/compras/facturas/{id}/lineas ───────────────────────────────
    /// <summary>
    /// Agrega el detalle a una factura heredada sin líneas. No inventa datos,
    /// no permite modificar documentos ya contabilizados ni sobreescribir líneas.
    /// </summary>
    [HttpPost("facturas/{id:guid}/lineas", Name = "CompletarDetalleFacturaProveedor")]
    public async Task<IActionResult> CompletarDetalleFactura(
        Guid id, [FromBody] List<LineaFacturaProveedorRequest> lineas,
        CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (factura is null) return NotFound(new { mensaje = "Factura no encontrada." });

        if (factura.AprobadaParaPago || factura.Pagada || factura.Items.Count > 0)
            return Conflict(new { mensaje = "No se puede modificar una factura aprobada, pagada o con detalle existente." });

        var po = await _db.OrdenesCompra
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == factura.OrdenCompraId, cancellationToken);
        if (po is null) return Conflict(new { mensaje = "La orden original de la factura no existe." });

        List<ItemFacturaProveedor> detalle;
        try
        {
            detalle = ValidarDetalleFactura(lineas, po, factura.MontoSubTotal);
            if (detalle.Count == 0)
                return BadRequest(new { mensaje = "Para completar la factura debe proporcionar líneas documentales." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }

        factura.Items = detalle;
        // Las entidades con GUID explícito se interpretan como existentes
        // cuando se adjuntan a una factura ya rastreada. Marcar como Added.
        _db.ItemsFacturaProveedor.AddRange(detalle);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { mensaje = "El detalle ya fue registrado por otra solicitud. Recargue la factura." });
        }

        return Ok(new { factura.Id, detalleRegistrado = true, cantidadLineas = detalle.Count });
    }

    private static List<ItemFacturaProveedor> ValidarDetalleFactura(
        List<LineaFacturaProveedorRequest>? lineas, OrdenCompra po, decimal subtotal)
    {
        if (lineas is null) return new List<ItemFacturaProveedor>();

        if (lineas.Count is < 1 or > 500)
            throw new ArgumentException("La factura debe tener entre 1 y 500 líneas documentales.");

        var idsPo = po.Items.Select(i => i.Id).ToHashSet();
        var idsRecibidos = new HashSet<Guid>();
        var resultado = new List<ItemFacturaProveedor>(lineas.Count);
        decimal acumulado = 0m;

        foreach (var linea in lineas)
        {
            if (linea is null || linea.ItemOrdenCompraId == Guid.Empty ||
                !idsPo.Contains(linea.ItemOrdenCompraId) ||
                !idsRecibidos.Add(linea.ItemOrdenCompraId))
                throw new ArgumentException("La factura contiene ítems repetidos o ajenos a la orden.");

            if (linea.CantidadFacturada <= 0 ||
                linea.PrecioUnitarioFacturado <= 0m ||
                !ImporteValido(linea.PrecioUnitarioFacturado))
                throw new ArgumentException("Cada línea requiere cantidad y precio positivos (máximo dos decimales).");

            try
            {
                acumulado = checked(acumulado +
                    checked(linea.CantidadFacturada * linea.PrecioUnitarioFacturado));
            }
            catch (OverflowException)
            {
                throw new ArgumentException("Las líneas de factura exceden la precisión monetaria.");
            }

            if (!ImporteValido(acumulado))
                throw new ArgumentException("El subtotal de líneas excede numeric(18,2).");

            resultado.Add(new ItemFacturaProveedor
            {
                Id = Guid.NewGuid(),
                ItemOrdenCompraId = linea.ItemOrdenCompraId,
                CantidadFacturada = linea.CantidadFacturada,
                PrecioUnitarioFacturado = linea.PrecioUnitarioFacturado
            });
        }

        if (acumulado != subtotal)
            throw new ArgumentException("El subtotal declarado no coincide con cantidades y precios facturados.");

        return resultado;
    }

    // ── POST /api/compras/match ───────────────────────────────────────────────
    /// <summary>Ejecuta el Match Tridireccional (PO - Factura - Bodega WMS).</summary>
    [HttpPost("match", Name = "EjecutarMatch3Way")]
    [PaymentOperationsGuard]
    [ProducesResponseType(typeof(MatchTridireccional), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EjecutarMatch(
        [FromBody] Validar3WayMatchRequest req, CancellationToken cancellationToken)
    {
        // El servicio 3-Way guarda la autorización ANTES de que Contabilidad
        // genere el asiento. Ambas escrituras deben confirmarse o deshacerse
        // juntas. En CI InMemory no hay transacciones; los tests de concurrencia
        // usan PostgreSQL real con el mismo FinanceDbContext por request.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var match = await _matching.EjecutarMatchAsync(req, cancellationToken);

            if (match.Resultado == Estado3WayMatch.Conforme && match.AutorizadoParaPago)
            {
                // Un segundo match no puede producir un segundo asiento:
                // hay índices parciales únicos tanto para autorizaciones como
                // para asientos de Compra, además de esta verificación legible.
                if (await _db.PartidasContables.AsNoTracking().AnyAsync(p =>
                    p.TipoOperacion == "Compra" && p.ReferenciaOrigenId == req.OrdenCompraId,
                    cancellationToken))
                    return Conflict(new { mensaje = "La orden ya tiene un asiento de compra registrado." });

                var po = await _db.OrdenesCompra.FindAsync([req.OrdenCompraId], cancellationToken)
                    ?? throw new InvalidOperationException("No se encuentra la orden contabilizable.");
                var factura = await _db.FacturasProveedor.FindAsync([req.FacturaProveedorId], cancellationToken)
                    ?? throw new InvalidOperationException("No se encuentra la factura contabilizable.");

                await _contabilidad.GenerarAsientoCompraAsync(
                    po.Id, factura.MontoSubTotal, factura.MontoIva, cancellationToken);
            }

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return Ok(match);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation &&
            (pg.ConstraintName == "UX_FIN06_match_po_autorizado" ||
             pg.ConstraintName == "UX_FIN06_asiento_compra_po"))
        {
            return Conflict(new { mensaje = "La orden ya fue autorizada o contabilizada por otra solicitud." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
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

public record LineaFacturaProveedorRequest(
    Guid ItemOrdenCompraId,
    int CantidadFacturada,
    decimal PrecioUnitarioFacturado);

public record RegistrarFacturaRequest(
    Guid OrdenCompraId,
    string NumeroFactura,
    decimal SubTotal,
    decimal MontoTotal,
    DateTime? FechaEmision,
    DateTime? FechaVencimiento,
    List<LineaFacturaProveedorRequest>? Lineas = null);
