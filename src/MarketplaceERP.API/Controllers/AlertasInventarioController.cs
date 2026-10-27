/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

[ApiController]
[Route("api/alertas-inventario")]
[Authorize]
public sealed class AlertasInventarioController : ControllerBase
{
    private readonly IAlertaInventarioService _alertas;
    private readonly FinanceDbContext _financeDb;

    public AlertasInventarioController(
        IAlertaInventarioService alertas,
        FinanceDbContext financeDb)
    {
        _alertas = alertas;
        _financeDb = financeDb;
    }

    [HttpGet]
    public async Task<IActionResult> Obtener(CancellationToken cancellationToken)
    {
        var alertas = await _alertas.ObtenerActivasAsync(cancellationToken);
        var skus = alertas.Select(a => a.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var enTransito = await _financeDb.OrdenesCompra
            .AsNoTracking()
            .Where(po => po.Estado == EstadoOrdenCompra.Enviada || po.Estado == EstadoOrdenCompra.RecibidaParcial)
            .SelectMany(po => po.Items.Select(item => new
            {
                item.Sku,
                Pendiente = item.CantidadSolicitada - item.CantidadRecibida,
                po.NumeroPO,
                po.FechaEntregaEsperada
            }))
            .Where(x => skus.Contains(x.Sku) && x.Pendiente > 0)
            .ToListAsync(cancellationToken);

        var transitoPorSku = enTransito
            .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Cantidad = g.Sum(x => x.Pendiente),
                    FechaEsperada = g.Where(x => x.FechaEntregaEsperada.HasValue)
                        .Select(x => x.FechaEntregaEsperada)
                        .OrderBy(x => x)
                        .FirstOrDefault(),
                    Ordenes = g.Select(x => x.NumeroPO).Distinct().OrderBy(x => x).ToArray()
                },
                StringComparer.OrdinalIgnoreCase);

        var items = alertas.Select(a =>
        {
            transitoPorSku.TryGetValue(a.Sku, out var transito);
            var cantidadEnTransito = transito?.Cantidad ?? 0;

            var mensaje = a.Tipo switch
            {
                TipoAlertaInventario.OutOfStock => "El SKU no tiene unidades disponibles.",
                TipoAlertaInventario.CriticalStock => $"Disponibilidad {a.CantidadDisponible}, igual o inferior al stock mínimo {a.StockMinimo}.",
                _ => $"Disponibilidad {a.CantidadDisponible}, igual o inferior al punto de reorden {a.PuntoReorden}."
            };

            var recomendacion = cantidadEnTransito > 0
                ? "Revisar mercancía en tránsito y fecha prevista de recepción antes de generar reposición adicional."
                : a.Tipo switch
                {
                    TipoAlertaInventario.OutOfStock => "Priorizar evaluación de reabastecimiento y revisar pedidos pendientes.",
                    TipoAlertaInventario.CriticalStock => "Evaluar reabastecimiento con prioridad alta.",
                    _ => "Evaluar necesidad de reposición según demanda y lead time."
                };

            return new
            {
                a.ProductoId,
                a.AlertaId,
                a.Sku,
                a.Producto,
                a.Categoria,
                Tipo = ToApi(a.Tipo),
                Prioridad = ToApi(a.Prioridad),
                Estado = ToApi(a.Estado),
                a.CantidadDisponible,
                a.CantidadFisica,
                a.CantidadReservada,
                a.CantidadBloqueada,
                CantidadEnTransito = cantidadEnTransito,
                FechaEntregaEsperada = transito?.FechaEsperada,
                OrdenesCompra = transito?.Ordenes ?? Array.Empty<string>(),
                a.StockMinimo,
                a.PuntoReorden,
                a.FaltanteAReorden,
                a.Ubicaciones,
                Mensaje = mensaje,
                Recomendacion = recomendacion,
                a.ReconocidaEn,
                a.ReconocidaPor
            };
        }).ToList();

        return Ok(new
        {
            Total = items.Count,
            Pendientes = items.Count(x => x.Estado == "OPEN"),
            Reconocidas = items.Count(x => x.Estado == "ACKNOWLEDGED"),
            PrioridadMedia = items.Count(x => x.Prioridad == "MEDIUM"),
            PrioridadAlta = items.Count(x => x.Prioridad == "HIGH"),
            PrioridadCritica = items.Count(x => x.Prioridad == "CRITICAL"),
            SinExistencia = items.Count(x => x.Tipo == "OUT_OF_STOCK"),
            FaltanteAReorden = items.Sum(x => x.FaltanteAReorden),
            Items = items
        });
    }

    [HttpPost("{productoId:guid}/reconocer")]
    public async Task<IActionResult> Reconocer(
        Guid productoId,
        [FromBody] ReconocerAlertaRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseTipo(request.Tipo, out var tipo))
            return BadRequest(new { mensaje = "Tipo de alerta inválido." });

        var usuario = string.IsNullOrWhiteSpace(request.Usuario)
            ? (User.Identity?.Name ?? "usuario")
            : request.Usuario;

        var reconocida = await _alertas.ReconocerAsync(
            productoId,
            tipo,
            usuario,
            cancellationToken);

        if (!reconocida)
        {
            return Conflict(new
            {
                mensaje = "La alerta ya no está activa con ese tipo. Actualiza la vista antes de reconocerla."
            });
        }

        return NoContent();
    }

    private static string ToApi(TipoAlertaInventario value) => value switch
    {
        TipoAlertaInventario.LowStock => "LOW_STOCK",
        TipoAlertaInventario.CriticalStock => "CRITICAL_STOCK",
        TipoAlertaInventario.OutOfStock => "OUT_OF_STOCK",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApi(PrioridadAlertaInventario value) => value switch
    {
        PrioridadAlertaInventario.Medium => "MEDIUM",
        PrioridadAlertaInventario.High => "HIGH",
        PrioridadAlertaInventario.Critical => "CRITICAL",
        _ => value.ToString().ToUpperInvariant()
    };

    private static string ToApi(EstadoAlertaInventario value) => value switch
    {
        EstadoAlertaInventario.Open => "OPEN",
        EstadoAlertaInventario.Acknowledged => "ACKNOWLEDGED",
        _ => value.ToString().ToUpperInvariant()
    };

    private static bool TryParseTipo(string value, out TipoAlertaInventario tipo)
    {
        tipo = value?.Trim().ToUpperInvariant() switch
        {
            "LOW_STOCK" => TipoAlertaInventario.LowStock,
            "CRITICAL_STOCK" => TipoAlertaInventario.CriticalStock,
            "OUT_OF_STOCK" => TipoAlertaInventario.OutOfStock,
            _ => (TipoAlertaInventario)(-1)
        };

        return Enum.IsDefined(tipo);
    }

    public sealed record ReconocerAlertaRequest(string Tipo, string? Usuario = null);
}
