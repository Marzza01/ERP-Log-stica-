/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// WebApplicationFactory para tests de integración.
/// Sobrescribe la configuración de Jwt mediante UseSetting antes de que el host arranque,
/// asegurando que Program.cs lea directamente estos valores para su AddJwtBearer.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey      = "CLAVE_DE_TEST_SOLO_PARA_INTEGRACION_MINIMO_32_CHARS_XUNIT!!";
    public const string TestJwtIssuer   = "MarketplaceERP.Tests";
    public const string TestJwtAudience = "MarketplaceERP.Tests.Clients";
    public const int    TestJwtExpMin   = 60;

    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting inyecta la configuración en WebHostBuilder ANTES de ejecutar Program.cs
        builder.UseSetting("Jwt:Key", TestJwtKey);
        builder.UseSetting("Jwt:Issuer", TestJwtIssuer);
        builder.UseSetting("Jwt:Audience", TestJwtAudience);
        builder.UseSetting("Jwt:ExpirationMinutes", TestJwtExpMin.ToString());

        builder.ConfigureServices(services =>
        {
            RemoveDescriptor<DbContextOptions<CoreDbContext>>(services);
            RemoveDescriptor<DbContextOptions<CatalogDbContext>>(services);
            RemoveDescriptor<DbContextOptions<FinanceDbContext>>(services);

            services.AddDbContext<CoreDbContext>(o =>
                o.UseInMemoryDatabase($"Core_{_dbName}"));
            services.AddDbContext<CatalogDbContext>(o =>
                o.UseInMemoryDatabase($"Catalog_{_dbName}"));
            services.AddDbContext<FinanceDbContext>(o =>
                o.UseInMemoryDatabase($"Finance_{_dbName}"));
        });
    }

    private static void RemoveDescriptor<T>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null) services.Remove(descriptor);
    }

    public HttpClient CreateClientWithAuth(string role, string? userId = null, string? vendedorId = null)
    {
        var client = CreateClient();
        var token  = GenerateToken(role, userId, vendedorId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public string GenerateToken(string role, string? userId = null, string? vendedorId = null)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var uid         = userId ?? Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, uid),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, uid),
            new(ClaimTypes.Name,           $"Usuario_{role}"),
            new(ClaimTypes.Role,           role)
        };

        if (!string.IsNullOrEmpty(vendedorId))
            claims.Add(new Claim("VendedorId", vendedorId));

        var token = new JwtSecurityToken(
            issuer:             TestJwtIssuer,
            audience:           TestJwtAudience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(TestJwtExpMin),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
