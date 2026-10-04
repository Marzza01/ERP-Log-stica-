/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

namespace MarketplaceERP.Shared.Settings;

/// <summary>
/// Configuración JWT leída desde appsettings.json usando el patrón Options (IOptions&lt;JwtSettings&gt;).
/// Nunca hardcodees la Key directamente en el código fuente.
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// Sección de configuración en appsettings.json.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Clave secreta para firmar los tokens JWT. Mínimo 32 caracteres.
    /// Leer siempre desde configuración, nunca desde código fuente.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Emisor (issuer) del token JWT.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Audiencia (audience) del token JWT.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Tiempo de expiración del token en minutos (por defecto 60).
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;
}
