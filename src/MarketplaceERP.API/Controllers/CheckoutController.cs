/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

public record ItemCheckoutRequest(
    Guid ProductoId,
    int Cantidad
);

public record CheckoutNuevoRequest(
    List<ItemCheckoutRequest> Items,
    string? DireccionEntrega,
    string? ZonaEnvio,
    string? MetodoPago
);

[ApiController]
[Route("api/checkout")]
public class CheckoutController : ControllerBase
{
    private readonly CheckoutOrchestrator _orchestrator;
    private readonly FinanceDbContext _financeDb;
    private readonly CatalogDbContext _catalogDb;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        CheckoutOrchestrator orchestrator,
        FinanceDbContext financeDb,
        CatalogDbContext catalogDb,
        IConfiguration configuration,
        ILogger<CheckoutController> logger)
    {
        _orchestrator = orchestrator;
        _financeDb = financeDb;
        _catalogDb = catalogDb;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Procesa el checkout de una orden bajo el nuevo contrato seguro e idempotente.
    /// Header obligatorio: Idempotency-Key
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Comprador")]
    [ProducesResponseType(typeof(CheckoutResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ProcesarCheckout(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] CheckoutNuevoRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Validar Idempotency-Key obligatoria
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new { mensaje = "El encabezado 'Idempotency-Key' es obligatorio para procesar el checkout." });
        }

        idempotencyKey = idempotencyKey.Trim();

        // 2. Extraer CompradorId
        var compradorIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(compradorIdClaim, out var compradorId))
        {
            return Unauthorized(new { mensaje = "Identificador de comprador inválido en el token." });
        }

        // 3. Normalizar request y calcular hash SHA-256
        // Ordenamos los ítems por ProductoId para que el hash sea determinista
        var itemsNormalizados = request.Items?
            .OrderBy(i => i.ProductoId)
            .Select(i => new { i.ProductoId, i.Cantidad })
            .ToList();

        var payloadToHash = JsonSerializer.Serialize(new
        {
            Items = itemsNormalizados,
            DireccionEntrega = request.DireccionEntrega?.Trim() ?? "",
            ZonaEnvio = request.ZonaEnvio?.Trim() ?? "",
            MetodoPago = request.MetodoPago?.Trim() ?? ""
        });

        string requestHash;
        using (var sha = SHA256.Create())
        {
            var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payloadToHash));
            requestHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // 4. Verificar si la clave de idempotencia ya existe
        var registroExistente = await _financeDb.IdempotencyKeys
            .FirstOrDefaultAsync(k => k.UsuarioId == compradorId && k.Key == idempotencyKey, cancellationToken);

        if (registroExistente != null)
        {
            // Definición de comportamiento: si la clave tiene más de 24 horas (expirada), se purga y se permite una nueva orden
            if (DateTime.UtcNow >= registroExistente.FechaExpiracion)
            {
                _logger.LogInformation("Clave de idempotencia '{Key}' para usuario {UsuarioId} expiró a las {Exp}. Purgando para permitir nueva transacción.",
                    idempotencyKey, compradorId, registroExistente.FechaExpiracion);
                _financeDb.IdempotencyKeys.Remove(registroExistente);
                await _financeDb.SaveChangesAsync(cancellationToken);
            }
            else
            {
                // Misma clave + diferente payload -> 409 Conflict
                if (registroExistente.RequestHash != requestHash)
                {
                    return Conflict(new { mensaje = "La clave de idempotencia ya fue utilizada con una solicitud diferente." });
                }

                // Misma clave + mismo payload -> devolver la respuesta previa almacenada sin volver a cobrar
                return StatusCode(registroExistente.StatusCode, JsonSerializer.Deserialize<object>(registroExistente.ResponseJson));
            }
        }

        // 5. Validar items del request
        if (request.Items == null || request.Items.Count == 0)
        {
            return BadRequest(new { mensaje = "El carrito no contiene productos." });
        }

        foreach (var it in request.Items)
        {
            if (it.Cantidad <= 0)
                return BadRequest(new { mensaje = "La cantidad por producto debe ser mayor a 0." });
            if (it.Cantidad > 1000)
                return BadRequest(new { mensaje = "La cantidad máxima permitida por producto es de 1000 unidades." });
        }

        // 6. Cargar productos desde CatalogDbContext
        var productoIds = request.Items.Select(i => i.ProductoId).Distinct().ToList();
        var productosDb = await _catalogDb.Productos
            .Where(p => productoIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (productosDb.Count != productoIds.Count)
        {
            return BadRequest(new { mensaje = "Uno o más productos del carrito no existen en el catálogo." });
        }

        // Validar que todos los productos estén publicados
        var noPublicados = productosDb.Where(p => !p.EstaPublicado).ToList();
        if (noPublicados.Any())
        {
            return BadRequest(new { mensaje = $"El producto '{noPublicados.First().Nombre}' no está publicado para la venta." });
        }

        // Regla de Negocio Marketplace: Una orden = un solo vendedor (si hay múltiples vendedores, rechazar con 400)
        var vendedores = productosDb.Select(p => p.VendedorId).Distinct().ToList();
        if (vendedores.Count > 1)
        {
            return BadRequest(new { mensaje = "Una orden solo puede contener productos de un mismo vendedor. Realice órdenes separadas por vendedor." });
        }

        var vendedorId = vendedores.First();

        // 7. Determinar TipoCliente (en el servidor, nunca desde el cliente)
        // Por defecto en marketplace B2C para Compradores regulares
        var tipoCliente = TipoCliente.B2C;

        // 8. Calcular SubTotal desde catálogo con decimal y redondeo a 2 decimales
        decimal subTotal = 0m;
        var itemsOrden = new List<ItemOrden>();
        var ordenId = Guid.NewGuid();

        foreach (var reqItem in request.Items)
        {
            var prod = productosDb.First(p => p.Id == reqItem.ProductoId);
            decimal precioUnitario = (tipoCliente == TipoCliente.B2B) ? prod.PrecioB2B : prod.PrecioB2C;
            precioUnitario = Math.Round(precioUnitario, 2, MidpointRounding.AwayFromZero);

            subTotal += Math.Round(precioUnitario * reqItem.Cantidad, 2, MidpointRounding.AwayFromZero);

            itemsOrden.Add(new ItemOrden
            {
                Id = Guid.NewGuid(),
                OrdenId = ordenId,
                ProductoId = prod.Id,
                SkuReferencia = prod.Sku,
                NombreProducto = prod.Nombre,
                Cantidad = reqItem.Cantidad,
                PrecioUnitario = precioUnitario
            });
        }

        subTotal = Math.Round(subTotal, 2, MidpointRounding.AwayFromZero);
        decimal iva13 = Math.Round(subTotal * 0.13m, 2, MidpointRounding.AwayFromZero);
        
        // Costo de envío extraído de configuración (Checkout:CostoEnvioDefault, fallback 5.00m)
        decimal costoEnvio = decimal.TryParse(_configuration["Checkout:CostoEnvioDefault"], out var cEnv) ? cEnv : 5.00m;
        costoEnvio = Math.Round(costoEnvio, 2, MidpointRounding.AwayFromZero);

        decimal montoTotal = Math.Round(subTotal + iva13 + costoEnvio, 2, MidpointRounding.AwayFromZero);

        // 9. Ejecución atómica dentro de UNA sola transacción EF Core
        var strategy = _financeDb.Database.CreateExecutionStrategy();

        try
        {
            var resultado = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _financeDb.Database.BeginTransactionAsync(cancellationToken);

                // Crear Orden en estado inicial Aprobada (pagada con éxito)
                var orden = new Orden
                {
                    Id = ordenId,
                    NumeroOrden = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(10000, 99999)}",
                    CompradorId = compradorId,
                    VendedorId = vendedorId,
                    SubTotal = subTotal,
                    ImpuestoIva = iva13,
                    CostoEnvio = costoEnvio,
                    MontoTotal = montoTotal,
                    Moneda = "USD",
                    Estado = EstadoOrden.Aprobada,
                    TipoCliente = tipoCliente,
                    FechaCreacion = DateTime.UtcNow,
                    FechaAprobacion = DateTime.UtcNow,
                    DireccionEntrega = request.DireccionEntrega ?? "San Salvador",
                    ZonaEnvio = request.ZonaEnvio ?? "San Salvador y AMSS",
                    Items = itemsOrden
                };

                _financeDb.Ordenes.Add(orden);
                await _financeDb.SaveChangesAsync(cancellationToken);

                // Ejecutar saga de cobro, split y asiento contable
                var primerItem = itemsOrden.First();
                var command = new CheckoutCommand(
                    OrdenId: ordenId,
                    CompradorId: compradorId,
                    VendedorId: vendedorId,
                    ProductoId: primerItem.ProductoId,
                    Cantidad: primerItem.Cantidad,
                    MontoTotal: montoTotal,
                    Moneda: "USD"
                );

                var sagaResult = await _orchestrator.ProcesarCheckoutAsync(command);

                // Guardar registro de idempotencia dentro de la misma transacción
                var record = new IdempotencyKeyRecord
                {
                    Id = Guid.NewGuid(),
                    Key = idempotencyKey,
                    UsuarioId = compradorId,
                    RequestHash = requestHash,
                    StatusCode = StatusCodes.Status200OK,
                    ResponseJson = JsonSerializer.Serialize(sagaResult),
                    FechaCreacion = DateTime.UtcNow,
                    FechaExpiracion = DateTime.UtcNow.AddHours(24)
                };

                _financeDb.IdempotencyKeys.Add(record);
                await _financeDb.SaveChangesAsync(cancellationToken);

                await tx.CommitAsync(cancellationToken);

                return sagaResult;
            });

            return Ok(resultado);
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            _logger.LogWarning("Colisión concurrente detectada en Idempotency-Key '{Key}'.", idempotencyKey);
            // Reintentar lectura de la clave recién guardada por la solicitud concurrente
            var claveConcurrente = await _financeDb.IdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.UsuarioId == compradorId && k.Key == idempotencyKey, cancellationToken);

            if (claveConcurrente != null)
            {
                if (claveConcurrente.RequestHash != requestHash)
                    return Conflict(new { mensaje = "La clave de idempotencia ya fue utilizada con una solicitud diferente." });

                return StatusCode(claveConcurrente.StatusCode, JsonSerializer.Deserialize<object>(claveConcurrente.ResponseJson));
            }

            return Conflict(new { mensaje = "Conflicto de concurrencia al procesar el checkout." });
        }
        catch (StockInsuficienteException ex)
        {
            _logger.LogWarning(ex, "Checkout fallido por stock insuficiente.");
            _financeDb.ChangeTracker.Clear();
            var ordenHuérfana = await _financeDb.Ordenes.FindAsync(new object[] { ordenId });
            if (ordenHuérfana != null) { _financeDb.Ordenes.Remove(ordenHuérfana); await _financeDb.SaveChangesAsync(CancellationToken.None); }
            return Conflict(new { mensaje = ex.Message, tipo = "StockInsuficiente" });
        }
        catch (PagoFallidoException ex)
        {
            _logger.LogWarning(ex, "Checkout fallido por rechazo de pago.");
            _financeDb.ChangeTracker.Clear();
            var ordenHuérfana = await _financeDb.Ordenes.FindAsync(new object[] { ordenId });
            if (ordenHuérfana != null) { _financeDb.Ordenes.Remove(ordenHuérfana); await _financeDb.SaveChangesAsync(CancellationToken.None); }
            return Conflict(new { mensaje = ex.Message, tipo = "PagoFallido" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("pasarela de pagos no está configurada") || ex.Message.Contains("503"))
        {
            _logger.LogError(ex, "Checkout fallido por pasarela no disponible.");
            _financeDb.ChangeTracker.Clear();
            var ordenHuérfana = await _financeDb.Ordenes.FindAsync(new object[] { ordenId });
            if (ordenHuérfana != null) { _financeDb.Ordenes.Remove(ordenHuérfana); await _financeDb.SaveChangesAsync(CancellationToken.None); }
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Lista las órdenes del sistema. Solo Admin puede ver todas.
    /// </summary>
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
