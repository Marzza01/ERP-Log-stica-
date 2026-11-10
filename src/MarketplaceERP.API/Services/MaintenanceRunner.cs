using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketplaceERP.API.Services;

public static class MaintenanceRunner
{
    public static async Task<int> RunAsync(MaintenanceCommand command)
    {
        try
        {
            if (!Enum.IsDefined(command.Action) || command.Action == MaintenanceAction.Http)
                throw new InvalidOperationException("HTTP no es una acción de mantenimiento.");
            // Build configuration/DI only; never Build/Start a web or generic host.
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = Array.Empty<string>(), ContentRootPath = AppContext.BaseDirectory
            });
            var config = builder.Configuration;
            var connection = DatabaseConnectionConfiguration.Resolve(config);
            var target = MaintenanceTarget.Describe(connection);
            if (command.Action == MaintenanceAction.DescribeTarget)
            {
                Console.WriteLine($"Host: {target.Host}; Port: {target.Port}; Database: {target.Database}");
                Console.WriteLine($"Target fingerprint: {target.Fingerprint}");
                return 0; // No context, connection or SQL is created for description.
            }
            target.Confirm(command);
            // Unlike HTTP startup, maintenance has its own execution path.
            // Enforce the same staging identity before opening any DB connection
            // or applying any schema/data writes. Production remote maintenance
            // remains blocked pending a separately authorized deployment gate.
            StagingDatabaseTargetGuard.ValidateMaintenanceWriteTarget(
                config, Environment.GetEnvironmentVariable("RENDER_SERVICE_NAME"),
                builder.Environment.EnvironmentName, connection);
            if (command.Action == MaintenanceAction.SeedDemo && !builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Seed demo solo se admite explícitamente en Development.");

            builder.Services.AddDbContext<CoreDbContext>(o => o.UseNpgsql(connection,
                n => n.MigrationsHistoryTable("__EFMigrationsHistory", "core")));
            builder.Services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(connection,
                n => n.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
            builder.Services.AddDbContext<FinanceDbContext>(o => o.UseNpgsql(connection,
                n => n.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));
            builder.Services.AddScoped<MarketplaceERP.Catalog.Services.ICatalogImportService,
                MarketplaceERP.Catalog.Services.CatalogImportService>();
            using var services = builder.Services.BuildServiceProvider();
            using var scope = services.CreateScope();
            var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            // Read identity only in the deliberately invoked maintenance process, before writes.
            foreach (var db in new DbContext[] { core, catalog, finance })
            {
                var c = db.Database.GetDbConnection();
                await c.OpenAsync();
                try
                {
                    await using var query = c.CreateCommand();
                    query.CommandText = "SELECT current_database()";
                    if ((string?)await query.ExecuteScalarAsync() != target.Database)
                        throw new InvalidOperationException("Base conectada distinta de la confirmada.");
                }
                finally { await c.CloseAsync(); }
            }
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ExplicitMaintenance");
            switch (command.Action)
            {
                case MaintenanceAction.Migrate:
                    await StartupInitializer.EjecutarMigracionesAsync(core, catalog, finance, config, logger);
                    break;
                case MaintenanceAction.BootstrapAdmin:
                    await StartupInitializer.CrearAdminInicialAsync(core, config, logger);
                    break;
                case MaintenanceAction.SeedDemo:
                    await StartupInitializer.SeedDemoDataAsync(scope.ServiceProvider, core, catalog, config, builder.Environment, logger);
                    break;
                default: throw new InvalidOperationException("Acción no ejecutable.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            // Provider exceptions may contain connection details. Do not print them or resolved config.
            Console.Error.WriteLine($"Mantenimiento fallido ({ex.GetType().Name}); revisar estado antes de reintentar.");
            return 1;
        }
    }
}
