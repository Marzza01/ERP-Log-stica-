/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Text;
using System.Threading.RateLimiting;
using MarketplaceERP.Catalog;
using MarketplaceERP.Core;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Soporte para DATABASE_URL de Railway/Heroku ───────────────────────────────
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    // Railway provee: postgresql://user:pass@host:port/dbname
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    var connStr = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
    builder.Configuration["ConnectionStrings:DefaultConnection"] = connStr;
}

// ── Soporte alternativo via variable CONNECTION_STRING ────────────────────────
var connStrEnv = Environment.GetEnvironmentVariable("CONNECTION_STRING");
if (!string.IsNullOrEmpty(connStrEnv))
{
    builder.Configuration["ConnectionStrings:DefaultConnection"] = connStrEnv;
}

// ── Configuración JWT usando patrón Options y validación estricta ─────────────
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("La configuración JWT es obligatoria. Verifica appsettings.json o variables de entorno.");

// Validación estricta de Key y Expiration
var isProduction = builder.Environment.IsProduction();
if (isProduction)
{
    if (string.IsNullOrWhiteSpace(jwtSettings.Key) ||
        jwtSettings.Key.Length < 32 ||
        jwtSettings.Key.Contains("CAMBIA", StringComparison.OrdinalIgnoreCase) ||
        jwtSettings.Key.Contains("DEV_ONLY", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("En Production, Jwt:Key no puede estar vacía, debe tener mínimo 32 caracteres y no puede contener palabras placeholder ('CAMBIA' o 'DEV_ONLY').");
    }
}
else
{
    if (string.IsNullOrWhiteSpace(jwtSettings.Key) || jwtSettings.Key.Length < 32)
    {
        throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres. Configura appsettings.json o variables de entorno.");
    }
}

if (jwtSettings.ExpirationMinutes > 1440)
{
    throw new InvalidOperationException("Jwt:ExpirationMinutes no puede superar el límite máximo de 1440 minutos (24 horas).");
}

// ── Autenticación JWT Bearer ──────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings.Key)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ── Rate Limiting nativo para Login ───────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("LoginLimiter", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

// ── CORS desde Cors:AllowedOrigins o Cors__AllowedOrigins ────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment() ? new[] { "http://localhost:5173" } : Array.Empty<string>());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// ── Módulos (Equipos 1, 2 y 3) ───────────────────────────────────────────────
builder.Services.AddCoreServices(builder.Configuration);
builder.Services.AddCatalogServices(builder.Configuration);
builder.Services.AddFinanceServices(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "MarketplaceERP API",
            Version = "v1",
            Description = "API del Marketplace ERP — Monolito Modular con 3 equipos"
        });

        // Soporte JWT en Swagger UI
        options.AddSecurityDefinition("Bearer", new()
        {
            Name = "Authorization",
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Description = "Ingresa el token JWT. Ejemplo: Bearer {tu_token}"
        });
        options.AddSecurityRequirement(new()
        {
            {
                new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
                Array.Empty<string>()
            }
        });
    });
}

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsProduction())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MarketplaceERP v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ── Health Check Endpoint ─────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    servicio = "MarketplaceERP.API",
    version = "1.0.0"
})).AllowAnonymous();

app.MapControllers();

// ── Seed Inicial de Usuario Admin ─────────────────────────────────────────────
try
{
    using var scope = app.Services.CreateScope();
    var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
    var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

    if (app.Environment.IsProduction())
    {
        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException("ADMIN_EMAIL y ADMIN_PASSWORD son obligatorios en entorno de Production.");
        }
    }

    if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
    {
        if (coreDb.Database.CanConnect())
        {
            var adminExistente = coreDb.Usuarios.FirstOrDefault(u => u.Email.ToLower() == adminEmail.ToLower());
            if (adminExistente is null)
            {
                var hasher = new PasswordHasher<Usuario>();
                var adminUsuario = new Usuario
                {
                    Id = Guid.NewGuid(),
                    Email = adminEmail.ToLower().Trim(),
                    Nombre = "Administrador del Sistema",
                    Rol = "Admin",
                    Activo = true,
                    FechaRegistro = DateTime.UtcNow
                };
                adminUsuario.PasswordHash = hasher.HashPassword(adminUsuario, adminPassword);

                coreDb.Usuarios.Add(adminUsuario);
                coreDb.SaveChanges();
                logger.LogInformation("Usuario Admin inicial creado exitosamente con email: {Email}", adminEmail);
            }
        }
    }
    else
    {
        logger.LogWarning("Variables ADMIN_EMAIL o ADMIN_PASSWORD no configuradas en entorno {Env}. Omitiendo creación de Admin inicial.", app.Environment.EnvironmentName);
    }
}
catch (Exception ex) when (!app.Environment.IsProduction())
{
    var logger = app.Services.GetService<ILogger<Program>>();
    logger?.LogWarning("Aviso en seed de Admin (omitido si BD no está lista aún): {Message}", ex.Message);
}

// ── Auto-seeding del Catálogo de 500 Productos (LOGIMKT S.A. de C.V.) ──────────
try
{
    using var scope = app.Services.CreateScope();
    var catalogDb = scope.ServiceProvider.GetRequiredService<MarketplaceERP.Catalog.Data.CatalogDbContext>();
    var importService = scope.ServiceProvider.GetRequiredService<MarketplaceERP.Catalog.Services.ICatalogImportService>();

    if (catalogDb.Database.CanConnect() && !catalogDb.Productos.Any())
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Base de datos de catálogo vacía. Iniciando auto-seeding de los 500 SKUs...");

        var defaultVendedorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var defaultCategoriaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var posiblesRutas = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "SeedData", "Catalogo_500_Productos.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "SeedData", "Catalogo_500_Productos.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "MarketplaceERP.Catalog", "SeedData", "Catalogo_500_Productos.csv")
        };

        string? rutaCsv = posiblesRutas.FirstOrDefault(File.Exists);
        if (rutaCsv != null)
        {
            var res = importService.ImportarCatalogoDesdeArchivoAsync(rutaCsv, defaultVendedorId, defaultCategoriaId).GetAwaiter().GetResult();
            logger.LogInformation("Auto-seeding completado: {Exitosos}/{Total} SKUs cargados con WMS.", res.Exitosos, res.TotalProcesados);
        }
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetService<ILogger<Program>>();
    logger?.LogWarning("Aviso en auto-seeding (omitido si PostgreSQL aún no está listo): {Message}", ex.Message);
}

app.Run();

public partial class Program { }
