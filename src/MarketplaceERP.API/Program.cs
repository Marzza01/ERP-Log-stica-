using System.Text;
using MarketplaceERP.Catalog;
using MarketplaceERP.Core;
using MarketplaceERP.Finance;
using MarketplaceERP.Shared.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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


// ── Configuración JWT usando patrón Options ───────────────────────────────────
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

var jwtSettings = builder.Configuration
    .GetSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("La configuración JWT es obligatoria. Verifica appsettings.json.");

if (string.IsNullOrWhiteSpace(jwtSettings.Key) || jwtSettings.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres. Configura appsettings.json.");

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

builder.Services.AddAuthorization();
builder.Services.AddCors();

// ── Módulos (Equipos 1, 2 y 3) ───────────────────────────────────────────────
// Cada módulo registra su propio DbContext, servicios y mocks
builder.Services.AddCoreServices(builder.Configuration);
builder.Services.AddCatalogServices(builder.Configuration);
builder.Services.AddFinanceServices(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
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

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod());

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MarketplaceERP v1");
    options.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

// ── Health Check Endpoint ─────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    servicio = "MarketplaceERP.API",
    version = "1.0.0"
}));

app.MapControllers();

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
    // No bloquea el inicio de la app si la base de datos no está levantada aún
    var logger = app.Services.GetService<ILogger<Program>>();
    logger?.LogWarning("Aviso en auto-seeding (omitido si PostgreSQL aún no está listo): {Message}", ex.Message);
}

app.Run();
