/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests para los endpoints de Health Check:
/// - GET /health (liveness básico)
/// - GET /health/ready (readiness de bases de datos)
/// </summary>
public class HealthCheckHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthCheckHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Liveness_Retorna200Ok_Anonimo()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }

    [Fact]
    public async Task Health_Ready_ConBasesDeDatosAlcanzables_Retorna200Ok_Anonimo()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ready", body);
    }

    [Fact]
    public async Task Health_Ready_SinBaseDeDatos_Retorna503ServiceUnavailable_SinDetallesInternos()
    {
        // Crear un WebApplicationFactory donde los DbContexts fallen al conectar
        var brokenFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Reemplazamos CoreDbContext con una conexión inválida
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<MarketplaceERP.Core.Data.CoreDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<MarketplaceERP.Core.Data.CoreDbContext>(o =>
                    o.UseNpgsql("Host=127.0.0.1;Port=1;Database=inexistente;Timeout=1;Command Timeout=1"));
            });
        });

        var client = brokenFactory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Verificar que no expone detalles internos sensibles ni excepciones de base de datos
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("Connection refused", body);
        Assert.DoesNotContain("Exception", body);
    }
}
