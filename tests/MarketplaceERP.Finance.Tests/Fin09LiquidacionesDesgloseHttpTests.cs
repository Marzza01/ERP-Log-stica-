using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.API.Security;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// FIN09: detalle 1:N real del contrato HTTP y restricciones de acceso.
/// Todos los importes/IDs son sinteticos; no se llama a pagos reales.
/// </summary>
public sealed class Fin09LiquidacionesDesgloseHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static Liquidacion LoteConTresSplits(Guid vendedorId)
    {
        var liq = new Liquidacion
        {
            Id = Guid.NewGuid(), VendedorId = vendedorId,
            MontoLiquidar = 315m, Moneda = "USD",
            Estado = EstadoLiquidacion.Pendiente,
            FechaCreacion = new DateTime(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc)
        };
        foreach (var amount in new[] { 90m, 180m, 45m })
        {
            var order = new Orden
            {
                Id = Guid.NewGuid(), VendedorId = vendedorId, Moneda = "USD"
            };
            liq.SplitsPago.Add(new SplitPago
            {
                Id = Guid.NewGuid(), OrdenId = order.Id, Orden = order,
                LiquidacionId = liq.Id, MontoTotal = amount / 0.9m,
                MontoVendedor = amount, MontoMarketplace = amount / 9m
            });
        }
        liq.SplitPagoId = liq.SplitsPago[0].Id;
        return liq;
    }

    private static Mock<ILiquidacionService> MockHistorial(Liquidacion liq)
    {
        var service = new Mock<ILiquidacionService>();
        service.Setup(x => x.ObtenerHistorialAsync(
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { liq });
        return service;
    }

    [Fact]
    public async Task HistorialYPendientes_DevuelvenTresSplitsConMontosRealesYFlagVerificado()
    {
        var vendor = Guid.NewGuid();
        var liq = LoteConTresSplits(vendor);
        var controller = new LiquidacionesController(
            MockHistorial(liq).Object, NullLogger<LiquidacionesController>.Instance);

        foreach (var result in new IActionResult[]
        {
            await controller.Historial(vendor),
            await controller.Pendientes(CancellationToken.None)
        })
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
                Assert.IsType<OkObjectResult>(result).Value, JsonOptions));
            var row = Assert.Single(doc.RootElement.EnumerateArray().ToList());
            Assert.True(row.GetProperty("desgloseVerificado").GetBoolean());
            Assert.Equal(3, row.GetProperty("cantidadSplits").GetInt32());
            Assert.Equal(350m, row.GetProperty("montoVentas").GetDecimal());
            Assert.Equal(35m, row.GetProperty("comisionMarketplace").GetDecimal());
            Assert.Equal(315m, row.GetProperty("montoLiquidar").GetDecimal());
            Assert.False(row.GetProperty("transferenciaBancariaConfirmada").GetBoolean());
            var splits = row.GetProperty("splits").EnumerateArray().ToList();
            Assert.Equal(3, splits.Count);
            Assert.Equal(315m, splits.Sum(s => s.GetProperty("montoVendedor").GetDecimal()));
            Assert.Equal(35m, splits.Sum(s => s.GetProperty("comisionMarketplace").GetDecimal()));
        }
    }

    [Fact]
    public async Task HistoricoSinBackfill_SeMuestraComoNoVerificadoSinInventarSplits()
    {
        var liq = new Liquidacion
        {
            Id = Guid.NewGuid(), VendedorId = Guid.NewGuid(),
            SplitPagoId = Guid.NewGuid(),
            MontoLiquidar = 90m, Moneda = "USD",
            Estado = EstadoLiquidacion.Pendiente,
            FechaCreacion = DateTime.UtcNow
        };
        var controller = new LiquidacionesController(
            MockHistorial(liq).Object, NullLogger<LiquidacionesController>.Instance);
        var response = Assert.IsType<OkObjectResult>(
            await controller.Historial(ct: CancellationToken.None));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response.Value, JsonOptions));
        var row = Assert.Single(doc.RootElement.EnumerateArray().ToList());
        Assert.False(row.GetProperty("desgloseVerificado").GetBoolean());
        Assert.Equal(0, row.GetProperty("cantidadSplits").GetInt32());
        Assert.Empty(row.GetProperty("splits").EnumerateArray().ToList());
        Assert.False(row.GetProperty("transferenciaBancariaConfirmada").GetBoolean());
        Assert.Equal(90m, row.GetProperty("montoLiquidar").GetDecimal());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DesgloseInconsistente_SePresentaComoNoVerificado(bool alteroImporte)
    {
        var liq = LoteConTresSplits(Guid.NewGuid());
        if (alteroImporte)
            liq.SplitsPago[0].MontoVendedor += 1m;
        else
            liq.SplitsPago[0].Orden.VendedorId = Guid.NewGuid();

        var controller = new LiquidacionesController(
            MockHistorial(liq).Object, NullLogger<LiquidacionesController>.Instance);
        var response = Assert.IsType<OkObjectResult>(
            await controller.Historial(ct: CancellationToken.None));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(response.Value, JsonOptions));
        var row = Assert.Single(doc.RootElement.EnumerateArray().ToList());
        Assert.False(row.GetProperty("desgloseVerificado").GetBoolean());
        Assert.Equal(3, row.GetProperty("cantidadSplits").GetInt32());
        Assert.Equal(3, row.GetProperty("splits").GetArrayLength());
        Assert.False(row.GetProperty("transferenciaBancariaConfirmada").GetBoolean());
    }

    [Fact]
    public async Task HttpJwt_ListasLiquidaciones_RechazanAnonimoYVendedor_PermitenAdmin()
    {
        const string secret = "FIN09-SYNTHETIC-TEST-JWT-KEY-ONLY-NOT-FOR-PRODUCTION";
        const string issuer = "fin09-qa";
        var vendor = Guid.NewGuid();
        var liq = LoteConTresSplits(vendor);
        var service = MockHistorial(liq);
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(service.Object);
                services.AddMarketplaceSecurity(new JwtSettings
                {
                    Key = secret, Issuer = issuer, Audience = issuer,
                    ExpirationMinutes = 5
                });
                services.AddControllers().AddApplicationPart(typeof(LiquidacionesController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

        static string Token(string role, string key, string issuerName)
        {
            var jwt = new JwtSecurityToken(issuer: issuerName, audience: issuerName,
                claims: new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, role)
                },
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        foreach (var endpoint in new[] { "/api/liquidaciones/historial", "/api/liquidaciones/pendientes" })
        {
            using var anonymous = server.CreateClient();
            using var anonResult = await anonymous.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.Unauthorized, anonResult.StatusCode);

            using var seller = server.CreateClient();
            seller.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Token("Vendedor", secret, issuer));
            using var sellerResult = await seller.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.Forbidden, sellerResult.StatusCode);

            using var admin = server.CreateClient();
            admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Token("Admin", secret, issuer));
            using var adminResult = await admin.GetAsync(endpoint);
            Assert.Equal(HttpStatusCode.OK, adminResult.StatusCode);
            var body = await adminResult.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var row = Assert.Single(doc.RootElement.EnumerateArray().ToList());
            Assert.Equal(3, row.GetProperty("cantidadSplits").GetInt32());
            Assert.True(row.GetProperty("desgloseVerificado").GetBoolean());
            Assert.False(row.GetProperty("transferenciaBancariaConfirmada").GetBoolean());
        }
    }
}
