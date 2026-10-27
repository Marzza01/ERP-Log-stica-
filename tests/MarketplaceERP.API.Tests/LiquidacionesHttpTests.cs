/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Net;
using System.Net.Http.Json;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests para LiquidacionesController:
/// - Generar sin saldos pendientes -> 409 Conflict con mensaje claro
/// - Procesar pago no encontrada -> 404 NotFound
/// - Procesar pago ya procesada -> 409 Conflict
/// </summary>
public class LiquidacionesHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LiquidacionesHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GenerarLiquidacion_SinSaldosPendientes_Retorna409ConflictConMensajeClaro()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var vendedorSinSplits = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/liquidaciones/generar", new
        {
            vendedorId = vendedorSinSplits
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no tiene saldos pendientes", body);
    }

    [Fact]
    public async Task ProcesarPago_LiquidacionNoEncontrada_Retorna404NotFound()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var idInexistente = Guid.NewGuid();

        var response = await client.PostAsync($"/api/liquidaciones/procesar-pago/{idInexistente}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("no encontrada", body);
    }

    [Fact]
    public async Task ProcesarPago_LiquidacionYaProcesada_Retorna409Conflict()
    {
        var client = _factory.CreateClientWithAuth("Admin");
        var liqId = Guid.NewGuid();
        var vendedorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var liq = new Liquidacion
            {
                Id = liqId,
                VendedorId = vendedorId,
                MontoLiquidar = 150m,
                Moneda = "USD",
                Estado = EstadoLiquidacion.Completada,
                FechaCreacion = DateTime.UtcNow,
                FechaLiquidacion = DateTime.UtcNow
            };
            db.Liquidaciones.Add(liq);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/liquidaciones/procesar-pago/{liqId}", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ya está en estado", body);
    }

    [Fact]
    public async Task ProcesarPago_DbUpdateExceptionClaveUnica_Retorna409Conflict()
    {
        // Test directo al Controller con stub de ILiquidacionService para verificar el mapeo de DbUpdateException -> 409 Conflict
        var fakeService = new FakeLiquidacionServiceDbUpdateException();
        var liqId = Guid.NewGuid();

        var controller = new MarketplaceERP.API.Controllers.LiquidacionesController(
            fakeService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketplaceERP.API.Controllers.LiquidacionesController>.Instance);

        var result = await controller.ProcesarPago(liqId, CancellationToken.None);

        var conflictResult = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(result);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict, conflictResult.StatusCode);
    }

    private class FakeLiquidacionServiceDbUpdateException : MarketplaceERP.Finance.Services.ILiquidacionService
    {
        public Task<Liquidacion> GenerarLiquidacionAsync(Guid vendedorId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Liquidacion> ProcesarPagoAsync(Guid liquidacionId, CancellationToken cancellationToken = default)
            => throw new Microsoft.EntityFrameworkCore.DbUpdateException(
                "Duplicate key 23505",
                new Exception("duplicate key value violates unique constraint"));

        public Task<IEnumerable<Liquidacion>> ObtenerHistorialAsync(Guid? vendedorId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<MarketplaceERP.Finance.Services.KpiVendedor>> ObtenerKpisVendedoresAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
