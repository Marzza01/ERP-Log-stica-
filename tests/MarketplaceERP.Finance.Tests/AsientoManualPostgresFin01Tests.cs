using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// FIN-01 contra PostgreSQL 15 real: un DB efímero por suite y nunca Neon.
/// CI requiere TEST_PG_CONNECTION apuntando a postgres local de Actions.
/// </summary>
public sealed class AsientoManualPostgresFin01Tests : IAsyncLifetime
{
    private readonly string _database = "ci_fin01_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _testConnection;
    private bool _created;

    private bool Active => _testConnection is not null && _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Local: pruebas PostgreSQL opcionales. CI: nunca permitir pass falso.
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CI requiere TEST_PG_CONNECTION para FIN-01.");
            return;
        }

        var conn = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(conn.Host, StringComparer.OrdinalIgnoreCase) ||
            conn.Port != 5432 || !string.Equals(conn.Database, "postgres", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "FIN-01 requiere PostgreSQL local de pruebas en puerto 5432/base postgres. Nunca Neon.");

        _adminConnection = conn.ConnectionString;
        conn.Database = _database; // CI exclusivo, nombre aleatorio sin aportes externos.
        _testConnection = conn.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnection))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_database}\"";
            await cmd.ExecuteNonQueryAsync();
            _created = true;
        }

        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    private FinanceDbContext NewContext()
    {
        if (!Active)
            throw new InvalidOperationException("Base FIN-01 de prueba no inicializada.");
        return new FinanceDbContext(
            new DbContextOptionsBuilder<FinanceDbContext>()
                .UseNpgsql(_testConnection!)
                .Options);
    }

    private static ContabilidadService Service(FinanceDbContext db) =>
        new(db, NullLogger<ContabilidadService>.Instance);

    [Fact]
    public async Task PostgreSql_PersisteCuentasGlosasYBalanceExactos()
    {
        if (!Active) return;
        Guid partidaId;
        await using (var db = NewContext())
        {
            var result = await Service(db).GenerarAsientoManualAsync(
                "Ajuste real PostgreSQL", "Ajuste", new[]
                {
                    new LineaAsientoInput("1101", 113.00m, 0m, "Cargo Caja"),
                    new LineaAsientoInput("4101", 0m, 100.00m, "Abono Ingresos"),
                    new LineaAsientoInput("2105", 0m, 13.00m, "Abono IVA")
                });
            partidaId = result.Id;
        }

        await using (var verify = NewContext())
        {
            var persisted = await verify.PartidasContables.AsNoTracking()
                .Include(p => p.Lineas).SingleAsync(p => p.Id == partidaId);
            Assert.Equal("Ajuste real PostgreSQL", persisted.Concepto);
            Assert.Equal("Ajuste", persisted.TipoOperacion);
            Assert.Null(persisted.ReferenciaOrigenId);
            Assert.Equal(113.00m, persisted.TotalDebe);
            Assert.Equal(113.00m, persisted.TotalHaber);
            Assert.Equal(3, persisted.Lineas.Count);
            Assert.Contains(persisted.Lineas, x =>
                x.CodigoCuenta == "1101" && x.Debe == 113m && x.Glosa == "Cargo Caja");
            Assert.Contains(persisted.Lineas, x =>
                x.CodigoCuenta == "4101" && x.Haber == 100m && x.Glosa == "Abono Ingresos");
            Assert.Contains(persisted.Lineas, x =>
                x.CodigoCuenta == "2105" && x.Haber == 13m && x.Glosa == "Abono IVA");

            var balance = await Service(verify).ObtenerBalanceComprobacionAsync();
            Assert.True(balance.EstaCuadrado);
            Assert.Equal(balance.TotalDebe, balance.TotalHaber);
        }
    }

    [Fact]
    public async Task PostgreSql_EntradaDescuadrada_NoPersisteNingunRegistro()
    {
        if (!Active) return;
        await using var db = NewContext();
        var antes = await db.PartidasContables.CountAsync();
        var lineasAntes = await db.LineasPartidaContable.CountAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(db).GenerarAsientoManualAsync("No válido", "Ajuste", new[]
            {
                new LineaAsientoInput("1101", 10m, 0m),
                new LineaAsientoInput("4101", 0m, 9.99m)
            }));
        Assert.Equal(antes, await db.PartidasContables.CountAsync());
        Assert.Equal(lineasAntes, await db.LineasPartidaContable.CountAsync());
    }

    [Fact]
    public async Task PostgreSql_DoceAltasConcurrentes_NoColisionanNumeroPartida()
    {
        if (!Active) return;
        var numeros = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(async i =>
            {
                await using var db = NewContext();
                var result = await Service(db).GenerarAsientoManualAsync(
                    "Ajuste paralelo " + i, "Ajuste", new[]
                    {
                        new LineaAsientoInput("1101", 5m, 0m),
                        new LineaAsientoInput("4101", 0m, 5m)
                    });
                return result.NumeroPartida;
            }));

        Assert.Equal(12, numeros.Distinct(StringComparer.Ordinal).Count());
        await using var verify = NewContext();
        var dbNumeros = await verify.PartidasContables
            .Where(p => p.Concepto.StartsWith("Ajuste paralelo"))
            .Select(p => p.NumeroPartida).ToListAsync();
        Assert.Equal(12, dbNumeros.Count);
        Assert.Equal(12, dbNumeros.Distinct(StringComparer.Ordinal).Count());
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null ||
            !_database.StartsWith("ci_fin01_", StringComparison.Ordinal))
            return;

        // Únicamente la base efímera creada por ESTE test, nunca postgres/Neon.
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var cmd = admin.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
        _created = false;
    }
}
