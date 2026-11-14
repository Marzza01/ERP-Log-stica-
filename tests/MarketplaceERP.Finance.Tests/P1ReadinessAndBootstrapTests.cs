using MarketplaceERP.API.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

public sealed class P1ReadinessAndBootstrapTests
{
    [Fact]
    public void Port_RespetaPortValido_YRechazaPuertosInvalidos()
    {
        Assert.Null(RuntimePortConfiguration.ResolveUrls(null));
        Assert.Equal("http://0.0.0.0:10000", RuntimePortConfiguration.ResolveUrls("10000"));
        Assert.Throws<InvalidOperationException>(() => RuntimePortConfiguration.ResolveUrls("0"));
        Assert.Throws<InvalidOperationException>(() => RuntimePortConfiguration.ResolveUrls("65536"));
        Assert.Throws<InvalidOperationException>(() => RuntimePortConfiguration.ResolveUrls("not-a-port"));
    }

    [Fact]
    public async Task BootstrapAdmin_NoDaFalsoExitoConCredencialesErroneas()
    {
        await using var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase("p1-bootstrap-" + Guid.NewGuid().ToString("N")).Options);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ADMIN_EMAIL"] = "Admin.Evaluacion@erp.invalid",
                ["ADMIN_PASSWORD"] = "P1-temporary-test-only-1234567"
            }).Build();
        await StartupInitializer.CrearAdminInicialAsync(core, settings, NullLogger.Instance);
        var usuario = Assert.Single(await core.Usuarios.ToListAsync());
        Assert.Equal("admin.evaluacion@erp.invalid", usuario.Email);
        Assert.Equal("Admin", usuario.Rol);
        Assert.True(usuario.Activo);
        Assert.NotEqual(settings["ADMIN_PASSWORD"], usuario.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed,
            new PasswordHasher<Usuario>().VerifyHashedPassword(
                usuario, usuario.PasswordHash, settings["ADMIN_PASSWORD"]!));

        await StartupInitializer.CrearAdminInicialAsync(core, settings, NullLogger.Instance);
        Assert.Single(await core.Usuarios.ToListAsync());

        var wrongPassword = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ADMIN_EMAIL"] = "admin.evaluacion@erp.invalid",
                ["ADMIN_PASSWORD"] = "P1-wrong-password-12345678"
            }).Build();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartupInitializer.CrearAdminInicialAsync(core, wrongPassword, NullLogger.Instance));
        Assert.Single(await core.Usuarios.ToListAsync());

        usuario.Activo = false;
        await core.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartupInitializer.CrearAdminInicialAsync(core, settings, NullLogger.Instance));
    }

    [Fact]
    public async Task Readiness_RechazaDbVaciaYEsquemaSinHistorialEF()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("P1 requiere PostgreSQL local en CI.");
            return;
        }

        // Nunca crear bases efímeras fuera del PostgreSQL del runner local.
        var admin = new NpgsqlConnectionStringBuilder(connection);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                admin.Host, StringComparer.OrdinalIgnoreCase)
            || admin.Port != 5432
            || !string.Equals(admin.Database, "postgres", StringComparison.Ordinal)
            || !string.Equals(admin.Username, "postgres", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "P1 solo puede crear DB efímera en postgres@localhost:5432/postgres.");

        var database = "p1_ready_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(admin.ConnectionString))
        {
            await root.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", root);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var local = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
            {
                Database = database
            }.ConnectionString;
            await using var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
                .UseNpgsql(local).Options);

            const string appTable = "core.usuarios";
            const string historyTable = "core.\"__EFMigrationsHistory\"";
            Assert.False(await ReadinessSchemaProbe.RequiredTablesExistAsync(
                core, appTable, historyTable));

            await core.Database.ExecuteSqlRawAsync("CREATE SCHEMA core");
            await core.Database.ExecuteSqlRawAsync("CREATE TABLE core.usuarios (\"Id\" uuid NOT NULL)");
            Assert.False(await ReadinessSchemaProbe.RequiredTablesExistAsync(
                core, appTable, historyTable));

            await core.Database.ExecuteSqlRawAsync(
                "CREATE TABLE core.\"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) NOT NULL)");
            Assert.True(await ReadinessSchemaProbe.RequiredTablesExistAsync(
                core, appTable, historyTable));
            // A history table's mere existence is insufficient: no migrations applied.
            Assert.False(await ReadinessSchemaProbe.CurrentMigrationsAppliedAsync(core));
        }
        finally
        {
            // Se destruye solo la base efímera de la prueba en localhost.
            await using var root = new NpgsqlConnection(admin.ConnectionString);
            await root.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", root);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
