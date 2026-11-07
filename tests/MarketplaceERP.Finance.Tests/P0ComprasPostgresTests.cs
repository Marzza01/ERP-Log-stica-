using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// P0: recorrido real de Finance, Core y Catalog sobre un PostgreSQL efímero
/// independiente de Neon y de cualquier cuenta, proveedor o pago real.
/// </summary>
public class P0ComprasPostgresTests
{
    [Fact]
    public async Task Orden_RecepcionParcial_Reintentos_Factura_Match_Asiento()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            return; // CI exige esta variable y PostgreSQL disponible.

        var database = "p0_flow_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(connection);
        var testConnection = new NpgsqlConnectionStringBuilder(connection) { Database = database }.ConnectionString;

        await using (var root = new NpgsqlConnection(admin.ConnectionString))
        {
            await root.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", root);
            await command.ExecuteNonQueryAsync();
        }

        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(testConnection).Options;
        var catalogOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(testConnection).Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(testConnection).Options;

        await using var core = new CoreDbContext(coreOptions);
        await using var catalog = new CatalogDbContext(catalogOptions);
        await using var finance = new FinanceDbContext(financeOptions);
        // Cada módulo crea solo su esquema; no se necesita ni se altera Neon.
        await core.Database.ExecuteSqlRawAsync(core.Database.GenerateCreateScript());
        await catalog.Database.ExecuteSqlRawAsync(catalog.Database.GenerateCreateScript());
        await finance.Database.ExecuteSqlRawAsync(finance.Database.GenerateCreateScript());

        var proveedor = new Proveedor
        {
            Id = Guid.NewGuid(), NombreComercial = "Proveedor ficticio P0",
            RazonSocial = "Proveedor ficticio P0", Activo = true
        };
        core.Proveedores.Add(proveedor);
        await core.SaveChangesAsync();

