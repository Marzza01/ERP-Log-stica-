/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;
using Microsoft.Extensions.Logging;

namespace MarketplaceERP.Finance.Services;

/// <summary>
/// Mock de la pasarela de pagos para el arranque inicial del proyecto.
/// Simula éxito el 90% de las veces y fallo el 10% restante.
/// REEMPLAZAR por integración real con pasarela (Stripe, PayPal, etc.) cuando esté disponible.
/// 
/// El método ProcesarPagoAsync es virtual para permitir sobreescritura en tests unitarios.
/// </summary>
internal class PagoServiceMock : IPaymentGateway
{
    private readonly ILogger<PagoServiceMock> _logger;

    public PagoServiceMock(ILogger<PagoServiceMock> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Falla aleatoriamente el 10% de las veces usando Random.Shared.NextDouble().
    /// Esto simula errores de red, fondos insuficientes, etc.
    /// </remarks>
    public virtual Task<PagoResult> ProcesarPagoAsync(PagoRequest request)
    {
        // 10% de probabilidad de fallo simulado
        bool debeSimularFallo = Random.Shared.NextDouble() < 0.10;

        if (debeSimularFallo)
        {
            _logger.LogWarning(
                "[MOCK] ProcesarPago FALLIDO (simulado): OrdenId={OrdenId}, Monto={Monto:C} {Moneda}",
                request.OrdenId, request.MontoTotal, request.Moneda);

            return Task.FromResult(new PagoResult(
                Exitoso: false,
                TransaccionId: null,
                MensajeError: "MOCK: Pago rechazado por la pasarela (simulado 10% de fallos)."
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
