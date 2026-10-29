namespace MarketplaceERP.Shared.DTOs;

public enum EstadoCobroProveedor { Confirmado, Rechazado, Pendiente, Desconocido }

/// <summary>Estado consultado sin iniciar un segundo cargo.</summary>
public record ConsultaPagoResult(
    EstadoCobroProveedor Estado,
    string? TransaccionId);

public enum EstadoReembolsoProveedor { Completado, Pendiente, Rechazado, Desconocido }

/// <summary>Referencia idempotente obligatoria y estable para reembolso.</summary>
public record ReembolsoPagoRequest(
    Guid OrdenId,
    string TransaccionId,
    string ClaveIdempotencia,
    decimal Monto,
    string Moneda);

public record ReembolsoPagoResult(
    EstadoReembolsoProveedor Estado,
    string? ReembolsoId);
