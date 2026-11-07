using System.Reflection;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// P0: impedir regresiones que expongan endpoints de contabilidad, match o pagos
/// sin autorización ni el filtro que bloquea efectos financieros en staging.
/// </summary>
public class P0GuardiasFinancierasTests
{
    [Fact]
    public void ControllersFinancieros_RequierenAdmin()
    {
        foreach (var controller in new[]
        {
            typeof(ComprasController),
            typeof(ContabilidadController),
            typeof(LiquidacionesController)
        })
        {
            var auth = controller.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(auth);
            Assert.Equal("Admin", auth!.Roles);
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        }
        var listaOrdenes = typeof(OrdenesController).GetMethod("Listar")!;
        Assert.Equal("Admin", listaOrdenes.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
    }

    [Fact]
    public void OperacionesConEfectosFinancieros_TienenPaymentOperationsGuard()
    {
        var protegidas = new[]
        {
            (typeof(ComprasController), "EjecutarMatch"),
            (typeof(ContabilidadController), "AsientoManual"),
            (typeof(LiquidacionesController), "Generar"),
            (typeof(LiquidacionesController), "ProcesarPago")
        };
        foreach (var (controller, name) in protegidas)
        {
            var method = controller.GetMethod(name);
            Assert.NotNull(method);
            Assert.NotNull(method!.GetCustomAttribute<PaymentOperationsGuardAttribute>());
        }
    }
}
