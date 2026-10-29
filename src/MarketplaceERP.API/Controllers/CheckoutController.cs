/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Contrato público de Checkout B2C: el cliente solo indica producto y cantidad.
/// Precio, moneda, vendedor y comprador se determinan desde datos confiables del servidor.
/// </summary>
public record CheckoutRequest(Guid ProductoId, int Cantidad);

[ApiController]
[Route("api/checkout")]
public class CheckoutController : ControllerBase
{
    private readonly CheckoutOrchestrator _orchestrator;
    private readonly FinanceDbContext _financeDb;
    private readonly CatalogDbContext _catalogDb;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        CheckoutOrchestrator orchestrator,
        FinanceDbContext financeDb,
        CatalogDbContext catalogDb,
        ILogger<CheckoutController> logger)
    {
        _orchestrator = orchestrator;
        _financeDb = financeDb;
        _catalogDb = catalogDb;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "Comprador")]
    [ProducesResponseType(typeof(CheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProcesarCheckout(
        [FromBody] CheckoutRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (request.ProductoId == Guid.Empty || request.Cantidad <= 0)
            return BadRequest(new { mensaje = "Producto y cantidad válida son obligatorios." });

        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            !Regex.IsMatch(idempotencyKey, @"\A[A-Za-z0-9_-]{8,128}\z", RegexOptions.CultureInvariant))
        {
            return BadRequest(new { mensaje = "Se requiere Idempotency-Key (8 a 128 caracteres alfanuméricos, guion o guion bajo)." });
        }

        // El claim de identidad debe ser el GUID real del usuario autenticado.
        // No generar IDs aleatorios ni hashes de nombres de usuario como sustitutos.
        var compradorIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(compradorIdClaim, out var compradorId) || compradorId == Guid.Empty)
            return Unauthorized(new { mensaje = "La sesión no contiene un identificador de comprador válido." });

        // La huella representa solo los campos enviados por el comprador.
        // La cotización original se conserva en la orden al reutilizar esta clave.
        var payload = $"{request.ProductoId:N}|{request.Cantidad}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        // Consultar el registro persistente ANTES de recalcular precio: una repetición
        // debe devolver el resultado de la operación original, no generar otra compra.
        var anterior = await _financeDb.Ordenes.AsNoTracking()
            .Include(o => o.SplitPago)
            .SingleOrDefaultAsync(o => o.CompradorId == compradorId && o.IdempotencyKey == idempotencyKey);
        if (anterior is not null)
            return RespuestaRepetida(anterior, hash);

        var producto = await _catalogDb.Productos.AsNoTracking()
            .Where(p => p.Id == request.ProductoId)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Nombre,
                p.PrecioB2C,
                p.Moneda,
                p.VendedorId,
                p.EstaPublicado,
                p.EstadoProducto
            })
            .SingleOrDefaultAsync();

        if (producto is null || !producto.EstaPublicado ||
            !string.Equals(producto.EstadoProducto, "Activo", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { mensaje = "El producto no está disponible para Checkout." });
        }

        if (producto.VendedorId == Guid.Empty || producto.PrecioB2C <= 0 ||
            !string.Equals(producto.Moneda, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { mensaje = "El producto no dispone de una configuración comercial válida para Checkout B2C en USD." });
        }

        decimal total;
        try
        {
            total = checked(producto.PrecioB2C * request.Cantidad);
            // El modelo monetario de Finance persiste importes con precisión (18,2).
            if (total > 9999999999999999.99m || decimal.Round(total, 2) != total)
                return BadRequest(new { mensaje = "El monto solicitado excede la precisión monetaria admitida." });
        }
        catch (OverflowException)
        {
            return BadRequest(new { mensaje = "La cantidad solicitada genera un monto inválido." });
        }

        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"CHK-{DateTime.UtcNow:yyyyMMdd}-{ordenId:N}",
            CompradorId = compradorId,
            VendedorId = producto.VendedorId,
            IdempotencyKey = idempotencyKey,
            CheckoutPayloadHash = hash,
            SubTotal = total,
            MontoTotal = total,
            Moneda = "USD",
            Estado = EstadoOrden.Pendiente,
            FechaCreacion = DateTime.UtcNow,
            Items = new List<ItemOrden>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    OrdenId = ordenId,
                    ProductoId = producto.Id,
                    SkuReferencia = producto.Sku,
                    NombreProducto = producto.Nombre,
                    Cantidad = request.Cantidad,
                    PrecioUnitario = producto.PrecioB2C
                }
            }
        };

        _financeDb.Ordenes.Add(orden);
        try
        {
            // Índice UNIQUE (CompradorId, IdempotencyKey) en PostgreSQL.
            // La carrera entre solicitudes concurrentes queda bloqueada antes de pagar.
            await _financeDb.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException postgres &&
            postgres.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation &&
            postgres.ConstraintName is not null &&
            postgres.ConstraintName.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase))
        {
            _financeDb.ChangeTracker.Clear();
            var concurrente = await _financeDb.Ordenes.AsNoTracking()
                .Include(o => o.SplitPago)
                .SingleAsync(o => o.CompradorId == compradorId && o.IdempotencyKey == idempotencyKey);
            return RespuestaRepetida(concurrente, hash);
        }

        try
        {
            var comando = new CheckoutCommand(
                OrdenId: orden.Id,
                CompradorId: compradorId,
                VendedorId: producto.VendedorId,
                ProductoId: producto.Id,
                Cantidad: request.Cantidad,
                MontoTotal: total,
                Moneda: "USD");

            var resultado = await _orchestrator.ProcesarCheckoutAsync(comando);

            // El pago autoriza el pedido; NO implica envío ni entrega.
            orden.Estado = EstadoOrden.Aprobada;
            orden.FechaAprobacion = DateTime.UtcNow;
            await _financeDb.SaveChangesAsync();
            return Ok(resultado);
        }
        catch (StockInsuficienteException ex)
        {
            orden.Estado = EstadoOrden.Cancelada;
            orden.FechaCancelacion = DateTime.UtcNow;
            await _financeDb.SaveChangesAsync();
            _logger.LogWarning(ex, "Checkout sin stock. OrdenId={OrdenId}", ordenId);
            return Conflict(new { mensaje = "No existe stock disponible suficiente.", tipo = "StockInsuficiente" });
        }
        catch (PagoResultadoInciertoException ex)
        {
            // No cancelar, liberar ni reintentar: el cargo podría existir.
            // La clave de idempotencia conserva esta orden para su conciliación.
            _logger.LogError(ex, "Checkout pendiente de conciliación. OrdenId={OrdenId}", ordenId);
            return Conflict(new
            {
                mensaje = "No fue posible confirmar el resultado del pago. Se requiere conciliación antes de reintentar.",
                tipo = "PagoPendienteConciliacion",
                ordenId
            });
        }
        catch (PagoFallidoException ex)
        {
            // Rechazo explícito de la pasarela: el orquestador ya liberó la
            // reserva. No debe confundirse con una respuesta incierta.
            orden.Estado = EstadoOrden.Cancelada;
            orden.FechaCancelacion = DateTime.UtcNow;
            await _financeDb.SaveChangesAsync();
            _logger.LogWarning(ex, "Pago rechazado explícitamente. OrdenId={OrdenId}", ordenId);
            return Conflict(new
            {
                mensaje = "El pago fue rechazado; la orden quedó cancelada.",
                tipo = "PagoRechazado",
                ordenId
            });
        }
        // Fallos no clasificados siguen sin marcarse como cancelados.
        // I2B2B añadirá conciliación persistente y reembolso verificable.
    }

    private IActionResult RespuestaRepetida(Orden anterior, string hash)
    {
        if (!string.Equals(anterior.CheckoutPayloadHash, hash, StringComparison.Ordinal))
        {
            return Conflict(new
            {
                mensaje = "La Idempotency-Key ya fue utilizada para una solicitud diferente.",
                tipo = "IdempotenciaPayloadDistinto"
            });
        }

        if (anterior.Estado == EstadoOrden.Cancelada)
        {
            return Conflict(new
            {
                mensaje = "La operación original terminó cancelada; debe verificarse antes de iniciar otra compra.",
                tipo = "CheckoutCancelado",
                ordenId = anterior.Id
            });
        }

        if ((anterior.Estado is EstadoOrden.Aprobada or EstadoOrden.EnProceso or EstadoOrden.Enviada or EstadoOrden.Entregada) &&
            anterior.SplitPago is not null)
        {
            return Ok(new CheckoutResult(
                OrdenId: anterior.Id,
                TransaccionId: anterior.SplitPago.TransaccionId,
                MontoVendedor: anterior.SplitPago.MontoVendedor,
                MontoMarketplace: anterior.SplitPago.MontoMarketplace));
        }

        // Conservar el mismo OrdenId pero NUNCA ejecutar la pasarela otra vez
        // si el estado del primer intento es ambiguo o sigue en curso.
        Response.Headers["Retry-After"] = "2";
        return Conflict(new
        {
            mensaje = "El Checkout ya está en proceso o requiere conciliación; no se inició otro cobro.",
            tipo = "CheckoutEnProceso",
            ordenId = anterior.Id
        });
    }

    [HttpGet("ordenes")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<Orden>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrdenes()
    {
        var ordenes = await _financeDb.Ordenes
            .Include(o => o.Items)
            .Include(o => o.SplitPago)
            .OrderByDescending(o => o.FechaCreacion)
            .Take(100)
            .ToListAsync();
        return Ok(ordenes);
    }
}
