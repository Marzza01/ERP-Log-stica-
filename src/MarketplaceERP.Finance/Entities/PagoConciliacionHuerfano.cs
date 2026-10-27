/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System;

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Registro de conciliación para pagos que fueron cobrados exitosamente por la pasarela
/// pero cuya saga de checkout falló posteriormente (por ejemplo, fallo en asiento contable,
/// clave de idempotencia o commit de base de datos).
/// Si la pasarela externa no soporta reembolso automático o falla el reembolso,
/// este registro permite a finanzas y auditoría conciliar y reembolsar manualmente.
/// Esquema DB: finance.pagos_conciliacion_huerfanos
/// </summary>
public class PagoConciliacionHuerfano
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrdenId { get; set; }
    public string TransaccionId { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public string Moneda { get; set; } = "USD";
    public string MotivoFallo { get; set; } = string.Empty;
    public bool Reembolsado { get; set; } = false;
    public string? ReembolsoTransaccionId { get; set; }
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    public DateTime? FechaResolucion { get; set; }
}
