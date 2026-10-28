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
using MarketplaceERP.Finance.Data;
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
    // NOTA: Railway utiliza certificados autofirmados / proxies dinámicos en su infraestructura PostgreSQL interna.
    // 'Trust Server Certificate=true' es requerido temporalmente para permitir handshake SSL sin error en Railway;
    // se mantendrá documentado hasta que se configure una CA personalizada de confianza en el contenedor.
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
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var allowedOrigins = configuredOrigins.Length > 0
    ? configuredOrigins
    : (builder.Environment.IsDevelopment() ? new[] { "http://localhost:5173" } : Array.Empty<string>());

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
    });
});

// ── Módulos (Equipos 1, 2 y 3) ───────────────────────────────────────────────
builder.Services.AddCoreServices(builder.Configuration);
builder.Services.AddCatalogServices(builder.Configuration);
builder.Services.AddFinanceServices(builder.Configuration);

// Servicio unificado de numeración secuencial
builder.Services.AddScoped<MarketplaceERP.Core.Interfaces.INumeracionService, MarketplaceERP.API.Services.NumeracionService>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<MarketplaceERP.API.Filters.ConcurrencyExceptionFilter>();
})
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
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1
    };
    forwardedOptions.KnownNetworks.Clear();
    forwardedOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedOptions);

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

// ── Health Check Endpoints (Anónimos) ─────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    servicio = "MarketplaceERP.API",
    version = "1.0.0"
})).AllowAnonymous();

app.MapGet("/health/ready", async (
    CoreDbContext coreDb,
    MarketplaceERP.Catalog.Data.CatalogDbContext catalogDb,
    FinanceDbContext financeDb) =>
{
    bool coreOk = false, catalogOk = false, financeOk = false;
    try
    {
        coreOk = await coreDb.Database.CanConnectAsync();
        catalogOk = await catalogDb.Database.CanConnectAsync();
        financeOk = await financeDb.Database.CanConnectAsync();
    }
    catch
    {
        // En caso de fallo de red/conexión, CanConnectAsync puede lanzar excepción
    }

    if (coreOk && catalogOk && financeOk)
    {
        return Results.Ok(new { status = "ready", timestamp = DateTime.UtcNow });
    }

    // Retorna 503 Service Unavailable sin exponer detalles internos sensibles
    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapControllers();

// ── Secuencia de arranque única, ordenada y asíncrona (StartupInitializer) ────
await MarketplaceERP.API.Services.StartupInitializer.InicializarAsync(
    app.Services,
    app.Configuration,
    app.Environment,
    app.Logger);

app.Run();

public partial class Program { }
