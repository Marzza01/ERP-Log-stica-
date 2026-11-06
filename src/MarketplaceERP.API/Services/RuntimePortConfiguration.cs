namespace MarketplaceERP.API.Services;

public static class RuntimePortConfiguration
{
    public static string? ResolveUrls(string? port)
    {
        if (port is null) return null;
        if (!int.TryParse(port, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) ||
            number is < 1 or > 65535)
            throw new InvalidOperationException("PORT debe ser un número entre 1 y 65535.");
        return $"http://0.0.0.0:{number}";
    }
}
