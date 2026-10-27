/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using FluentAssertions;
using MarketplaceERP.API.Services;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.API.Tests;

/// <summary>
/// Tests exhaustivos de StartupInitializer con PostgreSQL real (FASE 6):
/// (a) En BD vacía, el arranque completo del initializer produce todas las tablas y el Admin sin servidor HTTP.
/// (b) Una segunda ejecución consecutiva es idempotente (no duplica el Admin ni arroja errores).
/// (c) En Production con base de datos inalcanzable, el initializer lanza InvalidOperationException tras los reintentos.
/// </summary>
public class StartupInitializerPostgresRealTests : IAsyncLifetime
{
    private static readonly string? RawConnStr = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
    private readonly string _dbName = $"test_startup_{Guid.NewGuid():N}";
    private string _adminConnStr = "";
    private string _testConnStr = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(RawConnStr)) return;

        var builder = new NpgsqlConnectionStringBuilder(RawConnStr)
        {
            Database = "postgres"
        };
        _adminConnStr = builder.ConnectionString;

        builder.Database = _dbName;
        builder.IncludeErrorDetail = true;
        _testConnStr = builder.ConnectionString;

        await using var adminConn = new NpgsqlConnection(_adminConnStr);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_dbName}\";";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(RawConnStr)) return;

        try
        {
            await using var adminConn = new NpgsqlConnection(_adminConnStr);
            await adminConn.OpenAsync();
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $@"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = '{_dbName}' AND pid <> pg_backend_pid();
                DROP DATABASE IF EXISTS ""{_dbName}"";
            ";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignorar errores de limpieza
        }
    }

    private ServiceProvider BuildServiceProvider(string connStr, bool isProduction = false, bool seedDemo = false)
    {
        var services = new ServiceCollection();

        services.AddDbContext<CoreDbContext>(o =>
            o.UseNpgsql(connStr, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "core")));
        services.AddDbContext<CatalogDbContext>(o =>
            o.UseNpgsql(connStr, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")));
        services.AddDbContext<FinanceDbContext>(o =>
            o.UseNpgsql(connStr, n => n.MigrationsHistoryTable("__EFMigrationsHistory", "finance")));

        // Mock para import service en tests de arranque
        var mockImport = new Mock<ICatalogImportService>();
        mockImport.Setup(i => i.ImportarCatalogoDesdeArchivoAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResult(0, 0, 0, new List<string>()));
        services.AddSingleton(mockImport.Object);

        return services.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Test_A_BDVacia_InitializerProduceTablasYAdmin_SinServidorHttp()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ADMIN_EMAIL"] = "admin@marketplaceerp.com",
                ["ADMIN_PASSWORD"] = "Admin123*SuperSeguro!",
                ["Seed:DemoData"] = "false",
                ["Startup:MaxRetries"] = "2",
                ["Startup:InitialDelayMs"] = "100"
            })
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var serviceProvider = BuildServiceProvider(_testConnStr, isProduction: true);

        // Act: Invocación directa del inicializador sin servidor HTTP
        await StartupInitializer.InicializarAsync(serviceProvider, config, mockEnv.Object, NullLogger.Instance);

        // Assert: Validar que las tablas existen y el Admin fue persistido
        using var scope = serviceProvider.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var catalogDb = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var financeDb = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        (await coreDb.Database.CanConnectAsync()).Should().BeTrue();
        (await catalogDb.Database.CanConnectAsync()).Should().BeTrue();
        (await financeDb.Database.CanConnectAsync()).Should().BeTrue();

        var admin = await coreDb.Usuarios.FirstOrDefaultAsync(u => u.Email == "admin@marketplaceerp.com");
        admin.Should().NotBeNull();
        admin!.Rol.Should().Be("Admin");
        admin.Activo.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Test_B_SegundaEjecucion_EsIdempotente_SinDuplicarAdminNiFallar()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ADMIN_EMAIL"] = "admin@marketplaceerp.com",
                ["ADMIN_PASSWORD"] = "Admin123*SuperSeguro!",
                ["Seed:DemoData"] = "false",
                ["Startup:MaxRetries"] = "2",
                ["Startup:InitialDelayMs"] = "100"
            })
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var serviceProvider = BuildServiceProvider(_testConnStr, isProduction: true);

        // Primera ejecución
        await StartupInitializer.InicializarAsync(serviceProvider, config, mockEnv.Object, NullLogger.Instance);

        // Segunda ejecución consecutiva
        var act = () => StartupInitializer.InicializarAsync(serviceProvider, config, mockEnv.Object, NullLogger.Instance);
        await act.Should().NotThrowAsync();

        // Verificar que no se duplicó el Admin
        using var scope = serviceProvider.CreateScope();
        var coreDb = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var totalAdmins = await coreDb.Usuarios.CountAsync(u => u.Email == "admin@marketplaceerp.com");
        totalAdmins.Should().Be(1);
    }

    [SkippableFact]
    public async Task Test_C_EnProduction_ConBDInalcanzable_LanzaExcepcionTrasReintentos()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        // Cadena con host inalcanzable y timeout muy bajo
        var connInvalida = "Host=127.0.0.1;Port=1;Database=inexistente;Timeout=1;Command Timeout=1";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ADMIN_EMAIL"] = "admin@marketplaceerp.com",
                ["ADMIN_PASSWORD"] = "Admin123*SuperSeguro!",
                ["Startup:MaxRetries"] = "2",
                ["Startup:InitialDelayMs"] = "50" // Reintentos rápidos para no tardar
            })
            .Build();

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var serviceProvider = BuildServiceProvider(connInvalida, isProduction: true);

        // Act: En Production, debe fallar con excepción obligatoria
        var act = () => StartupInitializer.InicializarAsync(serviceProvider, config, mockEnv.Object, NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No fue posible conectar o migrar la base de datos tras 2 intentos en Production*");
    }
}
