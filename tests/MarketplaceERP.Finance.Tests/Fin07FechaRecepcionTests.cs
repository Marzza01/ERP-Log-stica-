using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public class Fin07FechaRecepcionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MatchConforme_NoFalsificaFechaDeRecepcion(bool tieneFechaRecepcion)
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase("fin07-fecha-" + Guid.NewGuid().ToString("N")).Options;
        var fechaOriginal = new DateTime(2026, 11, 5, 16, 30, 0, DateTimeKind.Utc);
        var proveedorId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();

        await using (var db = new FinanceDbContext(options))
        {
            db.OrdenesCompra.Add(new OrdenCompra
            {
                Id = poId,
                NumeroPO = "PO-TEST-FIN07-FECHA",
                ProveedorId = proveedorId,
                ProveedorNombre = "Proveedor de prueba",
                Estado = EstadoOrdenCompra.RecibidaCompleta,
                FechaRecepcionReal = tieneFechaRecepcion ? fechaOriginal : null,
                SubTotal = 100m,
                ImpuestoIva = 13m,
                MontoTotal = 113m,
                Items =
                {
                    new ItemOrdenCompra
                    {
                        Id = itemId,
                        OrdenCompraId = poId,
                        Sku = "SKU-TEST",
                        NombreProducto = "Artículo de prueba",
                        CantidadSolicitada = 10,
                        CantidadRecibida = 10,
                        PrecioUnitarioAcordado = 10m
                    }
                }
            });
            db.FacturasProveedor.Add(new FacturaProveedor
            {
                Id = facturaId,
                OrdenCompraId = poId,
                ProveedorId = proveedorId,
                NumeroFactura = "FACT-TEST-FIN07",
                MontoSubTotal = 100m,
                MontoIva = 13m,
                MontoTotal = 113m,
                Items =
                {
                    new ItemFacturaProveedor
                    {
                        Id = Guid.NewGuid(),
                        FacturaProveedorId = facturaId,
                        ItemOrdenCompraId = itemId,
                        CantidadFacturada = 10,
                        PrecioUnitarioFacturado = 10m
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new FinanceDbContext(options))
        {
            var service = new TresWayMatchingService(
                db, NullLogger<TresWayMatchingService>.Instance);
            var resultado = await service.EjecutarMatchAsync(
                new Validar3WayMatchRequest(poId, facturaId, 10));
            Assert.Equal(Estado3WayMatch.Conforme, resultado.Resultado);
            Assert.True(resultado.AutorizadoParaPago);
        }

        await using (var db = new FinanceDbContext(options))
        {
            var po = await db.OrdenesCompra.SingleAsync();
            Assert.Equal(tieneFechaRecepcion ? fechaOriginal : (DateTime?)null,
                po.FechaRecepcionReal);
            var factura = await db.FacturasProveedor.SingleAsync();
            Assert.True(factura.AprobadaParaPago);
        }
    }
}
