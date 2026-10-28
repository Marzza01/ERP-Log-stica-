/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarketplaceERP.API.Services;

/// <summary>
/// Orquestador centralizado de migraciones y seeding durante el arranque del sistema.
/// Garantiza la ejecución ordenada:
/// (1) Migraciones relacionales con pg_advisory_lock y reintentos exponenciales.
/// (2) Seed del usuario Administrador del sistema.
/// (3) Auto-seeding del catálogo oficial bajo la bandera Seed:DemoData (sin GUIDs mágicos).
/// </summary>
public static class StartupInitializer
{
    // Identificador único para el advisory lock de migraciones en PostgreSQL
    private const long MigrationAdvisoryLockKey = 987654321012345678L;

    public static async Task InicializarAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        using var scope = services.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        // ── 1. Migraciones con reintentos y pg_advisory_lock ─────────────────
        await EjecutarMigracionesConReintentosAsync(coreDb, catalogDb, financeDb, environment, configuration, logger);

        // ── 2. Seed de Usuario Administrador ─────────────────────────────────
        await SeedAdminAsync(coreDb, configuration, environment, logger);

        // ── 3. Seed de Catálogo / Demo Data ───────────────────────────────────
        await SeedDemoDataAsync(scope.ServiceProvider, coreDb, catalogDb, configuration, environment, logger);
    }

    private static async Task EjecutarMigracionesConReintentosAsync(
        CoreDbContext coreDb,
        CatalogDbContext catalogDb,
        FinanceDbContext financeDb,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger logger)
    {
        // Si el proveedor no es relacional (ej. InMemory en tests de integración API), se omite sin error
        if (!coreDb.Database.IsRelational())
        {
            logger.LogInformation("Proveedor de base de datos no es relacional (InMemory). Omitiendo Database.Migrate().");
            return;
        }

        int maxIntentos = int.TryParse(configuration["Startup:MaxRetries"], out var r) ? r : 5;
        int delayBaseMs = int.TryParse(configuration["Startup:InitialDelayMs"], out var d) ? d : 1000;

        for (int intento = 1; intento <= maxIntentos; intento++)
        {
            try
            {
                logger.LogInformation("Iniciando verificación y migración de base de datos (intento {Intento}/{Max})...", intento, maxIntentos);

                // Adquirir pg_advisory_lock usando conexión independiente para no afectar el ciclo de vida de EF Core
                var connStr = coreDb.Database.GetConnectionString();
                await using var lockConn = new NpgsqlConnection(connStr);
                await lockConn.OpenAsync();

                await using (var cmdLock = lockConn.CreateCommand())
                {
                    cmdLock.CommandText = $"SELECT pg_advisory_lock({MigrationAdvisoryLockKey});";
                    await cmdLock.ExecuteNonQueryAsync();
                }

                try
                {
                    // Secuencia ordenada de migraciones: Core -> Catalog -> Finance
                    logger.LogInformation("Aplicando migraciones de CoreDbContext...");
                    await coreDb.Database.MigrateAsync();

                    logger.LogInformation("Aplicando migraciones de CatalogDbContext...");
                    await catalogDb.Database.MigrateAsync();

                    logger.LogInformation("Aplicando migraciones de FinanceDbContext...");
                    await financeDb.Database.MigrateAsync();

                    logger.LogInformation("Migraciones completadas exitosamente en todos los DbContexts.");
                }
                finally
                {
                    // Liberar siempre el advisory lock al terminar o ante fallo
                    try
                    {
                        await using var cmdUnlock = lockConn.CreateCommand();
                        cmdUnlock.CommandText = $"SELECT pg_advisory_unlock({MigrationAdvisoryLockKey});";
                        await cmdUnlock.ExecuteNonQueryAsync();
                    }
                    catch (Exception unlockEx)
                    {
                        logger.LogWarning(unlockEx, "Error al liberar el advisory lock.");
                    }
                }

                return; // Migración exitosa
            }
            catch (Exception ex)
            {
                logger.LogWarning("Fallo en intento {Intento}/{Max} al migrar base de datos: {Message}", intento, maxIntentos, ex.Message);

                if (intento == maxIntentos)
                {
                    if (environment.IsProduction())
                    {
                        logger.LogError(ex, "FATAL: No fue posible aplicar migraciones en Production tras {Max} intentos. Abortando arranque.", maxIntentos);
                        throw new InvalidOperationException($"No fue posible conectar o migrar la base de datos tras {maxIntentos} intentos en Production.", ex);
                    }
                    else
                    {
                        logger.LogError(ex, "ERROR: Fallaron las migraciones en entorno {Env}. Continuando arranque...", environment.EnvironmentName);
                        return;
                    }
                }

                int espera = delayBaseMs * intento;
                await Task.Delay(espera);
            }
        }
    }

    private static async Task SeedAdminAsync(
        CoreDbContext coreDb,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        var adminEmail = configuration["ADMIN_EMAIL"] ?? Environment.GetEnvironmentVariable("ADMIN_EMAIL");
        var adminPassword = configuration["ADMIN_PASSWORD"] ?? Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

        if (environment.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                throw new InvalidOperationException("ADMIN_EMAIL y ADMIN_PASSWORD son obligatorios en entorno de Production.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.LogWarning("ADMIN_EMAIL o ADMIN_PASSWORD no configurados en {Env}. Omitiendo creación de Admin inicial.", environment.EnvironmentName);
                return;
            }
        }

        try
        {
            var adminExistente = await coreDb.Usuarios.FirstOrDefaultAsync(u => u.Email == adminEmail.ToLower().Trim());
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
                await coreDb.SaveChangesAsync();
                logger.LogInformation("Usuario Admin inicial creado exitosamente con email: {Email}", adminEmail);
            }
        }
        catch (Exception ex)
        {
            if (environment.IsProduction())
            {
                logger.LogError(ex, "FATAL: Error al inicializar usuario Admin en Production.");
                throw;
            }
            logger.LogError(ex, "Error al crear usuario Admin en {Env}: {Message}", environment.EnvironmentName, ex.Message);
        }
    }

    private static async Task SeedDemoDataAsync(
        IServiceProvider serviceProvider,
        CoreDbContext coreDb,
        CatalogDbContext catalogDb,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        // Bandera Seed:DemoData (default true en Development, default false en Production)
        bool demoDataDefault = environment.IsDevelopment();
        bool seedDemo = configuration.GetValue<bool>("Seed:DemoData", demoDataDefault);

        if (!seedDemo)
        {
            logger.LogInformation("Seed:DemoData deshabilitado. Omitiendo auto-seeding de catálogo/demo.");
            return;
        }

        try
        {
            // Verificar si el catálogo ya cuenta con productos
            bool tieneProductos = await catalogDb.Productos.AnyAsync();
            if (tieneProductos)
            {
                return;
            }

            logger.LogInformation("Catálogo vacío. Obteniendo o creando Vendedor y Categoría por defecto desde la BD...");

            // Obtener o crear Vendedor por defecto sin GUIDs mágicos (por Email/Nombre)
            const string vendedorEmailDefecto = "vendedor.oficial@marketplaceerp.com";
            var vendedor = await coreDb.Vendedores.FirstOrDefaultAsync(v => v.Email == vendedorEmailDefecto);
            if (vendedor is null)
            {
                vendedor = new Vendedor
                {
                    Id = Guid.NewGuid(),
                    NombreEmpresa = "LOGIMKT S.A. de C.V. (Oficial)",
                    Email = vendedorEmailDefecto,
                    Telefono = "+503 2222-0000",
                    EstaActivo = true,
                    FechaRegistro = DateTime.UtcNow
                };
                coreDb.Vendedores.Add(vendedor);
                await coreDb.SaveChangesAsync();
            }

            // Obtener o crear Categoría por defecto sin GUIDs mágicos (por Nombre)
            const string categoriaNombreDefecto = "General";
            var categoria = await coreDb.Categorias.FirstOrDefaultAsync(c => c.Nombre == categoriaNombreDefecto);
            if (categoria is null)
            {
                categoria = new Categoria
                {
                    Id = Guid.NewGuid(),
                    Nombre = categoriaNombreDefecto,
                    Descripcion = "Categoría general de productos del catálogo"
                };
                coreDb.Categorias.Add(categoria);
                await coreDb.SaveChangesAsync();
            }

            var importService = serviceProvider.GetRequiredService<ICatalogImportService>();

            var posiblesRutas = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "SeedData", "Catalogo_500_Productos.csv"),
                Path.Combine(Directory.GetCurrentDirectory(), "SeedData", "Catalogo_500_Productos.csv"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "MarketplaceERP.Catalog", "SeedData", "Catalogo_500_Productos.csv"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "docs", "data", "Catalogo_500_Productos.csv")
            };

            string? rutaCsv = posiblesRutas.FirstOrDefault(File.Exists);
            if (rutaCsv != null)
            {
                var res = await importService.ImportarCatalogoDesdeArchivoAsync(rutaCsv, vendedor.Id, categoria.Id);
                logger.LogInformation("Auto-seeding completado: {Exitosos}/{Total} SKUs cargados con WMS (VendedorId={VendedorId}, CategoriaId={CategoriaId}).",
                    res.Exitosos, res.TotalProcesados, vendedor.Id, categoria.Id);
            }
            else
            {
                logger.LogWarning("No se encontró el archivo Catalogo_500_Productos.csv para el auto-seeding de DemoData.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Aviso en auto-seeding de demo data: {Message}", ex.Message);
        }
    }
}
