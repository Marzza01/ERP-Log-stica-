/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System;

namespace MarketplaceERP.Finance.Entities;

/// <summary>
/// Registro de llaves de idempotencia para transacciones críticas (Checkout, etc.).
/// Esquema DB: finance.idempotency_keys
/// </summary>
public class IdempotencyKeyRecord
{
    public Guid Id { get; set; }

    /// <summary>Clave provista por el cliente en el encabezado Idempotency-Key</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Identificador del usuario que envió la solicitud</summary>
    public Guid UsuarioId { get; set; }

    /// <summary>Hash SHA-256 del cuerpo de la solicitud normalizado</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Código de respuesta HTTP devuelto (ej. 200)</summary>
    public int StatusCode { get; set; }

    /// <summary>Cuerpo JSON de la respuesta para replicación exacta</summary>
    public string ResponseJson { get; set; } = string.Empty;

    /// <summary>Fecha y hora de creación de la llave</summary>
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    /// <summary>Fecha y hora de expiración (24 horas después de la creación)</summary>
    public DateTime FechaExpiracion { get; set; } = DateTime.UtcNow.AddHours(24);
}
