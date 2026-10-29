using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Pruebas sin HTTP, pagos externos ni PostgreSQL. Validan las reglas del
/// contrato del controlador y su orquestador con dobles de prueba.
/// La unicidad real/concurrencia en PostgreSQL debe verificarse aparte.
/// </summary>
public sealed class CheckoutContratoI2ATests
{
    private const string Key = "CLAVE_I2A_12345678";

    private sealed class Entorno : IDisposable
    {
        public readonly Guid CompradorId = Guid.NewGuid();
        public readonly Guid VendedorId = Guid.NewGuid();
        public readonly Guid ProductoId = Guid.NewGuid();
        public readonly FinanceDbContext Finance;
        public readonly CatalogDbContext Catalog;
        public readonly Mock<IStockService> Stock = new();
        public readonly Mock<IPaymentGateway> Pago = new();
        public readonly Mock<ICommissionService> Comision = new();
        public readonly CheckoutController Controller;

        public Entorno()
        {
            Finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase($"Finance_I2A_{Guid.NewGuid():N}").Options);
            Catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase($"Catalog_I2A_{Guid.NewGuid():N}").Options);

            Stock.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
                .ReturnsAsync(new ReservaResult(Guid.NewGuid(), true, null));
            Stock.Setup(s => s.ConfirmarReservaAsync(It.IsAny<Guid>()))
                .Returns(Task.CompletedTask);
            Pago.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
                .ReturnsAsync(new PagoResult(true, "TXN-I2A-TEST", null));
            Comision.Setup(c => c.CalcularComisionAsync(It.IsAny<Guid>(), It.IsAny<decimal>()))
                .ReturnsAsync(2m);

