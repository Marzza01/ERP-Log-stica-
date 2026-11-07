using System.Text.Json;
using MarketplaceERP.API.Controllers;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// P1: el registro contable simulado nunca se presenta como prueba de
/// transferencia bancaria real en contratos HTTP del módulo Liquidaciones.
/// Los bloqueos de operaciones en staging se comprueban por separado en P0.
/// </summary>
public sealed class P1LiquidacionesSemanticaHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Generar_IdentificaRegistroInternoSinAcreditarTransferencia()
    {
        var vendedorId = Guid.NewGuid();
        var liq = new Liquidacion
        {
            Id = Guid.NewGuid(),
            VendedorId = vendedorId,
            MontoLiquidar = 45m,
            Estado = EstadoLiquidacion.Pendiente,
            FechaCreacion = DateTime.UtcNow
        };
        var service = new Mock<ILiquidacionService>();
        service.Setup(x => x.GenerarLiquidacionAsync(vendedorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(liq);
        var controller = new LiquidacionesController(
            service.Object, NullLogger<LiquidacionesController>.Instance);

        var result = Assert.IsType<CreatedAtActionResult>(
            await controller.Generar(new GenerarLiquidacionRequest(vendedorId), CancellationToken.None));
        var json = JsonSerializer.Serialize(result.Value, JsonOptions);

        Assert.Contains("\"transferenciaBancariaConfirmada\":false", json);
        Assert.Contains("no confirma ningún desembolso bancario", json);
        service.Verify(x => x.GenerarLiquidacionAsync(vendedorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcesarPago_ExponeAsientoSimulado_NoUnDesembolsoVerificado()
    {
        var liq = new Liquidacion
        {
            Id = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            MontoLiquidar = 45m,
            Estado = EstadoLiquidacion.Completada,
            FechaLiquidacion = DateTime.UtcNow
        };
        var service = new Mock<ILiquidacionService>();
        service.Setup(x => x.ProcesarPagoAsync(liq.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(liq);
        var controller = new LiquidacionesController(
            service.Object, NullLogger<LiquidacionesController>.Instance);

        var result = Assert.IsType<OkObjectResult>(
            await controller.ProcesarPago(liq.Id, CancellationToken.None));
        var json = JsonSerializer.Serialize(result.Value, JsonOptions);

        Assert.Contains("\"asientoGenerado\":true", json);
        Assert.Contains("\"registroContableSimulado\":true", json);
        Assert.Contains("\"transferenciaBancariaConfirmada\":false", json);
        Assert.Contains("no acreditan transferencia bancaria", json);
        service.Verify(x => x.ProcesarPagoAsync(liq.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HistorialYPendientes_NoAtribuyenTransferenciasABalancesInternos()
    {
        var liq = new Liquidacion
        {
            Id = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            MontoLiquidar = 90m,
            Estado = EstadoLiquidacion.Pendiente,
            FechaCreacion = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc)
        };
        var service = new Mock<ILiquidacionService>();
        service.Setup(x => x.ObtenerHistorialAsync(
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { liq });
        var controller = new LiquidacionesController(
            service.Object, NullLogger<LiquidacionesController>.Instance);

        var historial = Assert.IsType<OkObjectResult>(
            await controller.Historial(ct: CancellationToken.None));
        var pendientes = Assert.IsType<OkObjectResult>(
            await controller.Pendientes(CancellationToken.None));

        Assert.Contains("\"transferenciaBancariaConfirmada\":false",
            JsonSerializer.Serialize(historial.Value, JsonOptions));
        Assert.Contains("\"transferenciaBancariaConfirmada\":false",
            JsonSerializer.Serialize(pendientes.Value, JsonOptions));
    }
}
