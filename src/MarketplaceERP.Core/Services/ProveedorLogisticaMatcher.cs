/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MarketplaceERP.Core.Entities;

namespace MarketplaceERP.Core.Services;

/// <summary>
/// Vincula nombres libres de proveedor (por ejemplo, los que vienen del catálogo CSV)
/// con el maestro único core.proveedores sin crear registros paralelos.
/// </summary>
public static class ProveedorLogisticaMatcher
{
    private static readonly string[] SufijosLegales =
    [
        "SOCIEDAD ANONIMA DE CAPITAL VARIABLE",
        "SOCIEDAD ANONIMA",
        "SA DE CV",
        "S A DE C V",
        "S A",
        "SA",
        "SRL",
        "S R L",
        "LTDA",
        "LIMITADA",
        "INC",
        "LLC"
    ];

    public static string CrearClave(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return string.Empty;

        var normalized = nombre.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }

        var value = builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        value = Regex.Replace(value, "[^A-Z0-9]+", " ").Trim();
        value = Regex.Replace(value, "\\s+", " ");

        var changed = true;
        while (changed && value.Length > 0)
        {
            changed = false;
            foreach (var suffix in SufijosLegales.OrderByDescending(x => x.Length))
            {
                if (value.Equals(suffix, StringComparison.Ordinal))
                {
                    value = string.Empty;
                    changed = true;
                    break;
                }

                if (value.EndsWith($" {suffix}", StringComparison.Ordinal))
                {
                    value = value[..^(suffix.Length + 1)].Trim();
                    changed = true;
                    break;
                }
            }
        }

        return value;
    }

    public static bool Coincide(Proveedor proveedor, string? nombreCatalogo)
    {
        if (!proveedor.Activo) return false;

        var clave = CrearClave(nombreCatalogo);
        if (string.IsNullOrEmpty(clave)) return false;

        return clave == CrearClave(proveedor.NombreComercial) ||
               clave == CrearClave(proveedor.RazonSocial);
    }

    public static Proveedor? Resolver(IEnumerable<Proveedor> proveedores, string? nombreCatalogo) =>
        proveedores.FirstOrDefault(p => Coincide(p, nombreCatalogo));
}
