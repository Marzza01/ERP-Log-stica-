namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Línea o movimiento individual (Debe o Haber) de una partida contable.
/// Esquema DB: finance.lineas_partida_contable
/// </summary>
public class LineaPartidaContable
{
    public Guid Id { get; set; }
    public Guid PartidaContableId { get; set; }

    /// <summary>
    /// Código contable estándar:
    /// 1101 - Efectivo y Equivalentes de Efectivo (Bancos/Caja)
    /// 1105 - Cuentas por Cobrar Comerciales (Clientes B2B)
    /// 1108 - Inventarios de Mercaderías
    /// 1110 - IVA Crédito Fiscal (13% en compras)
    /// 2101 - Cuentas por Pagar Comerciales (Proveedores)
    /// 2105 - IVA Débito Fiscal (13% en ventas)
    /// 2108 - Pasivos por Liquidar a Vendedores Marketplace
    /// 4101 - Ingresos por Ventas / Comisiones del Marketplace
    /// 5101 - Costo de Ventas
    /// </summary>
    public string CodigoCuenta { get; set; } = string.Empty;
    public string NombreCuenta { get; set; } = string.Empty;

    public decimal Debe { get; set; } = 0m;
    public decimal Haber { get; set; } = 0m;

    public string Glosa { get; set; } = string.Empty;

    // Navegación
    public PartidaContable PartidaContable { get; set; } = null!;
}
