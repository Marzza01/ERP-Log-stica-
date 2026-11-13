/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using MarketplaceERP.API.Security;
using MarketplaceERP.API.Services;
using System.Threading.RateLimiting;
using MarketplaceERP.Catalog;
using MarketplaceERP.Core;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

MaintenanceCommand command;
try { command = MaintenanceCommand.Parse(args); }
catch (ArgumentException)
{
    Console.Error.WriteLine("Comando de mantenimiento inválido. No se inició HTTP ni mantenimiento.");
    Environment.ExitCode = 2;
    return;
}
if (await MaintenanceCommand.RunMaintenanceIfRequestedAsync(command, async () =>
    { Environment.ExitCode = await MaintenanceRunner.RunAsync(command); }))
    return;

var builder = WebApplication.CreateBuilder(args);

// Ambos modos usan la misma selección de destino. Ninguno abre la conexión aquí.
var databaseConnection = DatabaseConnectionConfiguration.Resolve(builder.Configuration);
StagingDatabaseTargetGuard.Validate(builder.Configuration,
    Environment.GetEnvironmentVariable("RENDER_SERVICE_NAME"),
    builder.Environment.EnvironmentName, databaseConnection);
builder.Configuration["ConnectionStrings:DefaultConnection"] = databaseConnection;

// Railway inyecta PORT al ejecutar el contenedor, no al construir la imagen.
var listenUrls = RuntimePortConfiguration.ResolveUrls(builder.Configuration["PORT"]);
if (listenUrls is not null) builder.WebHost.UseUrls(listenUrls);

// ── Configuración JWT usando patrón Options y validación estricta ─────────────
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("La configuración JWT es obligatoria. Verifica appsettings.json o variables de entorno.");

builder.Services.AddMarketplaceSecurity(jwtSettings);

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
builder.Services.AddFinanceServices(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddScoped<MarketplaceERP.API.Services.ReservasExpiracionService>();
builder.Services.AddScoped<MarketplaceERP.API.Services.OrdenEvaluacionService>();

builder.Services.AddControllers(options =>
    {
        // Recuperación selectiva de concurrencia histórica: solo conflictos EF a HTTP 409.
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
    if (DeploymentHttpSecurity.RenderTerminatesTls(
        Environment.GetEnvironmentVariable("RENDER_SERVICE_NAME")))
    {
        // Render ya redirige HTTP→HTTPS en el borde. No confiar en
        // X-Forwarded-For/Proto recibidos por el contenedor para rate limiting.
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                DeploymentHttpSecurity.AddRenderHsts(context.Response.Headers);
                return Task.CompletedTask;
            });
            await next(context);
        });
    }
    else
    {
        // Para otros despliegues, aceptar solo proxies loopback explícitos.
        // Nunca eliminar los filtros de IP para confiar en cabeceras arbitrarias.
        app.UseForwardedHeaders(DeploymentHttpSecurity.LocalTrustedForwarders());
        app.UseHsts();
        app.UseHttpsRedirection();
    }
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
        // CanConnectAsync alone returns true for an empty database. Require
        // a representative module table and each EF migration history table.
        coreOk = await coreDb.Database.CanConnectAsync() &&
            await ReadinessSchemaProbe.RequiredTablesExistAsync(
                coreDb, "core.usuarios", "core.\"__EFMigrationsHistory\"") &&
            await ReadinessSchemaProbe.CurrentMigrationsAppliedAsync(coreDb);
        catalogOk = await catalogDb.Database.CanConnectAsync() &&
            await ReadinessSchemaProbe.RequiredTablesExistAsync(
                catalogDb, "catalog.productos", "catalog.\"__EFMigrationsHistory\"") &&
            await ReadinessSchemaProbe.CurrentMigrationsAppliedAsync(catalogDb);
        financeOk = await financeDb.Database.CanConnectAsync() &&
            await ReadinessSchemaProbe.RequiredTablesExistAsync(
                financeDb, "finance.ordenes_compra", "finance.\"__EFMigrationsHistory\"") &&
            await ReadinessSchemaProbe.CurrentMigrationsAppliedAsync(financeDb);
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

app.Run();

public partial class Program { }
