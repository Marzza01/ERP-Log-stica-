/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests de API para ContabilidadController (POST /api/contabilidad/asiento-manual y GET /api/contabilidad/libro-diario):
/// Requiere token Admin:
///  - Descuadrada -> 400 Bad Request
///  - Una sola línea -> 400 Bad Request
///  - Cuenta inexistente -> 400 Bad Request
///  - Línea con Debe y Haber a la vez -> 400 Bad Request
///  - Válido de 3 líneas -> 201 Created y reflejado en GET libro-diario
/// </summary>
public class ContabilidadHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ContabilidadHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AsientoManual_Descuadrado_Retorna400BadRequest()
    {
        var client = _factory.CreateClientWithAuth("Admin");

        var request = new
        {
            concepto = "Ajuste Descuadrado",
            tipoOperacion = "Ajuste",
            lineas = new[]
            {
                new { codigoCuenta = "1101", debe = 100m, haber = 0m, glosa = "Caja" },
                new { codigoCuenta = "4101", debe = 0m, haber = 80m, glosa = "Ingresos" }
            }
        };

        var response = await client.PostAsJsonAsync("/api/contabilidad/asiento-manual", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no cuadra", body);
    }

    [Fact]
    public async Task AsientoManual_UnaSolaLinea_Retorna400BadRequest()
    {
        var client = _factory.CreateClientWithAuth("Admin");

        var request = new
        {
            concepto = "Ajuste Una Sola Línea",
            tipoOperacion = "Ajuste",
            lineas = new[]
            {
                new { codigoCuenta = "1101", debe = 100m, haber = 0m, glosa = "Caja" }
            }
        };

        var response = await client.PostAsJsonAsync("/api/contabilidad/asiento-manual", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("mínimo de 2 líneas", body);
    }

    [Fact]
    public async Task AsientoManual_CuentaInexistente_Retorna400BadRequest()
    {
        var client = _factory.CreateClientWithAuth("Admin");

        var request = new
        {
            concepto = "Ajuste Cuenta Inexistente",
            tipoOperacion = "Ajuste",
            lineas = new[]
            {
                new { codigoCuenta = "9999", debe = 50m, haber = 0m, glosa = "Cuenta Falsa" },
                new { codigoCuenta = "1101", debe = 0m, haber = 50m, glosa = "Caja" }
            }
        };

        var response = await client.PostAsJsonAsync("/api/contabilidad/asiento-manual", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no existe o no está activa", body);
    }

    [Fact]
    public async Task AsientoManual_DebeYHaberSimultaneosEnMismaLinea_Retorna400BadRequest()
    {
        var client = _factory.CreateClientWithAuth("Admin");

        var request = new
        {
            concepto = "Ajuste Ambos Positivos",
            tipoOperacion = "Ajuste",
            lineas = new[]
            {
                new { codigoCuenta = "1101", debe = 50m, haber = 10m, glosa = "Caja dual" },
                new { codigoCuenta = "4101", debe = 0m, haber = 40m, glosa = "Ingresos" }
            }
        };

        var response = await client.PostAsJsonAsync("/api/contabilidad/asiento-manual", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no se permite registrar montos positivos en Debe y Haber simultáneamente", body);
    }

    [Fact]
    public async Task AsientoManual_Valido3Lineas_Retorna201Created_YSeReflejaEnLibroDiario()
    {
        var client = _factory.CreateClientWithAuth("Admin");

        var conceptoUnico = $"Ajuste API 3 Líneas {Guid.NewGuid():N}";
        var request = new
        {
            concepto = conceptoUnico,
            tipoOperacion = "Ajuste",
            lineas = new[]
            {
                new { codigoCuenta = "1101", debe = 100m, haber = 0m, glosa = "Caja" },
                new { codigoCuenta = "4101", debe = 0m, haber = 87m, glosa = "Ingresos Netos" },
                new { codigoCuenta = "2105", debe = 0m, haber = 13m, glosa = "IVA Débito Fiscal" }
            }
        };

        var response = await client.PostAsJsonAsync("/api/contabilidad/asiento-manual", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        // Verificar que GET /api/contabilidad/libro-diario lo refleja
        var responseDiario = await client.GetAsync("/api/contabilidad/libro-diario?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, responseDiario.StatusCode);

        var diarioBody = await responseDiario.Content.ReadAsStringAsync();
        Assert.Contains(conceptoUnico, diarioBody);
    }
}
