using System.Text;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// Pruebas de atomicidad sobre un PostgreSQL 15 LOCAL, efímero y desechable.
/// Nunca acepta una conexión de Neon, Render, staging o producción.
/// </summary>
public sealed class CatalogImportAtomicPostgresTests : IAsyncLifetime
{
    private readonly string _database = "ci_catalog_import_" + Guid.NewGuid().ToString("N");
    private string? _adminConnection;
    private string? _testConnection;
    private bool _created;

    private const string Header =
        "\"SKU\",\"Nombre del Producto\",\"Categoría\",\"Subcategoría\",\"Precio B2C ($)\",\"Precio B2B ($)\",\"Stock Inicial\",\"Stock Mínimo\",\"Proveedor\",\"País Origen\",\"Peso (kg)\",\"Dimensiones (cm)\",\"Unidad de Medida\",\"Estado\",\"Código EAN\",\"Descripción Corta\"";

    private static string Row(string sku, string price, string stock) =>
        $"\"{sku}\",\"Producto {sku}\",\"Electrónica y Tecnología\",\"General\",\"{price}\",\"10.00\",\"{stock}\",\"5\",\"Proveedor\",\"El Salvador\",\"1.00\",\"20x10x5 cm\",\"Unidad\",\"Activo\",\"\",\"Ficha\"";

    private static MemoryStream FileOf(params string[] rows) =>
        new(Encoding.UTF8.GetBytes(Header + "\n" + string.Join("\n", rows) + "\n"));

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La CI de Catalog exige PostgreSQL local desechable.");
            return;
        }

        var c = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(c.Host, StringComparer.OrdinalIgnoreCase) ||
            c.Port != 5432 ||
            !string.Equals(c.Database, "postgres", StringComparison.Ordinal) ||
            !string.Equals(c.Username, "postgres", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(c.Password))
            throw new InvalidOperationException(
                "La auditoría de importación SOLO admite postgres@localhost:5432/postgres.");

        _adminConnection = c.ConnectionString;
        c.Database = _database;
        _testConnection = c.ConnectionString;

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

    private CatalogDbContext NewContext() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_testConnection!, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
            .Options);

    [Fact]
    public async Task LoteConFilaInvalida_NoEscribeNingunSkuNiUbicacion_Postgres()
    {
        if (!_created) return;

        await using var db = NewContext();
        var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
        using var file = FileOf(
            Row("ATOMIC-VALID", "25.00", "20"),
            Row("ATOMIC-INVALID", "30.00", "stock-malformado"));

        var result = await service.ImportarCatalogoCsvAsync(file, Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(2, result.TotalProcesados);
        Assert.Equal(0, result.Exitosos);
        Assert.Equal(1, result.Errores);

        await using var verify = NewContext();
        Assert.Equal(0, await verify.Productos.CountAsync());
        Assert.Equal(0, await verify.StockFisico.CountAsync());
        Assert.Equal(0, await verify.UbicacionesAlmacen.CountAsync());
    }

    [Fact]
    public async Task FalloInyectadoEnPostgreSql_RollbackDeProductosStockYUbicaciones()
    {
        if (!_created) return;

        // Trigger deliberadamente SOLO en la base temporal ci_catalog_import_*.
        // El segundo producto es rechazado por PostgreSQL DESPUÉS de que
        // SaveChanges ha comenzado: la transacción implícita debe revertir todo.
        await using (var connection = new NpgsqlConnection(_testConnection!))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE FUNCTION catalog.ci_reject_import() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."Sku" = 'ROLL-FAIL' THEN
                        RAISE EXCEPTION 'Fallo de persistencia inyectado para la auditoría';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER ci_reject_import_trigger
                  BEFORE INSERT ON catalog.productos
                  FOR EACH ROW EXECUTE FUNCTION catalog.ci_reject_import();
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using (var db = NewContext())
        {
            var service = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
            using var file = FileOf(
                Row("ROLL-PASS", "25.00", "25"),
                Row("ROLL-FAIL", "30.00", "40"));
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.ImportarCatalogoCsvAsync(file, Guid.NewGuid(), Guid.NewGuid()));
        }

        await using var verify = NewContext();
        Assert.Equal(0, await verify.Productos.CountAsync());
        Assert.Equal(0, await verify.StockFisico.CountAsync());
        Assert.Equal(0, await verify.UbicacionesAlmacen.CountAsync());
    }

    [Fact]
    public async Task DosImportacionesConcurrentes_MismoSku_SeSerializanSinDuplicarStock()
    {
        if (!_created) return;

        var vendedor = Guid.NewGuid();
        var categoria = Guid.NewGuid();

        // Retener exactamente el candado transaccional que debe usar el servicio.
        // Si se omite ese lock en la implementación, los dos importadores no
        // aparecerán bloqueados en pg_stat_activity y esta prueba fallará.
        await using var bloqueo = new NpgsqlConnection(_testConnection!);
        await bloqueo.OpenAsync();
        await using var transaccionBloqueo = await bloqueo.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(6173152, 20261108)",
            bloqueo, transaccionBloqueo))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        async Task<ImportResult> ImportarAsync()
        {
            await using var db = NewContext();
            var servicio = new CatalogImportService(db, NullLogger<CatalogImportService>.Instance);
            using var archivo = FileOf(Row("CONCURRENT-001", "25.00", "40"));
            return await servicio.ImportarCatalogoCsvAsync(archivo, vendedor, categoria);
        }

        var primero = Task.Run(ImportarAsync);
        var segundo = Task.Run(ImportarAsync);

        try
        {
            var ambosBloqueados = false;
            for (int intento = 0; intento < 100; intento++)
            {
                await using var count = new NpgsqlCommand("""
                    SELECT count(*) FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND wait_event_type = 'Lock'
                      AND wait_event = 'advisory'
                    """, bloqueo, transaccionBloqueo);
                var esperando = (long)(await count.ExecuteScalarAsync())!;
                if (esperando >= 2)
                {
                    ambosBloqueados = true;
                    break;
                }

                if (primero.IsCompleted || segundo.IsCompleted) break;
                await Task.Delay(100);
            }

            Assert.True(ambosBloqueados,
                "Los dos importadores deben esperar el advisory lock ANTES de consultar SKU.");
        }
        finally
        {
            // Liberar la transacción incluso cuando una aserción falla.
            await transaccionBloqueo.CommitAsync();
        }

        var resultados = await Task.WhenAll(primero, segundo)
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(resultados, r =>
        {
            Assert.Equal(1, r.TotalProcesados);
            Assert.Equal(1, r.Exitosos);
            Assert.Equal(0, r.Errores);
        });

        await using var verificar = NewContext();
        var producto = await verificar.Productos.AsNoTracking().SingleAsync();
        Assert.Equal("CONCURRENT-001", producto.Sku);
        Assert.Equal(1, await verificar.Productos.CountAsync());
        Assert.Equal(1, await verificar.StockFisico.CountAsync());
        Assert.Equal(1, await verificar.UbicacionesAlmacen.CountAsync());
        var stock = await verificar.StockFisico.AsNoTracking().SingleAsync();
        Assert.Equal(40, stock.CantidadTotal);
        Assert.Equal(0, stock.CantidadReservada);
        Assert.Equal(0, stock.CantidadBloqueada);
        Assert.Equal(producto.Id, stock.ProductoId);
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null ||
            !_database.StartsWith("ci_catalog_import_", StringComparison.Ordinal)) return;

        if (_testConnection is not null)
        {
            using var pool = new NpgsqlConnection(_testConnection);
            NpgsqlConnection.ClearPool(pool);
        }

        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        _created = false;
    }
}
