/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
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
/// Operaciones explícitas de mantenimiento. El arranque HTTP no las invoca.
/// </summary>
public static class StartupInitializer
{
    // Identificador único para el advisory lock de migraciones en PostgreSQL
    private const long MigrationAdvisoryLockKey = 987654321012345678L;

    public static async Task EjecutarMigracionesAsync(
        CoreDbContext coreDb,
        CatalogDbContext catalogDb,
        FinanceDbContext financeDb,
        IConfiguration configuration,
        ILogger logger)
    {
        if (!coreDb.Database.IsNpgsql() || !catalogDb.Database.IsNpgsql() || !financeDb.Database.IsNpgsql())
            throw new InvalidOperationException("Las migraciones explícitas requieren PostgreSQL en los tres contextos.");

        int maxIntentos = int.TryParse(configuration["Startup:MaxRetries"], out var r) && r > 0 ? r : 5;
        int delayBaseMs = int.TryParse(configuration["Startup:InitialDelayMs"], out var d) && d >= 0 ? d : 1000;
        var lockTimeout = GetLockTimeout(configuration);

        for (int intento = 1; intento <= maxIntentos; intento++)
        {
            try
            {
                logger.LogInformation("Iniciando verificación y migración de base de datos (intento {Intento}/{Max})...", intento, maxIntentos);

                // Adquirir pg_advisory_lock usando conexión independiente para no afectar el ciclo de vida de EF Core
                var connStr = coreDb.Database.GetConnectionString();
                await using var lockConn = new NpgsqlConnection(connStr);
                await lockConn.OpenAsync();

                await AcquireMigrationLockAsync(lockConn, lockTimeout);

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
                        logger.LogWarning("Error al liberar el advisory lock: {ExceptionType}.", unlockEx.GetType().Name);
                    }
                }

                return; // Migración exitosa
            }
            catch (TimeoutException)
            {
                // Un lock ocupado requiere intervención; reintentar no debe multiplicar el plazo máximo.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Fallo en intento {Intento}/{Max} al migrar base de datos: {ExceptionType}",
                    intento, maxIntentos, ex.GetType().Name);

                if (intento == maxIntentos)
                    throw new InvalidOperationException($"No fue posible aplicar las migraciones tras {maxIntentos} intentos.", ex);

                int espera = delayBaseMs * intento;
                await Task.Delay(espera);
            }
        }
    }

    public static TimeSpan GetLockTimeout(IConfiguration configuration)
    {
        var configured = configuration["Startup:MigrationLockTimeoutSeconds"];
        if (configured is null) return TimeSpan.FromSeconds(30);
        if (!int.TryParse(configured, out var seconds) || seconds is < 1 or > 300)
            throw new InvalidOperationException("Startup:MigrationLockTimeoutSeconds debe estar entre 1 y 300.");
        return TimeSpan.FromSeconds(seconds);
    }

    private static async Task AcquireMigrationLockAsync(NpgsqlConnection connection, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(timeout);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", MigrationAdvisoryLockKey);
        command.CommandTimeout = 0; // El CancellationToken fija el plazo total de adquisición.

        try
        {
            while (true)
            {
                if (await command.ExecuteScalarAsync(deadline.Token) is true) return;
                var remaining = timeout - watch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException("No se obtuvo el bloqueo de migración en el plazo configurado.");
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250),
                    deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException("No se obtuvo el bloqueo de migración en el plazo configurado.");
        }
    }

    public static async Task CrearAdminInicialAsync(
        CoreDbContext coreDb,
        IConfiguration configuration,
        ILogger logger)
    {
        var rawEmail = configuration["ADMIN_EMAIL"];
        var password = configuration["ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(rawEmail) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("ADMIN_EMAIL y ADMIN_PASSWORD son obligatorios para --bootstrap-admin.");

        var email = rawEmail.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(email) || email.Length > 254 || password.Length < 10)
            throw new InvalidOperationException("El correo o la longitud de la contraseña del Admin inicial no son válidos.");

        var hasher = new PasswordHasher<Usuario>();
        // Detecta cuentas históricas con mayúsculas antes de intentar crear otro Admin.
        var existing = await coreDb.Usuarios.SingleOrDefaultAsync(u => u.Email.ToLower() == email);
        if (existing is not null)
        {
            if (existing.Rol != "Admin" || !existing.Activo ||
                hasher.VerifyHashedPassword(existing, existing.PasswordHash, password) == PasswordVerificationResult.Failed)
                throw new InvalidOperationException("La cuenta existente no coincide con un Admin activo y sus credenciales; se requiere revisión manual.");

            logger.LogInformation("Admin inicial existente validado; no se realizaron cambios.");
            return;
        }

        var admin = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = email,
            Nombre = "Administrador del Sistema",
            Rol = "Admin",
            Activo = true,
            FechaRegistro = DateTime.UtcNow
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        coreDb.Usuarios.Add(admin);
        await coreDb.SaveChangesAsync();
        logger.LogInformation("Admin inicial creado.");
    }

    public static async Task SeedDemoDataAsync(
        IServiceProvider serviceProvider,
        CoreDbContext coreDb,
        CatalogDbContext catalogDb,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger logger)
    {
        // Invocable exclusivamente desde el comando explícito seed-demo.
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
                if (res.Errores > 0) throw new InvalidOperationException("La importación demo tuvo errores; puede haber cambios parciales.");
                logger.LogInformation("Auto-seeding completado: {Exitosos}/{Total} SKUs cargados con WMS (VendedorId={VendedorId}, CategoriaId={CategoriaId}).",
                    res.Exitosos, res.TotalProcesados, vendedor.Id, categoria.Id);
            }
            else
            {
                throw new InvalidOperationException("Falta el archivo de catálogo para seed-demo.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Seed demo fallido: {ExceptionType}.", ex.GetType().Name);
            throw;
        }
    }
}
