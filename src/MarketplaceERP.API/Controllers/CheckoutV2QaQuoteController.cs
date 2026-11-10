using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Finance.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// PROTOTIPO EN SANDBOX. No compra, no crea orden, no reserva stock, NO cobra.
/// No trasladar este controller QA a despliegues. Sin registro manual en DI.
/// </summary>
public sealed class QaQuoteHttpRequest
{
    public List<QaQuoteHttpItem> Items { get; set; } = new();
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? CamposNoPermitidos { get; set; }
}

public sealed class QaQuoteHttpItem
{
    public Guid ProductoId { get; set; }
    public int Cantidad { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? CamposNoPermitidos { get; set; }
}

[ApiController]
[Route("api/checkout/v2/qa")]
public sealed class CheckoutV2QaQuoteController : ControllerBase
{
    private readonly CatalogDbContext _catalogo;
    private readonly FinanceDbContext _finance;
    private readonly IWebHostEnvironment _entorno;
    private readonly IConfiguration _configuracion;

    public CheckoutV2QaQuoteController(
        CatalogDbContext catalogo, FinanceDbContext finance,
        IWebHostEnvironment entorno, IConfiguration configuracion)
    {
        _catalogo = catalogo;
        _finance = finance;
        _entorno = entorno;
        _configuracion = configuracion;
    }

    [HttpPost("cotizar")]
    [Authorize(Roles = "Comprador")]
    [PaymentOperationsGuard]
    public async Task<IActionResult> CotizarSoloQa(
        [FromBody] QaQuoteHttpRequest solicitud,
        [FromHeader(Name = "Idempotency-Key")] string? clave,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";

        // Fail-closed: ni siquiera SELECT fuera del sandbox local.
        if (!_entorno.IsDevelopment() ||
            !_configuracion.GetValue<bool>("Checkout:EnableMultiSkuQaQuote"))
            return NotFound();

        bool EsLocalEfimero(Microsoft.EntityFrameworkCore.DbContext db)
        {
            var c = new NpgsqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);
            return new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                c.Host, StringComparer.OrdinalIgnoreCase) &&
                c.Port == 5432 && c.Username == "postgres" &&
                !string.IsNullOrWhiteSpace(c.Database) &&
                c.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal);
        }
        if (!EsLocalEfimero(_catalogo) || !EsLocalEfimero(_finance) ||
            !string.Equals(_catalogo.Database.GetDbConnection().Database,
                _finance.Database.GetDbConnection().Database, StringComparison.Ordinal))
            return NotFound();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var compradorId) ||
            compradorId == Guid.Empty)
            return Unauthorized(new { tipo = "IdentidadInvalida" });

        if (string.IsNullOrWhiteSpace(clave) ||
            !Regex.IsMatch(clave, @"\A[A-Za-z0-9_-]{8,128}\z", RegexOptions.CultureInvariant))
            return BadRequest(new { tipo = "IdempotencyKeyInvalida" });

        if (solicitud?.CamposNoPermitidos?.Count > 0 ||
            solicitud?.Items is null ||
            solicitud.Items.Any(i => i.CamposNoPermitidos?.Count > 0))
            return BadRequest(new { tipo = "CamposNoPermitidos" });

        IReadOnlyList<QaCheckoutItemSolicitud> canon;
        try
        {
            canon = QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(
                solicitud.Items.Select(i => new QaCheckoutItemSolicitud(
                    i.ProductoId, i.Cantidad)).ToArray());
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { tipo = "CarritoInvalido" });
        }

        // Una clave consumida por una orden real NO autoriza recotización ni
        // cambio de importe; el endpoint QA jamás intenta recuperar su pago.
        if (await _finance.Ordenes.AsNoTracking().AnyAsync(o =>
                o.CompradorId == compradorId && o.IdempotencyKey == clave, ct))
            return Conflict(new { tipo = "ClaveYaAsociadaAOrden" });

        var ids = canon.Select(x => x.ProductoId).ToArray();
        var catalogo = await _catalogo.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new QaCheckoutProductoServidor(
                p.Id, p.VendedorId, p.PrecioB2C, p.Moneda,
                p.EstaPublicado, p.EstadoProducto))
            .ToListAsync(ct);

        QaCheckoutCotizacion cotizacion;
        try
        {
            // Política de SOLO QA: mismo cálculo que el checkout B2C v1.
            // Una política fiscal real debe aprobarse antes de cobrar en V2.
            cotizacion = QaMultiSkuCotizacionCanonica.Cotizar(
                canon, catalogo, new QaCheckoutPoliticaServidor(0m, 0m));
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { tipo = "CatalogoNoAptoParaCheckoutV2" });
        }

        return Ok(new
        {
            tipo = "CotizacionQaNoReservada",
            compradorId,
            cotizacion.PayloadHash,
            cotizacion.VendedorId,
            cotizacion.Moneda,
            cotizacion.Lineas,
            cotizacion.SubTotal,
            cotizacion.ImpuestoIva,
            cotizacion.CostoEnvio,
            cotizacion.MontoTotal,
            stockReservado = false,
            pagoProcesado = false
        });
    }
}
