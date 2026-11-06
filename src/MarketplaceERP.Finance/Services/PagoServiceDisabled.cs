using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Shared.DTOs;

namespace MarketplaceERP.Finance.Services;

/// <summary>Defensa adicional: nunca devuelve un cobro simulado.</summary>
public sealed class PagoServiceDisabled : IPaymentGateway
{
    public Task<PagoResult> ProcesarPagoAsync(PagoRequest request) =>
        throw new InvalidOperationException("La pasarela de pagos está deshabilitada.");
}

public sealed record PaymentOperationsMode(bool IsDisabled);