            var orquestador = new CheckoutOrchestrator(
                Stock.Object, Comision.Object, Pago.Object, Finance,
                NullLogger<CheckoutOrchestrator>.Instance);
            Controller = new CheckoutController(orquestador, Finance, Catalog,
                NullLogger<CheckoutController>.Instance);
            EstablecerCompradorAutenticado();
        }

        public void EstablecerCompradorAutenticado()
        {
            Controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, CompradorId.ToString()),
                        new Claim(ClaimTypes.Role, "Comprador")
                    }, "Prueba"))
                }
            };
        }

        public async Task RegistrarProductoAsync(bool publicado = true,
            string estado = "Activo", decimal precio = 12.50m, string moneda = "USD")
        {
            Catalog.Productos.Add(new Producto
            {
                Id = ProductoId,
                VendedorId = VendedorId,
                CategoriaId = Guid.NewGuid(),
                Sku = "SKU-I2A-001",
                Nombre = "Producto de prueba I2A",
                CategoriaNombre = "Pruebas",
                PrecioB2C = precio,
                PrecioB2B = precio,
                Moneda = moneda,
                EstaPublicado = publicado,
                EstadoProducto = estado
            });
            await Catalog.SaveChangesAsync();
        }

        public void Dispose()
        {
            Finance.Dispose();
            Catalog.Dispose();
        }
    }

    [Fact]
    public async Task CantidadCero_Devuelve400_SinCrearOrden()
    {
        using var e = new Entorno();
        var r = await e.Controller.ProcesarCheckout(new CheckoutRequest(Guid.NewGuid(), 0), Key);
        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Empty(e.Finance.Ordenes);
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
    }

    [Fact]
    public async Task SinClaveIdempotente_Devuelve400_SinPago()
    {
        using var e = new Entorno();
        var r = await e.Controller.ProcesarCheckout(new CheckoutRequest(Guid.NewGuid(), 1), null);
        Assert.IsType<BadRequestObjectResult>(r);
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
    }

    [Fact]
    public async Task SinGuidAutenticado_Devuelve401()
    {
        using var e = new Entorno();
        e.Controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        var r = await e.Controller.ProcesarCheckout(new CheckoutRequest(Guid.NewGuid(), 1), Key);
        Assert.IsType<UnauthorizedObjectResult>(r);
        Assert.Empty(e.Finance.Ordenes);
    }

    [Fact]
    public async Task ProductoInexistente_Devuelve409_SinPago()
    {
        using var e = new Entorno();
        var r = await e.Controller.ProcesarCheckout(new CheckoutRequest(Guid.NewGuid(), 1), Key);
        Assert.IsType<ConflictObjectResult>(r);
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
    }

    [Theory]
    [InlineData(false, "Activo", 12.50, "USD")]
    [InlineData(true, "Inactivo", 12.50, "USD")]
    [InlineData(true, "Activo", 0.00, "USD")]
    [InlineData(true, "Activo", 12.50, "EUR")]
    public async Task ProductoNoApto_Devuelve409_SinPago(
        bool publicado, string estado, double precio, string moneda)
    {
        using var e = new Entorno();
        await e.RegistrarProductoAsync(publicado, estado, (decimal)precio, moneda);
        var r = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 1), Key);
        Assert.IsType<ConflictObjectResult>(r);
        Assert.Empty(e.Finance.Ordenes);
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
    }

    [Fact]
    public async Task CheckoutExitoso_UsaPrecioYVendedorDelServidor_QuedaAprobada()
    {
        using var e = new Entorno();
        await e.RegistrarProductoAsync();
        var r = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 2), Key);
        var ok = Assert.IsType<OkObjectResult>(r);
        var resultado = Assert.IsType<CheckoutResult>(ok.Value);
        var orden = await e.Finance.Ordenes.Include(o => o.Items).SingleAsync();
        Assert.Equal(e.CompradorId, orden.CompradorId);
        Assert.Equal(e.VendedorId, orden.VendedorId);
        Assert.Equal(25.00m, orden.MontoTotal);
        Assert.Equal(12.50m, Assert.Single(orden.Items).PrecioUnitario);
        Assert.Equal(EstadoOrden.Aprobada, orden.Estado);
        Assert.NotNull(orden.FechaAprobacion);
        Assert.Equal(orden.Id, resultado.OrdenId);
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.Is<PagoRequest>(x => x.MontoTotal == 25.00m)), Times.Once);
        e.Stock.Verify(s => s.ConsumirReservasOrdenAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task RepeticionMismaClaveYDatos_ReutilizaOrden_SinNuevoPago()
    {
        using var e = new Entorno();
        await e.RegistrarProductoAsync();
        var primera = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 2), Key);
        var segunda = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 2), Key);
        var r1 = Assert.IsType<CheckoutResult>(Assert.IsType<OkObjectResult>(primera).Value);
        var r2 = Assert.IsType<CheckoutResult>(Assert.IsType<OkObjectResult>(segunda).Value);
        Assert.Equal(r1.OrdenId, r2.OrdenId);
        Assert.Equal(r1.TransaccionId, r2.TransaccionId);
        Assert.Equal(1, await e.Finance.Ordenes.CountAsync());
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once);
        e.Stock.Verify(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()), Times.Once);
    }

    [Fact]
    public async Task MismaClaveDistintoPayload_Devuelve409_SinNuevoPago()
    {
        using var e = new Entorno();
        await e.RegistrarProductoAsync();
        Assert.IsType<OkObjectResult>(await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 1), Key));
        var segunda = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 2), Key);
        Assert.IsType<ConflictObjectResult>(segunda);
        Assert.Equal(1, await e.Finance.Ordenes.CountAsync());
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once);
    }

    [Fact]
    public async Task PagoRechazado_ClaveNoVuelveAIntentarCobroAutomaticamente()
    {
        using var e = new Entorno();
        await e.RegistrarProductoAsync();
        e.Pago.Setup(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()))
            .ReturnsAsync(new PagoResult(false, null, "Rechazo simulado"));
        e.Stock.Setup(s => s.LiberarStockAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        var primera = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 1), Key);
        var segunda = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 1), Key);
        Assert.IsType<ConflictObjectResult>(primera);
        Assert.IsType<ConflictObjectResult>(segunda);
        Assert.Equal(1, await e.Finance.Ordenes.CountAsync());
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Once);
    }

    [Fact]
    public async Task StockInsuficiente_CancelaOrden_SinIntentarPago()
    {
        using var e = new Entorno();
        await e.RegistrarProductoAsync();
        e.Stock.Setup(s => s.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Guid>()))
            .ReturnsAsync(new ReservaResult(Guid.Empty, false, "Sin stock"));
        var r = await e.Controller.ProcesarCheckout(new CheckoutRequest(e.ProductoId, 1), Key);
        Assert.IsType<ConflictObjectResult>(r);
        Assert.Equal(EstadoOrden.Cancelada, (await e.Finance.Ordenes.SingleAsync()).Estado);
        e.Pago.Verify(p => p.ProcesarPagoAsync(It.IsAny<PagoRequest>()), Times.Never);
    }
}
