using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class CheckoutRecuperacionOperativaTests
{
    private sealed class Escenario : IDisposable
    {
        public readonly Guid OrdenId = Guid.NewGuid();
        public readonly Guid ReservaId = Guid.NewGuid();
        public readonly FinanceDbContext Db;
        public readonly Mock<IPaymentRecoveryGateway> Gateway = new(MockBehavior.Strict);
        public readonly Mock<IStockService> Stock = new(MockBehavior.Strict);
        public readonly CheckoutRecuperacionController Controller;
        private readonly ServiceProvider _provider;

        public Escenario(bool recovery = false, bool refunds = false,
            bool dev = true, bool gateway = false, bool seed = true)
        {
            Db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase($"REC_QA_{Guid.NewGuid():N}").Options);
            var conf = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Payments:EnableRecoveryQA"] = recovery.ToString(),
                    ["Payments:EnableRefundsQA"] = refunds.ToString()
                }).Build();
            var env = new Mock<IHostEnvironment>();
            env.SetupGet(e => e.EnvironmentName)
                .Returns(dev ? Environments.Development : Environments.Production);

            var services = new ServiceCollection();
            services.AddSingleton<FinanceDbContext>(Db);
            services.AddSingleton<IStockService>(Stock.Object);
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<EvaluadorConciliacionPago>>(
                NullLogger<EvaluadorConciliacionPago>.Instance);
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<RecuperacionFinancieraService>>(
                NullLogger<RecuperacionFinancieraService>.Instance);
            services.AddScoped<EvaluadorConciliacionPago>();
            services.AddScoped<RecuperacionFinancieraService>();
            if (gateway) services.AddSingleton<IPaymentRecoveryGateway>(Gateway.Object);
            _provider = services.BuildServiceProvider();

            Controller = new CheckoutRecuperacionController(Db, conf, env.Object, _provider,
                NullLogger<CheckoutRecuperacionController>.Instance);
            Identidad("Admin");
            if (seed) Sembrar();
        }

        public void Identidad(string rol, bool conGuid = true)
        {
            var claims = new List<Claim> { new(ClaimTypes.Role, rol) };
            if (conGuid) claims.Add(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
            Controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "QA")),
                    RequestServices = _provider
                }
            };
        }

        private void Sembrar()
        {
            Db.Ordenes.Add(new Orden
            {
                Id = OrdenId,
                NumeroOrden = $"QA-{OrdenId:N}",
                CompradorId = Guid.NewGuid(),
                VendedorId = Guid.NewGuid(),
                Estado = EstadoOrden.Pendiente,
                MontoTotal = 30m,
                SubTotal = 30m,
                Moneda = "USD"
            });
            Db.IntentosPago.Add(new IntentoPago
            {
                Id = Guid.NewGuid(),
                OrdenId = OrdenId,
                ReservaId = ReservaId,
                ReferenciaOperacion = $"PAY-{OrdenId:N}",
                Monto = 30m,
                Moneda = "USD",
                Estado = EstadoIntentoPago.Incierto
            });
            Db.SaveChanges();
        }

        public void PrepararReembolso()
        {
            var intento = Db.IntentosPago.Single();
            intento.Estado = EstadoIntentoPago.RequiereConciliacion;
            intento.TransaccionId = "TX-QA";
            Db.SaveChanges();
            Stock.Setup(s => s.LiberarStockAsync(ReservaId))
                .Returns(Task.CompletedTask);
        }

        public void Dispose() { _provider.Dispose(); Db.Dispose(); }
    }

    [Fact]
    public void RolesYEndpointsRequierenAdministrador()
    {
        var permiso = Assert.Single(typeof(CheckoutRecuperacionController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", permiso.Roles);
        Assert.Contains(typeof(CheckoutRecuperacionController).GetMethods(),
            m => m.Name == nameof(CheckoutRecuperacionController.Conciliar) &&
                 m.GetCustomAttributes(typeof(HttpPostAttribute), false).Length == 1);
        Assert.Contains(typeof(CheckoutRecuperacionController).GetMethods(),
            m => m.Name == nameof(CheckoutRecuperacionController.Reembolsar) &&
                 m.GetCustomAttributes(typeof(HttpPostAttribute), false).Length == 1);
    }

    [Fact]
    public async Task CompradorNoPuedeConciliarNiReembolsar()
    {
        using var e = new Escenario(recovery: true, refunds: true, gateway: true);
        e.Identidad("Comprador");
        Assert.IsType<ForbidResult>(await e.Controller.Conciliar(e.OrdenId));
        Assert.IsType<ForbidResult>(await e.Controller.Reembolsar(e.OrdenId, $"REEMBOLSAR-{e.OrdenId:N}"));
        e.Gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AdminSinIdentificadorNoPuedeEjecutarAcciones()
    {
        using var e = new Escenario(recovery: true, refunds: true, gateway: true);
        e.Identidad("Admin", conGuid: false);
        Assert.IsType<ForbidResult>(await e.Controller.Conciliar(e.OrdenId));
        Assert.IsType<ForbidResult>(await e.Controller.Reembolsar(e.OrdenId, "irrelevante"));
    }

    [Fact]
    public async Task BloqueoPorDefecto_NoInvocaProveedorNiCambiaBase()
    {
        using var e = new Escenario(gateway: true);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await e.Controller.Conciliar(e.OrdenId)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await e.Controller.Reembolsar(e.OrdenId, "x")).StatusCode);
        Assert.Empty(e.Db.ConciliacionesPago);
        Assert.Empty(e.Db.ReembolsosPago);
        e.Gateway.VerifyNoOtherCalls();
        e.Stock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProduccionSiempreBloqueada_InclusoConBanderasYProveedor()
    {
        using var e = new Escenario(recovery: true, refunds: true, dev: false, gateway: true);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await e.Controller.Conciliar(e.OrdenId)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await e.Controller.Reembolsar(e.OrdenId, "x")).StatusCode);
        e.Gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoHayGatewayRecuperacion_Devuelve503AunEnQA()
    {
        using var e = new Escenario(recovery: true, refunds: true, gateway: false);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await e.Controller.Conciliar(e.OrdenId)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await e.Controller.Reembolsar(e.OrdenId, "x")).StatusCode);
    }

    [Fact]
    public async Task OrdenInexistente_NoLlamaProveedor()
    {
        using var e = new Escenario(recovery: true, refunds: true, gateway: true);
        var idInexistente = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await e.Controller.Conciliar(idInexistente));
        Assert.IsType<NotFoundResult>(await e.Controller.Reembolsar(idInexistente, $"REEMBOLSAR-{idInexistente:N}"));
        e.Gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConciliacionQA_ConfirmaSinCobrarReembolsarNiConsumirStock()
    {
        using var e = new Escenario(recovery: true, gateway: true);
        e.Gateway.Setup(g => g.ConsultarEstadoAsync($"PAY-{e.OrdenId:N}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultaPagoResult(EstadoCobroProveedor.Confirmado, "TX-QA"));
        Assert.IsType<OkObjectResult>(await e.Controller.Conciliar(e.OrdenId));
        Assert.Single(e.Db.ConciliacionesPago);
        Assert.Equal(EstadoIntentoPago.RequiereConciliacion, e.Db.IntentosPago.Single().Estado);
        Assert.Equal(EstadoOrden.Pendiente, e.Db.Ordenes.Single().Estado);
        e.Gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
        e.Stock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReembolsarSinBanderaAdicional_Bloqueado()
    {
        using var e = new Escenario(recovery: true, refunds: false, gateway: true);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await e.Controller.Reembolsar(e.OrdenId, "x")).StatusCode);
        e.Gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReembolsarSinConfirmacionExacta_Devuelve400SinProveedor()
    {
        using var e = new Escenario(recovery: true, refunds: true, gateway: true);
        Assert.IsType<BadRequestObjectResult>(await e.Controller.Reembolsar(e.OrdenId, "CONFIRMAR"));
        Assert.Empty(e.Db.ReembolsosPago);
        e.Gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReembolsoQA_ConfirmadoYRepetido_NoDuplicaSolicitudNiConsumo()
    {
        using var e = new Escenario(recovery: true, refunds: true, gateway: true);
        e.PrepararReembolso();
        e.Gateway.Setup(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Completado, "REFUND-QA"));
        var confirmar = $"REEMBOLSAR-{e.OrdenId:N}";
        Assert.IsType<OkObjectResult>(await e.Controller.Reembolsar(e.OrdenId, confirmar));
        Assert.IsType<OkObjectResult>(await e.Controller.Reembolsar(e.OrdenId, confirmar));
        Assert.Equal(EstadoReembolsoPago.Finalizado, e.Db.ReembolsosPago.Single().Estado);
        Assert.Equal(EstadoOrden.Cancelada, e.Db.Ordenes.Single().Estado);
        e.Gateway.Verify(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        e.Stock.Verify(s => s.LiberarStockAsync(e.ReservaId), Times.Once);
        e.Stock.Verify(s => s.ConsumirReservasOrdenAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ReembolsoQA_Pendiente_NoLiberaReservaNiCierraOrden()
    {
        using var e = new Escenario(recovery: true, refunds: true, gateway: true);
        e.PrepararReembolso();
        e.Gateway.Setup(g => g.ReembolsarAsync(It.IsAny<ReembolsoPagoRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReembolsoPagoResult(EstadoReembolsoProveedor.Pendiente, "R-QA"));
        Assert.IsType<AcceptedResult>(await e.Controller.Reembolsar(e.OrdenId, $"REEMBOLSAR-{e.OrdenId:N}"));
        Assert.Equal(EstadoOrden.Pendiente, e.Db.Ordenes.Single().Estado);
        Assert.Equal(EstadoReembolsoPago.Pendiente, e.Db.ReembolsosPago.Single().Estado);
        e.Stock.VerifyNoOtherCalls();
    }
}
