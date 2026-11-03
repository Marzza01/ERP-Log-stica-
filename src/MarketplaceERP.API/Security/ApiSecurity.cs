using System.Security.Claims;
using System.Text;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace MarketplaceERP.API.Security;

public static class ApiSecurity
{
    // Shared by production registration and isolated middleware tests; no host or database startup.
    public static IServiceCollection AddMarketplaceSecurity(this IServiceCollection services, JwtSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Key) || settings.Key.Length < 32 ||
            settings.Key.Contains("CAMBIA", StringComparison.OrdinalIgnoreCase) ||
            settings.Key.Contains("DEV_ONLY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Jwt:Key requiere una clave externa privada de al menos 32 caracteres, sin placeholders.");
        if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience))
            throw new InvalidOperationException("Jwt:Issuer y Jwt:Audience son obligatorios.");
        if (settings.ExpirationMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Jwt:ExpirationMinutes debe estar entre 1 y 1440 minutos.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                ValidIssuer = settings.Issuer,
                ValidAudience = settings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                ClockSkew = TimeSpan.Zero
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var identities = context.Principal?.FindAll(ClaimTypes.NameIdentifier)
                        .Select(c => c.Value).ToArray() ?? Array.Empty<string>();
                    if (identities.Length == 0 || identities.Any(value =>
                            !Guid.TryParse(value, out var id) || id == Guid.Empty) ||
                        identities.Select(Guid.Parse).Distinct().Count() != 1)
                        context.Fail("Identidad de usuario inválida o ambigua.");
                    return Task.CompletedTask;
                }
            };
        });
        services.AddAuthorization(options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }
}
