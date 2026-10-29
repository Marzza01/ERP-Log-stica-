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
    [Theory]
    [InlineData(typeof(FulfillmentController))]
    [InlineData(typeof(OrdenesController))]
    public void ControladoresOperativos_RequierenAdminOVendedor(Type controlador)
    {
        var autorizacion = controlador.GetCustomAttribute<AuthorizeAttribute>();
        autorizacion.Should().NotBeNull();
        autorizacion!.Roles.Should().Be("Admin,Vendedor");
        controlador.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
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
