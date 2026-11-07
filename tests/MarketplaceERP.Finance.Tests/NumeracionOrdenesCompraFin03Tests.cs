using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class NumeracionOrdenesCompraFin03Tests
{
    [Fact]
    public async Task CincuentaOrdenesCompra_MantienenNumeroUnicoDerivadoDeId()
    {
        await using var core = new CoreDbContext(
            new DbContextOptionsBuilder<CoreDbContext>()
                .UseInMemoryDatabase("fin03-core-" + Guid.NewGuid()).Options);
        await using var finance = new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase("fin03-finance-" + Guid.NewGuid()).Options);

        var proveedor = new Proveedor
        {
            Id = Guid.NewGuid(), NombreComercial = "Proveedor pruebas",
            RazonSocial = "Empresa Pruebas", Activo = true
        };
        core.Proveedores.Add(proveedor);
        await core.SaveChangesAsync();

        var controller = new ComprasController(
            finance,
            Mock.Of<ITresWayMatchingService>(),
            Mock.Of<IContabilidadService>(),
            Mock.Of<IInventoryService>(),
            core,
            NullLogger<ComprasController>.Instance);

        for (var i = 0; i < 50; i++)
        {
            var request = new CrearPORequest(
                proveedor.Id, proveedor.RazonSocial, null, null, null, false,
                new List<ItemPORequest> { new("SKU-TEST", "Producto", 1, 10m) });

            var response = await controller.CrearPO(request, CancellationToken.None);
            Assert.IsType<CreatedAtActionResult>(response);
        }

        var orders = await finance.OrdenesCompra.AsNoTracking().ToListAsync();
        Assert.Equal(50, orders.Count);
        Assert.Equal(50, orders.Select(x => x.NumeroPO)
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(orders, o =>
        {
            Assert.StartsWith("PO-", o.NumeroPO);
            Assert.EndsWith(o.Id.ToString("N"), o.NumeroPO);
            Assert.True(o.NumeroPO.Length <= 50);
        });
    }
}
