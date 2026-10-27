/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Vista operativa de necesidades de reabastecimiento.
/// Compone Inventario (Catalog) con órdenes de compra abiertas (Finance)
/// sin duplicar responsabilidades financieras.
/// </summary>
[ApiController]
[Route("api/reabastecimiento")]
[Authorize]
public class ReabastecimientoController : ControllerBase
{
    private readonly IReabastecimientoService _reabastecimiento;
    private readonly FinanceDbContext _financeDb;

    public ReabastecimientoController(
        IReabastecimientoService reabastecimiento,
        FinanceDbContext financeDb)
    {
        _reabastecimiento = reabastecimiento;
        _financeDb = financeDb;
    }

    [HttpGet("necesidades")]
    public async Task<IActionResult> ObtenerNecesidades(CancellationToken cancellationToken)
    {
        var necesidades = await _reabastecimiento.ObtenerNecesidadesAsync(cancellationToken);
        var skus = necesidades.Select(n => n.Sku).Distinct().ToList();

        var comprasAbiertas = await _financeDb.OrdenesCompra
            .AsNoTracking()
            .Where(po => po.Estado == EstadoOrdenCompra.Borrador ||
                         po.Estado == EstadoOrdenCompra.Enviada ||
                         po.Estado == EstadoOrdenCompra.RecibidaParcial)
            .SelectMany(po => po.Items.Select(item => new
            {
                item.Sku,
                item.CantidadSolicitada,
                item.CantidadRecibida,
                po.NumeroPO
            }))
            .Where(x => skus.Contains(x.Sku))
            .ToListAsync(cancellationToken);

        var comprasPorSku = comprasAbiertas
            .Select(x => new
            {
                x.Sku,
                Pendiente = Math.Max(0, x.CantidadSolicitada - x.CantidadRecibida),
                x.NumeroPO
            })
            .Where(x => x.Pendiente > 0)
            .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    CantidadPendiente = g.Sum(x => x.Pendiente),
                    Ordenes = g.Select(x => x.NumeroPO).Distinct().OrderBy(x => x).ToArray()
                },
                StringComparer.OrdinalIgnoreCase);

        var items = necesidades.Select(n =>
        {
            comprasPorSku.TryGetValue(n.Sku, out var compra);
            var cantidadEnCompra = compra?.CantidadPendiente ?? 0;
            var proyectado = n.CantidadDisponible + cantidadEnCompra;
            var faltanteProyectado = Math.Max(0, (n.StockMinimo + 1) - proyectado);
            var estadoCompra = cantidadEnCompra <= 0
                ? "SIN_PO"
                : faltanteProyectado > 0
                    ? "COBERTURA_PARCIAL"
                    : "CUBIERTA_POR_PO";

            return new
            {
                n.ProductoId,
                n.Sku,
                n.Nombre,
                n.CategoriaNombre,
                n.ProveedorNombre,
                n.StockMinimo,
                n.CantidadFisica,
                n.CantidadReservada,
                n.CantidadBloqueada,
                n.CantidadDisponible,
                n.FaltanteHastaMinimo,
                n.CantidadParaSalirAlerta,
                n.NivelCriticidad,
                CantidadEnCompra = cantidadEnCompra,
                DisponibleProyectado = proyectado,
                FaltanteProyectado = faltanteProyectado,
                EstadoCompra = estadoCompra,
                OrdenesCompra = compra?.Ordenes ?? Array.Empty<string>()
            };
        }).ToList();

        return Ok(new
        {
            Total = items.Count,
            Agotados = items.Count(x => x.NivelCriticidad == "AGOTADO"),
            ConCompraEnCurso = items.Count(x => x.CantidadEnCompra > 0),
            SinCoberturaCompleta = items.Count(x => x.FaltanteProyectado > 0),
            Items = items
        });
    }
}
