namespace MarketplaceERP.API.Security;

/// <summary>
/// Nombres de roles en forma canónica. ClaimsPrincipal.IsInRole y las
/// políticas de [Authorize(Roles = "...")] utilizan los valores exactos.
/// </summary>
public static class UsuarioRoles
{
    public static string? Canonizar(string? rol)
    {
        if (string.IsNullOrWhiteSpace(rol)) return null;

        return rol.Trim().ToLowerInvariant() switch
        {
            "admin" => "Admin",
            "vendedor" => "Vendedor",
            "comprador" => "Comprador",
            _ => null
        };
    }
}
