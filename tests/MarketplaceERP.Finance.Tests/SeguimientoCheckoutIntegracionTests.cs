using System.Security.Claims;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class SeguimientoCheckoutIntegracionTests
{
    private sealed class Escenario : IDisposable
    {
        public readonly Guid Comprador = Guid.NewGuid();
        public readonly Guid OrdenId = Guid.NewGuid();
        public readonly FinanceDbContext Finance;
        public readonly CatalogDbContext Catalog;
        public readonly CheckoutSeguimientoController Controller;

        public Escenario()
        {
            Finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase($"F_{Guid.NewGuid():N}").Options);
            Catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase($"C_{Guid.NewGuid():N}").Options);
            Controller = new CheckoutSeguimientoController(Finance, Catalog);
            Identidad("Comprador", Comprador);
            Finance.Ordenes.Add(new Orden
            {
                Id = OrdenId, NumeroOrden = $"QA-{OrdenId:N}",
                CompradorId = Comprador, VendedorId = Guid.NewGuid(),
                MontoTotal = 50m, SubTotal = 50m, Moneda = "USD",
                Estado = EstadoOrden.Pendiente
            });
            Finance.SaveChanges();
        }

        public void Identidad(string rol, Guid usuario)
        {
            Controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new []
                    {
                        new Claim(ClaimTypes.NameIdentifier, usuario.ToString()),
                        new Claim(ClaimTypes.Role, rol)
                    }, "QA"))
                }
            };
        }

        public void Intento(EstadoIntentoPago estado, bool conTransaccion = false)
        {
            Finance.IntentosPago.Add(new IntentoPago
            {
                Id = Guid.NewGuid(), OrdenId = OrdenId,
                ReferenciaOperacion = $"PAY-{OrdenId:N}", Monto = 50m, Moneda = "USD",
                Estado = estado, TransaccionId = conTransaccion ? "TX-01" : null,
                ReservaId = Guid.NewGuid()
            });
            Finance.SaveChanges();
        }

        public void Reserva(EstadoReserva estado, int cantidadOriginal = 2,
            int liberada = 0, int consumida = 0)
        {
            Catalog.ReservasStock.Add(new ReservaStock
            {
                Id = Guid.NewGuid(), OrdenId = OrdenId, ProductoId = Guid.NewGuid(),
                Estado = estado, CantidadOriginal = cantidadOriginal,
                CantidadLiberada = liberada, CantidadConsumida = consumida
            });
            Catalog.SaveChanges();
        }

        public async Task<SeguimientoCheckoutDto> VerAsync()
        {
            var respuesta = await Controller.Seguimiento(OrdenId);
            return Assert.IsType<SeguimientoCheckoutDto>(Assert.IsType<OkObjectResult>(respuesta).Value);
        }

        public void Dispose() { Finance.Dispose(); Catalog.Dispose(); }
    }

    [Fact]
    public async Task CompradorConsultaSuOrden_SinIntentoNiReserva()
    {
        using var e = new Escenario();
        var resultado = await e.VerAsync();
        Assert.Equal("SinIntento", resultado.EstadoPago);
        Assert.Equal("SinReserva", resultado.EstadoReserva);
        Assert.False(resultado.RequiereRevision);
    }

    [Fact]
    public async Task CompradorAjeno_NoPuedeVerLaOrden()
    {
        using var e = new Escenario();
        e.Identidad("Comprador", Guid.NewGuid());
        Assert.IsType<NotFoundResult>(await e.Controller.Seguimiento(e.OrdenId));
    }

    [Fact]
    public async Task AdministradorPuedeConsultarOrdenAjena()
    {
        using var e = new Escenario();
        e.Identidad("Admin", Guid.NewGuid());
        Assert.Equal(e.OrdenId, (await e.VerAsync()).OrdenId);
    }

    [Fact]
    public async Task CompradorSinGuid_NoAutorizado()
    {
        using var e = new Escenario();
        e.Controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Comprador") }, "QA"));
        Assert.IsType<UnauthorizedResult>(await e.Controller.Seguimiento(e.OrdenId));
    }

    [Fact]
    public async Task OrdenInexistente_NoExponeDatos()
    {
        using var e = new Escenario();
        Assert.IsType<NotFoundResult>(await e.Controller.Seguimiento(Guid.NewGuid()));
    }

    [Fact]
    public async Task PagoIncierto_ConservaReservaYMarcaRevision()
    {
        using var e = new Escenario();
        e.Intento(EstadoIntentoPago.Incierto);
        e.Reserva(EstadoReserva.Pendiente);
        var r = await e.VerAsync();
        Assert.True(r.RequiereRevision);
        Assert.Contains("PAGO_REQUIERE_CONCILIACION", r.Alertas);
        Assert.Equal(2, r.CantidadReservadaPendiente);
    }

    [Fact]
    public async Task OrdenAprobada_ReservaConfirmada_NoImplicaConsumoFisico()
    {
        using var e = new Escenario();
        e.Finance.Ordenes.Single().Estado = EstadoOrden.Aprobada;
        e.Finance.SaveChanges();
        e.Intento(EstadoIntentoPago.Completado, true);
        e.Reserva(EstadoReserva.Confirmada);
        var r = await e.VerAsync();
        Assert.False(r.RequiereRevision);
        Assert.Equal(2, r.CantidadReservadaPendiente);
        Assert.Equal(0, r.CantidadDespachada);
        Assert.Equal(EstadoReserva.Confirmada, e.Catalog.ReservasStock.Single().Estado);
    }

    [Fact]
    public async Task DespachoRegistrado_SeMuestraSinModificarStock()
    {
        using var e = new Escenario();
        e.Finance.Ordenes.Single().Estado = EstadoOrden.Enviada;
        e.Finance.SaveChanges();
        e.Intento(EstadoIntentoPago.Completado, true);
        e.Reserva(EstadoReserva.Consumida, 3, 0, 3);
        var r = await e.VerAsync();
        Assert.Equal(3, r.CantidadDespachada);
        Assert.Equal(0, r.CantidadReservadaPendiente);
        Assert.False(r.RequiereRevision);
    }

    [Fact]
    public async Task CancelacionConReservaPendiente_Alerta()
    {
        using var e = new Escenario();
        e.Finance.Ordenes.Single().Estado = EstadoOrden.Cancelada;
        e.Finance.SaveChanges();
        e.Reserva(EstadoReserva.Pendiente);
        var r = await e.VerAsync();
        Assert.Contains("ORDEN_CANCELADA_CON_RESERVA", r.Alertas);
    }


    [Fact]
    public async Task PagoRechazado_ReservaNoLiberada_Alerta()
    {
        using var e = new Escenario();
        e.Intento(EstadoIntentoPago.Rechazado);
        e.Reserva(EstadoReserva.Pendiente);
        var r = await e.VerAsync();
        Assert.Contains("PAGO_RECHAZADO_CON_RESERVA", r.Alertas);
    }

    [Fact]
    public async Task PagoRechazado_OrdenAprobada_AlertaYColaAdmin()
    {
        using var e = new Escenario();
        e.Finance.Ordenes.Single().Estado = EstadoOrden.Aprobada;
        e.Finance.SaveChanges();
        e.Intento(EstadoIntentoPago.Rechazado);
        e.Reserva(EstadoReserva.Confirmada);
        var r = await e.VerAsync();
        Assert.Contains("ORDEN_ACTIVA_CON_PAGO_RECHAZADO", r.Alertas);
        e.Identidad("Admin", Guid.NewGuid());
        var cola = Assert.IsType<List<SeguimientoCheckoutDto>>(
            Assert.IsType<OkObjectResult>(await e.Controller.PendientesConciliacion()).Value);
        Assert.Single(cola);
    }

    [Fact]
    public async Task ReembolsoPendiente_AlertaSinInvocarProveedor()
    {
        using var e = new Escenario();
        e.Intento(EstadoIntentoPago.Aprobado, true);
        e.Reserva(EstadoReserva.Pendiente);
        e.Finance.ReembolsosPago.Add(new ReembolsoPago
        {
            OrdenId = e.OrdenId, ReservaId = Guid.NewGuid(),
            ClaveIdempotencia = $"REF-{e.OrdenId:N}",
            TransaccionId = "TX-01", Monto = 50m, Moneda = "USD",
            Estado = EstadoReembolsoPago.Pendiente
        });
        e.Finance.SaveChanges();
        var r = await e.VerAsync();
        Assert.Contains("REEMBOLSO_PENDIENTE", r.Alertas);
    }

    [Fact]
    public async Task ColaAdministrador_FiltraYNoModificaRegistros()
    {
        using var e = new Escenario();
        e.Identidad("Admin", Guid.NewGuid());
        e.Intento(EstadoIntentoPago.Enviado);
        e.Reserva(EstadoReserva.Pendiente);
        var r = Assert.IsType<OkObjectResult>(await e.Controller.PendientesConciliacion());
        var pendientes = Assert.IsType<List<SeguimientoCheckoutDto>>(r.Value);
        Assert.Single(pendientes);
        Assert.Equal(e.OrdenId, pendientes[0].OrdenId);
        Assert.Equal(EstadoOrden.Pendiente, e.Finance.Ordenes.Single().Estado);
    }

    [Fact]
    public async Task CompradorNoPuedeVerLaColaAdmin()
    {
        using var e = new Escenario();
        Assert.IsType<ForbidResult>(await e.Controller.PendientesConciliacion());
    }

    [Fact]
    public async Task OrdenCanceladaConCobroSinDevolucion_RequiereRevision()
    {
        using var e = new Escenario();
        e.Finance.Ordenes.Single().Estado = EstadoOrden.Cancelada;
        e.Finance.SaveChanges();
        e.Intento(EstadoIntentoPago.Aprobado, true);
        e.Reserva(EstadoReserva.Liberada, liberada: 2);
        var resultado = await e.VerAsync();
        Assert.Contains("ORDEN_CANCELADA_CON_COBRO_SIN_REEMBOLSO", resultado.Alertas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ColaRechazaLimiteInvalido(int limite)
    {
        using var e = new Escenario();
        e.Identidad("Admin", Guid.NewGuid());
        Assert.IsType<BadRequestObjectResult>(await e.Controller.PendientesConciliacion(limite));
    }

    [Fact]
    public void EndpointProtegido_ConRolesExplicitos()
    {
        var tipo = typeof(CheckoutSeguimientoController);
        var metodo = tipo.GetMethod(nameof(CheckoutSeguimientoController.Seguimiento))!;
        var autor = (AuthorizeAttribute)Attribute.GetCustomAttribute(metodo, typeof(AuthorizeAttribute))!;
        Assert.Equal("Admin,Comprador", autor.Roles);
        var cola = tipo.GetMethod(nameof(CheckoutSeguimientoController.PendientesConciliacion))!;
        var admin = (AuthorizeAttribute)Attribute.GetCustomAttribute(cola, typeof(AuthorizeAttribute))!;
        Assert.Equal("Admin", admin.Roles);
    }
}
