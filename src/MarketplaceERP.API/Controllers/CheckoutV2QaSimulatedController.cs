using System.Security.Claims;
using System.Text.RegularExpressions;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// RUTA COMPLETAMENTE QA/LOCAL: usa SOLO IQaPagoSimulador, jamás IPaymentGateway.
/// No desplegar ni registrar en producción; aislada al sandbox de auditoría.
/// No sustituye CheckoutController original.
/// </summary>
[ApiController]
[Route("api/checkout/v2/qa")]
public sealed class CheckoutV2QaSimulatedController : ControllerBase
{
    private readonly CatalogDbContext _catalogo;
    private readonly FinanceDbContext _finance;
    private readonly IWebHostEnvironment _entorno;
    private readonly IConfiguration _configuracion;
    private readonly IServiceProvider _servicios;
    private readonly ILoggerFactory _loggerFactory;

    public CheckoutV2QaSimulatedController(
        CatalogDbContext catalogo, FinanceDbContext finance,
        IWebHostEnvironment entorno, IConfiguration configuracion,
        IServiceProvider servicios, ILoggerFactory loggerFactory)
    {
        _catalogo = catalogo;
        _finance = finance;
        _entorno = entorno;
        _configuracion = configuracion;
        _servicios = servicios;
        _loggerFactory = loggerFactory;
    }

    [HttpPost("comprar")]
    [Authorize(Roles = "Comprador")]
    [PaymentOperationsGuard]
    public async Task<IActionResult> ComprarSimulado(
        [FromBody] QaQuoteHttpRequest solicitud,
        [FromHeader(Name = "Idempotency-Key")] string? clave,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!_entorno.IsDevelopment() ||
            !_configuracion.GetValue<bool>("Checkout:EnableMultiSkuQaCheckout"))
            return NotFound();

