namespace MarketplaceERP.Shared.Exceptions;

/// <summary>
/// Se lanza cuando no hay stock suficiente para completar una reserva.
/// Esta excepción no requiere rollback porque no se había realizado ninguna operación previa.
/// </summary>
public sealed class StockInsuficienteException : Exception
{
    public Guid ProductoId { get; }
    public int CantidadSolicitada { get; }
    public int StockDisponible { get; }

    public StockInsuficienteException(Guid productoId, int cantidadSolicitada, int stockDisponible)
        : base($"Stock insuficiente para el producto {productoId}. Solicitado: {cantidadSolicitada}, Disponible: {stockDisponible}.")
    {
        ProductoId = productoId;
        CantidadSolicitada = cantidadSolicitada;
        StockDisponible = stockDisponible;
    }

    public StockInsuficienteException(string message) : base(message) { }

    public StockInsuficienteException(string message, Exception innerException)
        : base(message, innerException) { }
}
