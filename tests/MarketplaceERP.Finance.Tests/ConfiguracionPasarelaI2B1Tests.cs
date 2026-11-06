using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance;
using MarketplaceERP.Finance.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>La pasarela simulada debe estar deshabilitada por defecto y en Production.</summary>
public class ConfiguracionPasarelaI2B1Tests
{
    private static IConfiguration Configurar(string? provider, string? enableMock)
    {
        var valores = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=qa;Username=qa;Password=not_used"
        };
        if (provider is not null) valores["Payments:Provider"] = provider;
        if (enableMock is not null) valores["Payments:EnableMock"] = enableMock;
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    [Fact]
    public void SinProveedor_NoArrancaNiEnDevelopment()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddFinanceServices(Configurar(null, null), isDevelopment: true));
        Assert.Empty(services);
    }

    [Fact]
    public void MockSinHabilitacionExplicita_SeRechaza()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddFinanceServices(Configurar("Mock", null), isDevelopment: true));
    }

    [Fact]
    public void MockDeshabilitado_SeRechaza()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddFinanceServices(Configurar("Mock", "false"), isDevelopment: true));
    }

    [Fact]
    public void Production_ProhibeMockAunqueLaBanderaEsteActiva()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddFinanceServices(Configurar("Mock", "true"), isDevelopment: false));
        Assert.Empty(services);
    }

    [Fact]
    public void ProveedorNoImplementado_SeRechazaSinFingirCobros()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddFinanceServices(Configurar("Stripe", "true"), isDevelopment: true));
    }

    [Fact]
    public void Development_ConOptInExplicito_RegistraSolamenteElMock()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinanceServices(Configurar("Mock", "true"), isDevelopment: true);
        using var sp = services.BuildServiceProvider();
        var pasarela = sp.GetRequiredService<IPaymentGateway>();
        Assert.IsType<PagoServiceMock>(pasarela);
    }

    [Fact]
    public async Task Production_ConProveedorDeshabilitado_RegistraPasarelaQueNuncaConfirmaCobros()
    {
        var services = new ServiceCollection();
        services.AddFinanceServices(Configurar("Disabled", null), isDevelopment: false);
        using var sp = services.BuildServiceProvider();
        Assert.True(sp.GetRequiredService<PaymentOperationsMode>().IsDisabled);
        var gateway = sp.GetRequiredService<IPaymentGateway>();
        Assert.IsType<PagoServiceDisabled>(gateway);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.ProcesarPagoAsync(new MarketplaceERP.Shared.DTOs.PagoRequest(
                Guid.NewGuid(), Guid.NewGuid(), 1m, "USD", "test")));
    }
}
