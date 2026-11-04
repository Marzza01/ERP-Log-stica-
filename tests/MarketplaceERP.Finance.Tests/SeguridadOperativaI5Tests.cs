using System.Reflection;
using FluentAssertions;
using MarketplaceERP.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// I5: API publica por defecto solo endpoints intencionalmente anonimos.
/// Las operaciones OMS/Fulfillment no deben quedar accesibles a Comprador.
/// El QA HTTP comprueba la aplicacion real de estas reglas con JWT.
/// </summary>
public sealed class SeguridadOperativaI5Tests
{
    [Fact]
    public void Fulfillment_ExigeAdminEnTodasSusAcciones()
    {
        var controlador = typeof(FulfillmentController);
        controlador.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Admin");
        controlador.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        controlador.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<HttpMethodAttribute>() is not null)
            .Should().OnlyContain(m => m.GetCustomAttribute<AllowAnonymousAttribute>() == null);
    }

    [Fact]
    public void Ordenes_ExigeAdminSalvoDetallePropioDelComprador()
    {
        var controlador = typeof(OrdenesController);
        controlador.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        controlador.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        var acciones = controlador.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<HttpMethodAttribute>() is not null).ToArray();
        acciones.Select(m => m.Name).Should().BeEquivalentTo(new[]
        {
            nameof(OrdenesController.Listar), nameof(OrdenesController.Obtener),
            nameof(OrdenesController.Crear), nameof(OrdenesController.TransicionarEstado),
            nameof(OrdenesController.ObtenerKpis)
        });
        foreach (var accion in acciones)
        {
            accion.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
            accion.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(
                accion.Name == nameof(OrdenesController.Obtener) ? "Admin,Comprador" : "Admin");
        }
    }

    [Theory]
    [InlineData(typeof(FulfillmentController))]
    [InlineData(typeof(OrdenesController))]
    public void MetodosDeControladoresOperativos_NoPermitenAnonimos(Type controlador)
    {
        var metodos = controlador.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<HttpMethodAttribute>() is not null).ToArray();
        metodos.Should().NotBeEmpty();
        metodos.Should().OnlyContain(m => m.GetCustomAttribute<AllowAnonymousAttribute>() == null);
    }

    [Fact]
    public void FlujoDelComprador_ContinuaSeparadoDelOmsAdministrativo()
    {
        typeof(CheckoutController).GetMethod("ProcesarCheckout")!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be("Comprador");
        typeof(CheckoutSeguimientoController).GetMethod("Seguimiento")!
            .GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Contain("Comprador");
    }
}
