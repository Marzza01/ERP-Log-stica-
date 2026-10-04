/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Shared.Exceptions;

/// <summary>
/// Se lanza cuando el procesamiento de un pago falla en la pasarela.
/// Al lanzarse, el stock ya fue liberado previamente (compensación de la Saga).
/// NOTA: La reversión del pago NO está implementada en esta fase; solo se registra el error.
/// </summary>
public sealed class PagoFallidoException : Exception
{
    public Guid OrdenId { get; }
    public string? MensajeError { get; }

    public PagoFallidoException(Guid ordenId, string? mensajeError)
        : base($"El pago para la orden {ordenId} falló: {mensajeError ?? "Error desconocido"}.")
    {
        OrdenId = ordenId;
        MensajeError = mensajeError;
    }

    public PagoFallidoException(string message) : base(message) { }

    public PagoFallidoException(string message, Exception innerException)
        : base(message, innerException) { }
}
