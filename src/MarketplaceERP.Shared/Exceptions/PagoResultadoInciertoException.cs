namespace MarketplaceERP.Shared.Exceptions;

/// <summary>
/// El resultado del cobro es desconocido o un paso posterior al cobro falló.
/// No se puede afirmar rechazo ni liberar la reserva sin conciliar primero.
/// </summary>
public sealed class PagoResultadoInciertoException : Exception
{
    public Guid OrdenId { get; }

    public PagoResultadoInciertoException(
        Guid ordenId,
        string mensaje,
        Exception? innerException = null)
        : base(mensaje, innerException)
    {
        OrdenId = ordenId;
    }
}
