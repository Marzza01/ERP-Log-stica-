using MarketplaceERP.Shared.DTOs;

namespace MarketplaceERP.Core.Interfaces;

/// <summary>
/// Contrato para la integración con una pasarela de pagos externa.
/// Implementado por el Equipo 3 (Finance) en PagoServiceMock / PagoService.
/// Consumido internamente por el CheckoutOrchestrator.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Procesa un pago a través de la pasarela externa configurada.
    /// </summary>
    /// <param name="request">Datos del pago a procesar.</param>
    /// <returns>Resultado del procesamiento con el ID de transacción si fue exitoso.</returns>
    Task<PagoResult> ProcesarPagoAsync(PagoRequest request);
}