        var ubicacion = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(), Codigo = "P0-REC-01", Pasillo = "A",
            Rack = "R1", Nivel = 1, Posicion = "P1", CapacidadMaxima = 100
        };
        catalog.UbicacionesAlmacen.Add(ubicacion);
        catalog.Productos.AddRange(
            new Producto
            {
                Id = Guid.NewGuid(), Sku = "P0-ARROZ", Nombre = "Arroz demo",
                CategoriaNombre = "Alimentos", PrecioB2C = 5m, PrecioB2B = 3.55m,
                StockMinimo = 5, EstadoProducto = "Activo"
            },
            new Producto
            {
                Id = Guid.NewGuid(), Sku = "P0-CAFE", Nombre = "Café demo",
                CategoriaNombre = "Alimentos", PrecioB2C = 14m, PrecioB2B = 10.06m,
                StockMinimo = 5, EstadoProducto = "Activo"
            });
        await catalog.SaveChangesAsync();

        // InventoryService es interno al módulo: crearlo por reflexión usando
        // su constructor público, sin añadir una API pública solo para tests.
        var impl = typeof(IInventoryService).Assembly.GetType(
            "MarketplaceERP.Catalog.Services.InventoryService", throwOnError: true)!;
        var nullLogger = typeof(NullLogger<>).MakeGenericType(impl)
            .GetProperty("Instance")!.GetValue(null)!;
        var inventory = (IInventoryService)Activator.CreateInstance(
            impl, catalog, nullLogger)!;

        var matching = new TresWayMatchingService(
            finance, NullLogger<TresWayMatchingService>.Instance);
        var ledger = new ContabilidadService(
            finance, NullLogger<ContabilidadService>.Instance);
        var controller = new ComprasController(
            finance, matching, ledger, inventory, core,
            NullLogger<ComprasController>.Instance);

        Assert.IsType<CreatedAtActionResult>(await controller.CrearPO(
            new CrearPORequest(proveedor.Id, "Proveedor ficticio P0", null,
                "Caso de prueba aislado", null, false,
                new List<ItemPORequest>
                {
                    new("P0-ARROZ", "Arroz demo", 24, 3.55m),
                    new("P0-CAFE", "Café demo", 12, 10.06m)
                }), CancellationToken.None));

        var po = await finance.OrdenesCompra.Include(x => x.Items).SingleAsync();
        Assert.Equal(205.92m, po.SubTotal);
        Assert.Equal(26.77m, po.ImpuestoIva);
        Assert.Equal(232.69m, po.MontoTotal);
        Assert.IsType<OkObjectResult>(await controller.EnviarPO(po.Id, CancellationToken.None));

        var arroz = po.Items.Single(i => i.Sku == "P0-ARROZ");
        var cafe = po.Items.Single(i => i.Sku == "P0-CAFE");
        var operacionArroz = Guid.NewGuid();
        var operacionCafe = Guid.NewGuid();

        // Primer ítem confirma 24; segundo falla por WMS inexistente.
        // Finance debe persistir únicamente el movimiento físico confirmado.
        var parcial = await controller.RecibirMercaderia(po.Id,
            new RecibirMercaderiaRequest(new List<RecibirMercaderiaItemRequest>
            {
                new(arroz.Id, 24, 24, ubicacion.Id, operacionArroz),
                new(cafe.Id, 12, 12, Guid.NewGuid(), operacionCafe)
            }, "Intento parcial"), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(parcial);

        await using (var check = new FinanceDbContext(financeOptions))
        {
            var saved = await check.OrdenesCompra.Include(x => x.Items).SingleAsync();
            Assert.Equal(EstadoOrdenCompra.RecibidaParcial, saved.Estado);
            Assert.Equal(24, saved.Items.Single(x => x.Sku == "P0-ARROZ").CantidadRecibida);
            Assert.Equal(0, saved.Items.Single(x => x.Sku == "P0-CAFE").CantidadRecibida);
        }
        Assert.Equal(24, await catalog.StockFisico.SumAsync(s => s.CantidadTotal));
        Assert.Single(await catalog.MovimientosInventario.ToListAsync());

        // Misma recepción de arroz + recepción pendiente de café.
        var requestValido = new RecibirMercaderiaRequest(new List<RecibirMercaderiaItemRequest>
        {
            new(arroz.Id, 24, 24, ubicacion.Id, operacionArroz),
            new(cafe.Id, 12, 12, ubicacion.Id, operacionCafe)
        }, "Recuperación idempotente");
        Assert.IsType<OkObjectResult>(await controller.RecibirMercaderia(
            po.Id, requestValido, CancellationToken.None));

        Assert.Equal(36, await catalog.StockFisico.SumAsync(s => s.CantidadTotal));
        Assert.Equal(2, await catalog.MovimientosInventario.CountAsync(
            m => m.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound));

        // El replay exacto no genera stock; uno con nuevo token se rechaza.
        Assert.IsType<OkObjectResult>(await controller.RecibirMercaderia(
            po.Id, requestValido, CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.RecibirMercaderia(
            po.Id, new RecibirMercaderiaRequest(new()
            {
                new(arroz.Id, 24, 24, ubicacion.Id, Guid.NewGuid())
            }, "Token inválido"), CancellationToken.None));

        // Documento sintético; no se ejecuta pago ni se llama a un proveedor.
        Assert.IsType<CreatedAtActionResult>(await controller.RegistrarFactura(
            new RegistrarFacturaRequest(po.Id, "FAC-P0-DEMO", 205.92m, 232.69m,
                null, null, new()
                {
                    new(arroz.Id, 24, 3.55m),
                    new(cafe.Id, 12, 10.06m)
                }), CancellationToken.None));
        var factura = await finance.FacturasProveedor.SingleAsync();
        Assert.False(factura.Pagada);

        var match = await matching.EjecutarMatchAsync(
            new Validar3WayMatchRequest(po.Id, factura.Id, 36));
        Assert.Equal(Estado3WayMatch.Conforme, match.Resultado);
        Assert.True(match.AutorizadoParaPago); // banderín contable, NO pago ejecutado

        // Asiento de compra simulado sobre la misma base efímera.
        var asiento = await ledger.GenerarAsientoCompraAsync(
            po.Id, factura.MontoSubTotal, factura.MontoIva);
        Assert.Equal(232.69m, asiento.TotalDebe);
        Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        Assert.Equal(205.92m, asiento.Lineas.Single(x => x.CodigoCuenta == "1108").Debe);
        Assert.Equal(26.77m, asiento.Lineas.Single(x => x.CodigoCuenta == "1110").Debe);
        Assert.Equal(232.69m, asiento.Lineas.Single(x => x.CodigoCuenta == "2101").Haber);

        await using var verify = new FinanceDbContext(financeOptions);
        var partidas = await verify.PartidasContables.Include(x => x.Lineas).ToListAsync();
        Assert.Single(partidas);
        Assert.All(partidas, p =>
        {
            Assert.Equal(p.TotalDebe, p.Lineas.Sum(x => x.Debe));
            Assert.Equal(p.TotalHaber, p.Lineas.Sum(x => x.Haber));
            Assert.Equal(p.TotalDebe, p.TotalHaber);
        });
        Assert.Equal(36, (await verify.OrdenesCompra.Include(p => p.Items)
            .SingleAsync()).Items.Sum(i => i.CantidadRecibida));
        Assert.Equal(2, await catalog.MovimientosInventario.CountAsync(
            m => m.TipoMovimiento == TipoMovimientoInventario.RecepcionInbound));
        Assert.False((await verify.FacturasProveedor.SingleAsync()).Pagada);
    }
}