        bool EsLocalQa(DbContext db)
        {
            var cs = new NpgsqlConnectionStringBuilder(db.Database.GetDbConnection().ConnectionString);
            return new[] { "localhost", "127.0.0.1", "::1" }
                    .Contains(cs.Host, StringComparer.OrdinalIgnoreCase) &&
                cs.Port == 5432 && cs.Username == "postgres" &&
                !string.IsNullOrWhiteSpace(cs.Database) &&
                cs.Database.StartsWith("ci_checkout_http_", StringComparison.Ordinal);
        }
        if (!EsLocalQa(_finance) || !EsLocalQa(_catalogo) ||
            !string.Equals(_finance.Database.GetDbConnection().Database,
                _catalogo.Database.GetDbConnection().Database, StringComparison.Ordinal))
            return NotFound();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var compradorId)
            || compradorId == Guid.Empty)
            return Unauthorized(new { tipo = "IdentidadInvalida" });

        if (string.IsNullOrWhiteSpace(clave) ||
            !Regex.IsMatch(clave, @"\A[A-Za-z0-9_-]{8,128}\z", RegexOptions.CultureInvariant))
            return BadRequest(new { tipo = "IdempotencyKeyInvalida" });

        if (solicitud?.CamposNoPermitidos?.Count > 0 || solicitud?.Items is null ||
            solicitud.Items.Any(i => i.CamposNoPermitidos?.Count > 0))
            return BadRequest(new { tipo = "CamposNoPermitidos" });

        IReadOnlyList<QaCheckoutItemSolicitud> items;
        try
        {
            items = QaMultiSkuCotizacionCanonica.NormalizarLineasCliente(
                solicitud.Items.Select(i => new QaCheckoutItemSolicitud(
                    i.ProductoId, i.Cantidad)).ToArray());
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { tipo = "CarritoInvalido" });
        }

        var huella = QaMultiSkuCotizacionCanonica.HashCarritoNormalizado(items);
        async Task<IActionResult> Repeticion(Orden anterior)
        {
            if (anterior.CheckoutPayloadHash != huella)
                return Conflict(new { tipo = "IdempotenciaPayloadDistinto" });
            if (anterior.Estado == EstadoOrden.Cancelada)
                return Conflict(new { tipo = "CheckoutCancelado", ordenId = anterior.Id });

            var split = await _finance.SplitsPago.AsNoTracking()
                .SingleOrDefaultAsync(s => s.OrdenId == anterior.Id, ct);
            if (anterior.Estado is EstadoOrden.Aprobada or EstadoOrden.EnProceso
                    or EstadoOrden.Enviada or EstadoOrden.Entregada &&
                split is not null)
                return Ok(new
                {
                    tipo = "CheckoutSimuladoRepetido",
                    ordenId = anterior.Id,
                    transaccionId = split.TransaccionId,
                    montoVendedor = split.MontoVendedor,
                    montoMarketplace = split.MontoMarketplace
                });
            return Conflict(new { tipo = "CheckoutEnProcesoNoReemitir", ordenId = anterior.Id });
        }

        var previa = await _finance.Ordenes.AsNoTracking()
            .SingleOrDefaultAsync(o => o.CompradorId == compradorId &&
                o.IdempotencyKey == clave, ct);
        if (previa is not null) return await Repeticion(previa);

        // DI no contiene IQaPagoSimulador fuera del TestServer: sin simulador,
        // no hay operación. Nunca usar IPaymentGateway como fallback.
        var simulador = _servicios.GetService<IQaPagoSimulador>();
        if (simulador is null)
            return StatusCode(503, new { tipo = "SimuladorQaNoConfigurado" });

        var ids = items.Select(i => i.ProductoId).ToArray();
        var productoDatos = await _catalogo.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.VendedorId, p.Sku, p.Nombre, p.PrecioB2C,
                p.Moneda, p.EstaPublicado, p.EstadoProducto
            }).ToListAsync(ct);

        QaCheckoutCotizacion cotizacion;
        try
        {
            cotizacion = QaMultiSkuCotizacionCanonica.Cotizar(
                items,
                productoDatos.Select(p => new QaCheckoutProductoServidor(
                    p.Id, p.VendedorId, p.PrecioB2C, p.Moneda,
                    p.EstaPublicado, p.EstadoProducto)).ToArray(),
                // QA: exactamente la política monetaria actual del B2C V1.
                new QaCheckoutPoliticaServidor(0m, 0m));
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { tipo = "CatalogoInvalido" });
        }

        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId, NumeroOrden = "QA-V2-" + ordenId.ToString("N"),
            CompradorId = compradorId,
            VendedorId = cotizacion.VendedorId,
            IdempotencyKey = clave,
            CheckoutPayloadHash = huella,
            Moneda = "USD", SubTotal = cotizacion.SubTotal,
            MontoTotal = cotizacion.MontoTotal,
            Estado = EstadoOrden.Pendiente,
            Items = cotizacion.Lineas.Select(l =>
            {
                var p = productoDatos.Single(p => p.Id == l.ProductoId);
                return new ItemOrden
                {
                    Id = Guid.NewGuid(), OrdenId = ordenId,
                    ProductoId = p.Id, SkuReferencia = p.Sku,
                    NombreProducto = p.Nombre, Cantidad = l.Cantidad,
                    PrecioUnitario = l.PrecioUnitario
                };
            }).ToList()
        };
        var intento = new IntentoPago
        {
            Id = Guid.NewGuid(), OrdenId = ordenId, Estado = EstadoIntentoPago.Preparado,
            ReservaId = null, ReferenciaOperacion = $"PAY-{ordenId:N}",
            Monto = cotizacion.MontoTotal, Moneda = "USD"
        };

        _finance.Ordenes.Add(orden);
        _finance.IntentosPago.Add(intento);
        try
        {
            await _finance.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            _finance.ChangeTracker.Clear();
            var actual = await _finance.Ordenes.AsNoTracking()
                .SingleOrDefaultAsync(o => o.CompradorId == compradorId &&
                    o.IdempotencyKey == clave, ct);
            if (actual is not null) return await Repeticion(actual);
            throw;
        }

        // Puntos de interrupción SOLO para QA LOCAL (Development + BD efímera).
        // Devuelven 503 tras un commit para modelar caída del cliente/proceso;
        // la repetición debe bloquear segundo pago sin reparar a ciegas.
        var falloQa = _configuracion["Checkout:QaFaultPoint"];
        if (falloQa == "afterFinance")
            return StatusCode(503, new { tipo = "QaCaidaTrasFinanceAntesDeReserva", ordenId });

        ReservaCarritoQa reservas;
        try
        {
            reservas = await new ReservaCarritoAtomicaQaService(
                _catalogo, _loggerFactory).ReservarAsync(
                    ordenId, items.Select(i =>
                        new ItemReservaCarritoQa(i.ProductoId, i.Cantidad)).ToArray(),
                    autorizacionExplicitaQa: true, cancellationToken: ct);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("STOCK_INSUFICIENTE_MULTISKU_", StringComparison.Ordinal))
        {
            _finance.ChangeTracker.Clear();
            var cancelar = await _finance.Ordenes.SingleAsync(o => o.Id == ordenId, ct);
            var rechazo = await _finance.IntentosPago.SingleAsync(i => i.Id == intento.Id, ct);
            cancelar.Estado = EstadoOrden.Cancelada;
            cancelar.FechaCancelacion = DateTime.UtcNow;
            rechazo.Estado = EstadoIntentoPago.Rechazado;
            await _finance.SaveChangesAsync(ct);
            return Conflict(new { tipo = "StockInsuficiente", ordenId });
        }

        if (falloQa == "afterReserve")
            return StatusCode(503, new { tipo = "QaCaidaTrasReservaAntesDeMapa", ordenId });

        // Si falla el mapa, las reservas quedan para RECUPERACIÓN MANUAL;
        // jamás cobrar ni reintentar automáticamente.
        var catalogReservas = await _catalogo.ReservasStock.AsNoTracking()
            .Where(x => x.OrdenId == ordenId)
            .ToListAsync(ct);
        try
        {
            await new QaIntentoReservasMapService(_finance).RegistrarAsync(
                intento.Id, catalogReservas.Select(x => new QaVinculoReserva(
                    x.Id, x.ProductoId, x.CantidadOriginal)).ToArray(),
                autorizacionExplicitaQa: true, cancellationToken: ct);
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { tipo = "MapaReservasInciertoNoCobrar", ordenId });
        }

        if (falloQa == "afterMap")
            return StatusCode(503, new { tipo = "QaCaidaTrasMapaAntesDeEnviado", ordenId });

        // Enviado hace duradera la prohibición de segundo pago ANTES
        // de simular, incluso ante crash/timeout.
        string resultado;
        try
        {
            resultado = await new QaMultiSkuPagoSimuladoService(
                _finance, simulador).SimularUnaVezAsync(
                    intento.Id, autorizacionExplicitaQa: true, ct: ct);
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { tipo = "PagoSimuladoInciertoNoReemitir", ordenId });
        }
        if (resultado == "RECHAZADO_SIMULADO_RESERVAS_AUN_PENDIENTES")
        {
            await new ReservaCarritoAtomicaQaService(_catalogo, _loggerFactory)
                .LiberarTrasRechazoAsync(ordenId, reservas.ReservaIds,
                    autorizacionExplicitaQa: true, rechazoExplicitoSimulado: true,
                    cancellationToken: ct);
            _finance.ChangeTracker.Clear();
            var cancelar = await _finance.Ordenes.SingleAsync(o => o.Id == ordenId, ct);
            cancelar.Estado = EstadoOrden.Cancelada;
            cancelar.FechaCancelacion = DateTime.UtcNow;
            await _finance.SaveChangesAsync(ct);
            return Conflict(new { tipo = "PagoRechazadoSimulado", ordenId });
        }
        if (resultado != "APROBADO_SIMULADO_SIN_CIERRE_COMERCIAL")
            return Conflict(new { tipo = "PagoInciertoNoReemitir", ordenId });

        try
        {
            await new QaMultiSkuCierreTransaccionalService(_finance)
                .CerrarAsync(intento.Id, autorizacionExplicitaQa: true, ct: ct);
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { tipo = "CobroSimuladoSinCierreComercial", ordenId });
        }

        _finance.ChangeTracker.Clear();
        var splitFinal = await _finance.SplitsPago.AsNoTracking()
            .SingleAsync(s => s.OrdenId == ordenId, ct);
        return Ok(new
        {
            tipo = "CheckoutSimuladoCompletado",
            ordenId,
            transaccionId = splitFinal.TransaccionId,
            montoVendedor = splitFinal.MontoVendedor,
            montoMarketplace = splitFinal.MontoMarketplace
        });
    }
}
