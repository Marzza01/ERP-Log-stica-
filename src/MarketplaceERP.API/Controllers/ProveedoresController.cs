/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Services;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Controllers;

/// <summary>
/// Vista logística de proveedores. Consume el maestro único core.proveedores;
/// no crea ni duplica proveedores desde Catalog o Finance.
/// </summary>
[ApiController]
[Route("api/proveedores")]
[Authorize]
public class ProveedoresController : ControllerBase
{
    private readonly CoreDbContext _coreDb;
    private readonly CatalogDbContext _catalogDb;
    private readonly FinanceDbContext _financeDb;

    public ProveedoresController(CoreDbContext coreDb, CatalogDbContext catalogDb, FinanceDbContext financeDb)
    {
        _coreDb = coreDb;
        _catalogDb = catalogDb;
        _financeDb = financeDb;
    }

    [HttpGet("logistica")]
    public async Task<IActionResult> ListarLogistica(
        [FromQuery] string? buscar = null,
        CancellationToken cancellationToken = default)
    {
        var proveedoresQuery = _coreDb.Proveedores
            .AsNoTracking()
            .Where(p => p.Activo);

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var termino = buscar.Trim().ToLower();
            proveedoresQuery = proveedoresQuery.Where(p =>
                p.NombreComercial.ToLower().Contains(termino) ||
                p.RazonSocial.ToLower().Contains(termino) ||
                p.CategoriaSuministro.ToLower().Contains(termino));
        }

        var proveedores = await proveedoresQuery
            .OrderBy(p => p.NombreComercial)
            .ToListAsync(cancellationToken);

        var todosProveedoresActivos = await _coreDb.Proveedores
            .AsNoTracking()
            .Where(p => p.Activo)
            .ToListAsync(cancellationToken);

        var productos = await _catalogDb.Productos
            .AsNoTracking()
            .Include(p => p.Stocks)
            .Where(p => p.EstaPublicado && p.EstadoProducto == "Activo")
            .ToListAsync(cancellationToken);

        var posAbiertas = await _financeDb.OrdenesCompra
            .AsNoTracking()
            .Include(po => po.Items)
            .Where(po => po.Estado == EstadoOrdenCompra.Borrador ||
                         po.Estado == EstadoOrdenCompra.Enviada ||
                         po.Estado == EstadoOrdenCompra.RecibidaParcial)
            .ToListAsync(cancellationToken);

        var vinculaciones = productos
            .Select(p => new
            {
                Producto = p,
                Proveedor = ProveedorLogisticaMatcher.Resolver(todosProveedoresActivos, p.ProveedorNombre)
            })
            .ToList();

        var items = proveedores.Select(proveedor =>
        {
            var productosProveedor = vinculaciones
                .Where(x => x.Proveedor?.Id == proveedor.Id)
                .Select(x => x.Producto)
                .ToList();

            var bajoMinimo = productosProveedor.Count(p =>
            {
                var disponible = p.Stocks.Sum(s => s.CantidadTotal - s.CantidadReservada - s.CantidadBloqueada);
                return p.StockMinimo > 0 && disponible <= p.StockMinimo;
            });

            var compras = posAbiertas.Where(po => po.ProveedorId == proveedor.Id).ToList();
            var pendienteRecepcion = compras
                .SelectMany(po => po.Items)
                .Sum(i => Math.Max(0, i.CantidadSolicitada - i.CantidadRecibida));

            return new
            {
                proveedor.Id,
                proveedor.NombreComercial,
                proveedor.RazonSocial,
                proveedor.CategoriaSuministro,
                proveedor.ContactoPrincipal,
                proveedor.Email,
                proveedor.Telefono,
                proveedor.Direccion,
                proveedor.PlazoCreditoDias,
                proveedor.OtifHistorico,
                ProductosCatalogo = productosProveedor.Count,
                ProductosBajoMinimo = bajoMinimo,
                OrdenesCompraAbiertas = compras.Count,
                UnidadesPendientesRecepcion = pendienteRecepcion
            };
        }).ToList();

        var nombresNoVinculados = vinculaciones
            .Where(x => x.Proveedor is null && !string.IsNullOrWhiteSpace(x.Producto.ProveedorNombre))
            .Select(x => x.Producto.ProveedorNombre.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();

        return Ok(new
        {
            Total = items.Count,
            ConProductosCatalogo = items.Count(x => x.ProductosCatalogo > 0),
            ConCompraAbierta = items.Count(x => x.OrdenesCompraAbiertas > 0),
            NombresCatalogoNoVinculados = nombresNoVinculados,
            Items = items
        });
    }
}
