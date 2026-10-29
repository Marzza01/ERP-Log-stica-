using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using MarketplaceERP.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class ReservasExpiracionI3Tests
{
    private static ReservaStock Reserva(int minutos = -15) => new()
    {
        Id = Guid.NewGuid(), OrdenId = Guid.NewGuid(), ProductoId = Guid.NewGuid(),
        CantidadOriginal = 5, Estado = EstadoReserva.Pendiente,
        FechaReserva = DateTime.UtcNow.AddHours(-2),
        FechaExpiracion = DateTime.UtcNow.AddMinutes(minutos)
    };

    [Fact]
    public void Politica_AutorizaSoloOrdenCanceladaSinHuellaFinanciera()
    {
        var r = Reserva();
        var result = PoliticaExpiracionReserva.Evaluar(r, EstadoOrden.Cancelada,
            false, false, DateTime.UtcNow);
        Assert.True(result.Elegible);
        Assert.Equal("ELEGIBLE_CANCELADA_SIN_PAGO", result.Motivo);
    }

    [Theory]
    [InlineData(EstadoOrden.Pendiente)]
    [InlineData(EstadoOrden.Aprobada)]
    [InlineData(EstadoOrden.EnProceso)]
    [InlineData(EstadoOrden.Enviada)]
    [InlineData(EstadoOrden.Entregada)]
    public void Politica_RetieneOrdenNoCancelada(EstadoOrden estado)
    {
        var resultado = PoliticaExpiracionReserva.Evaluar(Reserva(), estado,
            false, false, DateTime.UtcNow);
        Assert.False(resultado.Elegible);
        Assert.Equal("ORDEN_NO_CANCELADA_REVISAR", resultado.Motivo);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Politica_RetieneTodaHuellaFinanciera(bool intento, bool reembolso)
    {
        var resultado = PoliticaExpiracionReserva.Evaluar(Reserva(), EstadoOrden.Cancelada,
            intento, reembolso, DateTime.UtcNow);
        Assert.False(resultado.Elegible);
        Assert.Equal("EXISTE_HUELLA_FINANCIERA_REVISAR", resultado.Motivo);
    }

    [Fact]
    public void Politica_RetieneOrdenDesconocida()
    {
        var resultado = PoliticaExpiracionReserva.Evaluar(Reserva(), null,
            false, false, DateTime.UtcNow);
        Assert.Equal("ORDEN_NO_ENCONTRADA_REVISAR", resultado.Motivo);
        Assert.False(resultado.Elegible);
    }

    [Fact]
    public void Politica_NoExpiraReservaVigente()
    {
        var resultado = PoliticaExpiracionReserva.Evaluar(Reserva(20), EstadoOrden.Cancelada,
            false, false, DateTime.UtcNow);
        Assert.Equal("AUN_VIGENTE", resultado.Motivo);
    }

    [Fact]
    public void Politica_NoExpiraReservaSinFecha()
    {
        var r = Reserva(); r.FechaExpiracion = null;
        var resultado = PoliticaExpiracionReserva.Evaluar(r, EstadoOrden.Cancelada,
            false, false, DateTime.UtcNow);
        Assert.False(resultado.Elegible);
        Assert.Equal("SIN_VENCIMIENTO_CONFIGURADO", resultado.Motivo);
    }

    [Theory]
    [InlineData(EstadoReserva.Confirmada)]
    [InlineData(EstadoReserva.ParcialmenteLiberada)]
    [InlineData(EstadoReserva.ParcialmenteConsumida)]
    [InlineData(EstadoReserva.Consumida)]
    [InlineData(EstadoReserva.Expirada)]
    public void Politica_NoExpiraEstadosComprometidosONoPendientes(EstadoReserva estado)
    {
        var r = Reserva(); r.Estado = estado;
        var resultado = PoliticaExpiracionReserva.Evaluar(r, EstadoOrden.Cancelada,
            false, false, DateTime.UtcNow);
        Assert.False(resultado.Elegible);
    }

    [Fact]
    public void Politica_NoDuplicaLiberacion()
    {
        var r = Reserva(); r.CantidadLiberada = r.CantidadOriginal;
        var resultado = PoliticaExpiracionReserva.Evaluar(r, EstadoOrden.Cancelada,
            false, false, DateTime.UtcNow);
        Assert.Equal("SIN_CANTIDAD_PENDIENTE", resultado.Motivo);
    }

    [Fact]
    public async Task Consulta_DistingueCanceladaYOrdenActiva_SinEscribir()
    {
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"I3_C_{Guid.NewGuid():N}").Options);
        await using var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I3_F_{Guid.NewGuid():N}").Options);
        var cancelada = Reserva();
        var pendiente = Reserva();
        catalog.ReservasStock.AddRange(cancelada, pendiente);
        finance.Ordenes.AddRange(new Orden
        {
            Id = cancelada.OrdenId, Estado = EstadoOrden.Cancelada,
            NumeroOrden = $"CAN-{cancelada.OrdenId:N}", MontoTotal = 20m
        }, new Orden
        {
            Id = pendiente.OrdenId, Estado = EstadoOrden.Pendiente,
            NumeroOrden = $"PEN-{pendiente.OrdenId:N}", MontoTotal = 20m
        });
        await catalog.SaveChangesAsync();
        await finance.SaveChangesAsync();
        var stock = new Mock<IStockService>(MockBehavior.Strict);
        var servicio = new ReservasExpiracionService(catalog, finance,
            stock.Object, NullLogger<ReservasExpiracionService>.Instance);
        var resultado = await servicio.ConsultarAsync(10);
        Assert.Equal(2, resultado.Count);
        Assert.Single(resultado.Where(r => r.Elegible));
        Assert.Equal(2, await catalog.ReservasStock.CountAsync());
        Assert.Equal(0, await catalog.MovimientosInventario.CountAsync());
        stock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Consulta_RetieneOrdenCanceladaConSplitHistorico()
    {
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"I3_C_{Guid.NewGuid():N}").Options);
        await using var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I3_F_{Guid.NewGuid():N}").Options);
        var reserva = Reserva();
        catalog.ReservasStock.Add(reserva);
        finance.Ordenes.Add(new Orden { Id = reserva.OrdenId,
            Estado = EstadoOrden.Cancelada,
            NumeroOrden = $"LEGADO-{reserva.OrdenId:N}", MontoTotal = 20m });
        finance.SplitsPago.Add(new SplitPago { Id = Guid.NewGuid(),
            OrdenId = reserva.OrdenId, MontoTotal = 20m,
            TransaccionId = "LEGACY-TRX" });
        await catalog.SaveChangesAsync();
        await finance.SaveChangesAsync();
        var servicio = new ReservasExpiracionService(catalog, finance,
            new Mock<IStockService>(MockBehavior.Strict).Object,
            NullLogger<ReservasExpiracionService>.Instance);
        var resultado = Assert.Single(await servicio.ConsultarAsync(10));
        Assert.False(resultado.Elegible);
        Assert.Equal("EXISTE_HUELLA_FINANCIERA_REVISAR", resultado.Motivo);
    }

    [Fact]
    public async Task Ejecucion_EsRechazadaSiNoEsPostgreSQL()
    {
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"I3_C_{Guid.NewGuid():N}").Options);
        await using var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I3_F_{Guid.NewGuid():N}").Options);
        var servicio = new ReservasExpiracionService(catalog, finance,
            new Mock<IStockService>(MockBehavior.Strict).Object,
            NullLogger<ReservasExpiracionService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EjecutarAsync(1));
    }

    [Fact]
    public async Task Controller_BloqueaEscrituraSinHabilitacionExplicita()
    {
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"I3_C_{Guid.NewGuid():N}").Options);
        await using var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I3_F_{Guid.NewGuid():N}").Options);
        var service = new ReservasExpiracionService(catalog, finance,
            new Mock<IStockService>(MockBehavior.Strict).Object,
            NullLogger<ReservasExpiracionService>.Instance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Reservas:EnableExpirationQA"] = "true" }).Build();
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(Environments.Development);
        var controller = new ReservasExpiracionController(service, catalog, config, env.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
            }, "QA"))
        } };
        var respuesta = await controller.Ejecutar(1, "EXPIRAR-RESERVAS-QA");
        Assert.Equal(503, Assert.IsType<ObjectResult>(respuesta).StatusCode);
        Assert.Empty(await catalog.ReservasStock.ToListAsync());
    }

    [Fact]
    public async Task Checkout_CanceladoAntesDelProveedor_NoCobraYLiberaReserva()
    {
        await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"I3_FIN_{Guid.NewGuid():N}").Options);
        var ordenId = Guid.NewGuid();
        db.Ordenes.Add(new Orden { Id = ordenId, Estado = EstadoOrden.Cancelada,
            NumeroOrden = $"CANCELADA-{ordenId:N}", MontoTotal = 30m });
        await db.SaveChangesAsync();
        var reservaId = Guid.NewGuid();
        var stock = new Mock<IStockService>(MockBehavior.Strict);
        stock.Setup(x => x.ReservarStockAsync(It.IsAny<Guid>(), It.IsAny<int>(), ordenId))
            .ReturnsAsync(new ReservaResult(reservaId, true, null));
        stock.Setup(x => x.LiberarStockAsync(reservaId)).Returns(Task.CompletedTask);
        var pago = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var comision = new Mock<ICommissionService>(MockBehavior.Strict);
        var sut = new CheckoutOrchestrator(stock.Object, comision.Object, pago.Object,
            db, NullLogger<CheckoutOrchestrator>.Instance);
        var command = new CheckoutCommand(ordenId, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 1, 30m, "USD");
        await Assert.ThrowsAsync<PagoFallidoException>(() => sut.ProcesarCheckoutAsync(command));
        pago.VerifyNoOtherCalls();
        comision.VerifyNoOtherCalls();
        stock.Verify(x => x.LiberarStockAsync(reservaId), Times.Once);
        Assert.Equal(EstadoIntentoPago.Rechazado, (await db.IntentosPago.SingleAsync()).Estado);
    }

    [Fact]
    public void Controller_TieneAutorizacionAdminObligatoria()
    {
        var atributo = (AuthorizeAttribute?)Attribute.GetCustomAttribute(
            typeof(ReservasExpiracionController), typeof(AuthorizeAttribute));
        Assert.Equal("Admin", atributo?.Roles);
    }
}
