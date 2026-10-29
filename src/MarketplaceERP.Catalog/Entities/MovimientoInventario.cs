/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Catalog.Entities;

/// <summary>
/// Tipos de movimiento que forman el ledger operativo de inventario.
/// MovimientoMWIP continúa existiendo de forma independiente para material en proceso.
/// </summary>
public enum TipoMovimientoInventario
{
    RecepcionInbound = 0,
    Reserva = 1,
    LiberacionReserva = 2,
    DespachoOutbound = 3,
    TransferenciaSalida = 4,
    TransferenciaEntrada = 5,
    AjustePositivo = 6,
    AjusteNegativo = 7,
    RmaRestock = 8,
    BloqueoDano = 9,
    Desbloqueo = 10
}

/// <summary>
/// Registro inmutable de una operación que afecta o explica el inventario.
/// Esquema DB: catalog.movimientos_inventario
/// </summary>
public class MovimientoInventario
{
    public Guid Id { get; set; }
    public Guid ProductoId { get; set; }
    public TipoMovimientoInventario TipoMovimiento { get; set; }
    public int Cantidad { get; set; }

    public Guid? UbicacionOrigenId { get; set; }
    public Guid? UbicacionDestinoId { get; set; }

    public Guid? ReservaStockId { get; set; }
    public Guid? OrdenId { get; set; }
    public Guid? RmaId { get; set; }

    public string Referencia { get; set; } = string.Empty;
    public string Observaciones { get; set; } = string.Empty;
    public DateTime FechaMovimiento { get; set; } = DateTime.UtcNow;

    public Producto Producto { get; set; } = null!;
    public UbicacionAlmacen? UbicacionOrigen { get; set; }
    public UbicacionAlmacen? UbicacionDestino { get; set; }
    public ReservaStock? ReservaStock { get; set; }
}
