using System;
using System.Threading.Tasks;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// Mock de la pasarela de pagos para el desarrollo y pruebas.
/// En Production, si no hay pasarela real configurada, rechaza con InvalidOperationException (503).
/// En Development/Test, simula fallo solo si Payments:SimulateFailures es true (por defecto false).
/// </summary>
public class PagoServiceMock : IPaymentGateway
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PagoServiceMock> _logger;

    public PagoServiceMock(
        IConfiguration configuration,
        ILogger<PagoServiceMock> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public virtual Task<PagoResult> ProcesarPagoAsync(PagoRequest request)
    {
        var envName = _configuration["ASPNETCORE_ENVIRONMENT"] 
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") 
            ?? "Development";

        bool esProduccion = string.Equals(envName, "Production", StringComparison.OrdinalIgnoreCase);

        // En Producción, si no hay pasarela real configurada, no se debe procesar cobros ficticios con el mock
        if (esProduccion)
        {
            _logger.LogCritical("Intento de cobro con PagoServiceMock en entorno Production para OrdenId={OrdenId}. Abortando con 503.", request.OrdenId);
            throw new InvalidOperationException("La pasarela de pagos no está configurada en entorno de producción. Servicio no disponible temporalmente.");
        }

        // El fallo aleatorio solo opera si Payments:SimulateFailures está explícitamente activado en Development/Test
        bool simularFallos = bool.TryParse(_configuration["Payments:SimulateFailures"], out var sim) && sim;

        if (simularFallos && Random.Shared.NextDouble() < 0.10)
        {
            _logger.LogWarning(
                "[MOCK] ProcesarPago FALLIDO (simulado por Payments:SimulateFailures): OrdenId={OrdenId}, Monto={Monto:C} {Moneda}",
                request.OrdenId, request.MontoTotal, request.Moneda);

            return Task.FromResult(new PagoResult(
                Exitoso: false,
                TransaccionId: null,
                MensajeError: "MOCK: Pago rechazado por la pasarela (simulado por configuración)."
            ));
        }

        var transaccionId = $"TXN-MOCK-{Guid.NewGuid():N}".ToUpperInvariant();

        _logger.LogInformation(
            "[MOCK] ProcesarPago EXITOSO: OrdenId={OrdenId}, Monto={Monto:C} {Moneda}, TransaccionId={TransaccionId}",
            request.OrdenId, request.MontoTotal, request.Moneda, transaccionId);

        return Task.FromResult(new PagoResult(
            Exitoso: true,
            TransaccionId: transaccionId,
            MensajeError: null
        ));
    }
}
