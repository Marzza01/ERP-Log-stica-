using MarketplaceERP.Shared.DTOs;

namespace MarketplaceERP.Core.Interfaces;

/// <summary>
/// Operaciones para una futura pasarela REAL con consulta y reembolso.
/// No está registrada en DI en I2B2B y jamás debe invocar PagoServiceMock.
/// La implementación deberá garantizar idempotencia de cada reembolso.
/// </summary>
public interface IPaymentRecoveryGateway
{
    Task<ConsultaPagoResult> ConsultarEstadoAsync(
        string referenciaOperacion, CancellationToken cancellationToken = default);

    /// <summary>Consultar reembolso usando la misma clave, sin iniciarlo otra vez.</summary>
    Task<ReembolsoPagoResult> ConsultarReembolsoAsync(
        string claveIdempotencia, CancellationToken cancellationToken = default);

    Task<ReembolsoPagoResult> ReembolsarAsync(
        ReembolsoPagoRequest request, CancellationToken cancellationToken = default);
}
