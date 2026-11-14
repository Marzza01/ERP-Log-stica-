using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MarketplaceERP.Catalog.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// Test de concurrencia física WMS en PostgreSQL LOCAL. Nunca consulta Neon,
/// Render ni producción; crea y destruye únicamente su base efímera qa_wms_*.
/// </summary>
public sealed class WmsReferenciasUnicasPostgresTests : IAsyncLifetime
{
    private readonly string _database = "qa_wms_" + Guid.NewGuid().ToString("N")[..16];
    private string? _adminConnection;
    private string? _qaConnection;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CI requiere PostgreSQL LOCAL para WMS.");
            return;
        }

        var options = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }
                .Contains(options.Host, StringComparer.OrdinalIgnoreCase)
            || options.Port != 5432 || options.Database != "postgres"
            || options.Username != "postgres" || string.IsNullOrEmpty(options.Password))
            throw new InvalidOperationException(
                "WMS QA solo acepta postgres@localhost:5432/postgres.");

        _adminConnection = options.ConnectionString;
        options.Database = _database;
        options.Pooling = false;
        _qaConnection = options.ConnectionString;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", admin);
        await cmd.ExecuteNonQueryAsync();
        _created = true;
    }

    private CatalogDbContext NewDb() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_qaConnection!,
                x => x.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
            .Options);

    private static PickList Pick(Guid id, string number, Guid? ordenId = null) => new()
    {
        Id = id, OrdenId = ordenId ?? Guid.NewGuid(), NumeroOrden = "ORD-QA-" + Guid.NewGuid().ToString("N"),
        NumeroPickList = number, OperadorAsignado = "QA"
    };

    private static PaqueteDespacho Package(Guid id, string number, Guid? ordenId = null) => new()
    {
        Id = id, OrdenId = ordenId ?? Guid.NewGuid(), NumeroOrden = "ORD-QA-" + Guid.NewGuid().ToString("N"),
        NumeroGuia = number, Transportista = "QA"
    };

    private async Task<bool> HasIndexAsync(string name)
    {
        await using var connection = new NpgsqlConnection(_qaConnection);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM pg_indexes
                WHERE schemaname='catalog' AND indexname=@name
                  AND indexdef LIKE '%UNIQUE INDEX%'
            )
            """, connection);
        cmd.Parameters.AddWithValue("name", name);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public void ModeloEFNoTieneDiferenciasPendientesConElSnapshotDeMigraciones()
    {
        if (!_created) return;
        using var db = NewDb();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task MigracionWmsCreaIndicesYConcurrenciaDistintaConservaTodos()
    {
        if (!_created) return;
        await using (var db = NewDb()) await db.Database.MigrateAsync();

        Assert.True(await HasIndexAsync("UX_WMS_PickList_NumeroPickList"));
        Assert.True(await HasIndexAsync("UX_WMS_Paquete_NumeroGuia"));
        Assert.True(await HasIndexAsync("UX_WMS_PickList_OrdenId"));
        Assert.True(await HasIndexAsync("UX_WMS_Paquete_OrdenId"));

        // Distintas ordenes creadas en paralelo, con identidad determinista.
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            var pickId = Guid.NewGuid();
            var packageId = Guid.NewGuid();
            await using var db = NewDb();
            db.PickLists.Add(Pick(pickId, $"PKL-20261109-{pickId:N}"));
            db.PaquetesDespacho.Add(Package(packageId, $"SV-LOGI-20261109-{packageId:N}"));
            await db.SaveChangesAsync();
        }));

        await using var verify = NewDb();
        Assert.Equal(12, await verify.PickLists.CountAsync());
        Assert.Equal(12, await verify.PaquetesDespacho.CountAsync());
        Assert.Equal(12, await verify.PickLists.Select(x => x.NumeroPickList).Distinct().CountAsync());
        Assert.Equal(12, await verify.PaquetesDespacho.Select(x => x.NumeroGuia).Distinct().CountAsync());
    }

    private async Task<bool> TryInsertAsync(bool pick, string number)
    {
        await using var db = NewDb();
        if (pick) db.PickLists.Add(Pick(Guid.NewGuid(), number));
        else db.PaquetesDespacho.Add(Package(Guid.NewGuid(), number));
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException e) when (
            e.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            return false;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PostgreSqlBloqueaMismoNumeroEnDosTransaccionesParalelas(bool pick)
    {
        if (!_created) return;
        await using (var db = NewDb()) await db.Database.MigrateAsync();

        string same = (pick ? "PKL-" : "SV-LOGI-") + "DUP-" + Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(
            TryInsertAsync(pick, same),
            TryInsertAsync(pick, same));
        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, results.Count(x => !x));

        await using var verify = NewDb();
        if (pick) Assert.Equal(1, await verify.PickLists.CountAsync());
        else Assert.Equal(1, await verify.PaquetesDespacho.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MigracionFallaCerradoAnteReferenciasHistoricasDuplicadas(bool pick)
    {
        if (!_created) return;
        await using (var db = NewDb())
            db.GetService<IMigrator>().Migrate("20261106005614_CodigoEanOpcional");

        await using (var db = NewDb())
        {
            string dup = "LEGACY-DUP-" + Guid.NewGuid().ToString("N");
            if (pick)
                db.PickLists.AddRange(Pick(Guid.NewGuid(), dup), Pick(Guid.NewGuid(), dup));
            else
                db.PaquetesDespacho.AddRange(
                    Package(Guid.NewGuid(), dup), Package(Guid.NewGuid(), dup));
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => db.Database.MigrateAsync());
            Assert.Contains(pick ? "PickLists" : "paquetes", ex.ToString());
        }

        Assert.False(await HasIndexAsync("UX_WMS_PickList_NumeroPickList"));
        Assert.False(await HasIndexAsync("UX_WMS_Paquete_NumeroGuia"));
        await using var verify = NewDb();
        if (pick) Assert.Equal(2, await verify.PickLists.CountAsync());
        else Assert.Equal(2, await verify.PaquetesDespacho.CountAsync());
    }

    [Fact]
    public async Task EmpaqueSimultaneoDeMismaOrden_DevuelveMismoPaqueteYGuia()
    {
        if (!_created) return;
        await using (var init = NewDb())
        {
            await init.Database.MigrateAsync();
        }

        var ordenId = Guid.NewGuid();
        var numeroOrden = "ORD-QA-" + ordenId.ToString("N");
        await using (var seed = NewDb())
        {
            seed.PickLists.Add(new PickList
            {
                Id = Guid.NewGuid(),
                OrdenId = ordenId,
                NumeroOrden = numeroOrden,
                NumeroPickList = "PKL-QA-" + Guid.NewGuid().ToString("N"),
                Estado = EstadoPicking.Completado,
                OperadorAsignado = "QA",
                FechaFin = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var request = new EmpacarDespachoRequest(
            ordenId, numeroOrden, 2m, 20m, 10m, 8m, "Transportista QA",
            "San Salvador", "Centro", "Direccion QA", "Cliente QA", "7000-0000");
        var lockKey = BitConverter.ToInt64(ordenId.ToByteArray(), 0);

        // Retener fuera del servicio el candado para obligar a que ambas
        // transacciones concurrentes lo soliciten antes de continuar.
        await using var blocker = new NpgsqlConnection(_qaConnection);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key)", blocker, transaction))
        {
            acquire.Parameters.AddWithValue("key", lockKey);
            await acquire.ExecuteNonQueryAsync();
        }

        async Task<PaqueteDespacho> EmpacarEnContextoSeparado()
        {
            await using var db = NewDb();
            var service = new FulfillmentService(db,
                new StockService(db, NullLogger<StockService>.Instance),
                NullLogger<FulfillmentService>.Instance);
            return await service.EmpacarYRotularAsync(request);
        }

        // Ejecutar dos tareas independientes; cada una crea su contexto,
        // conexión y transacción. No utilizar DbContext compartido.
        var readyCount = 0;
        var bothStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PaqueteDespacho> StartWorker() => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCount) == 2)
                bothStarted.TrySetResult(true);
            return await EmpacarEnContextoSeparado();
        });
        var first = StartWorker();
        var second = StartWorker();

        try
        {
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(12));
            await using var inspector = new NpgsqlConnection(_qaConnection);
            await inspector.OpenAsync();
            var waiting = 0;
            for (int i = 0; i < 80; i++)
            {
                await using var query = new NpgsqlCommand("""
                    SELECT count(*) FROM pg_locks
                    WHERE locktype = 'advisory' AND granted = false
                      AND database = (SELECT oid FROM pg_database
                                      WHERE datname = current_database())
                    """, inspector);
                waiting = Convert.ToInt32(await query.ExecuteScalarAsync());
                if (waiting >= 2) break;
                await Task.Delay(100);
            }
            Assert.Equal(2, waiting);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            await transaction.RollbackAsync(); // libera el lock aun si falla
        }

        var output = await Task.WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(output[0].Id, output[1].Id);
        Assert.Equal(output[0].NumeroGuia, output[1].NumeroGuia);
        Assert.Equal(ordenId, output[0].OrdenId);

        await using var verify = NewDb();
        var saved = Assert.Single(await verify.PaquetesDespacho
            .AsNoTracking().ToListAsync());
        Assert.Equal(output[0].Id, saved.Id);
        Assert.Equal(output[0].NumeroGuia, saved.NumeroGuia);
        Assert.Equal($"SV-LOGI-{saved.FechaEmpaque:yyyyMMdd}-{saved.Id:N}",
            saved.NumeroGuia);
    }

    [Fact]
    public async Task ReintentoEmpaqueConDireccionDiferente_DebeRechazarSinAlterarPaquete()
    {
        if (!_created) return;
        await using (var init = NewDb())
            await init.Database.MigrateAsync();

        var ordenId = Guid.NewGuid();
        var numeroOrden = "ORD-RETRY-" + ordenId.ToString("N");
        await using (var seed = NewDb())
        {
            seed.PickLists.Add(new PickList
            {
                Id = Guid.NewGuid(),
                OrdenId = ordenId,
                NumeroOrden = numeroOrden,
                NumeroPickList = "PKL-RETRY-" + Guid.NewGuid().ToString("N"),
                Estado = EstadoPicking.Completado,
                OperadorAsignado = "QA",
                FechaFin = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var original = new EmpacarDespachoRequest(
            ordenId, numeroOrden, 2m, 20m, 10m, 8m, "Transportista QA",
            "San Salvador", "Centro", "Direccion original QA", "Cliente QA", "7000-0000");

        Guid paqueteId;
        string guia;
        await using (var db = NewDb())
        {
            var service = new FulfillmentService(db,
                new StockService(db, NullLogger<StockService>.Instance),
                NullLogger<FulfillmentService>.Instance);
            var saved = await service.EmpacarYRotularAsync(original);
            paqueteId = saved.Id;
            guia = saved.NumeroGuia;
        }

        await using (var retryDb = NewDb())
        {
            var service = new FulfillmentService(retryDb,
                new StockService(retryDb, NullLogger<StockService>.Instance),
                NullLogger<FulfillmentService>.Instance);
            var changed = original with { DireccionEntrega = "Direccion distinta no autorizada" };
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.EmpacarYRotularAsync(changed));
            Assert.Contains("datos de empaque distintos", ex.Message);
        }

        await using var verify = NewDb();
        var persisted = Assert.Single(await verify.PaquetesDespacho.AsNoTracking().ToListAsync());
        Assert.Equal(paqueteId, persisted.Id);
        Assert.Equal(guia, persisted.NumeroGuia);
        Assert.Equal("Direccion original QA", persisted.DireccionEntrega);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MigracionDetieneDosDocumentosMismaOrdenPeroNumerosDistintos(bool pick)
    {
        if (!_created) return;
        await using (var oldDb = NewDb())
            oldDb.GetService<IMigrator>().Migrate(
                "20261106005614_CodigoEanOpcional");

        var ordenId = Guid.NewGuid();
        await using (var insert = NewDb())
        {
            if (pick)
                insert.PickLists.AddRange(
                    Pick(Guid.NewGuid(), "PKL-A-" + Guid.NewGuid().ToString("N"), ordenId),
                    Pick(Guid.NewGuid(), "PKL-B-" + Guid.NewGuid().ToString("N"), ordenId));
            else
                insert.PaquetesDespacho.AddRange(
                    Package(Guid.NewGuid(), "SV-A-" + Guid.NewGuid().ToString("N"), ordenId),
                    Package(Guid.NewGuid(), "SV-B-" + Guid.NewGuid().ToString("N"), ordenId));
            await insert.SaveChangesAsync();
        }

        await using (var migrate = NewDb())
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(
                () => migrate.Database.MigrateAsync());
            Assert.Contains("OrdenId", failure.ToString());
        }

        Assert.False(await HasIndexAsync("UX_WMS_PickList_OrdenId"));
        Assert.False(await HasIndexAsync("UX_WMS_Paquete_OrdenId"));
        await using var verify = NewDb();
        if (pick) Assert.Equal(2, await verify.PickLists.CountAsync());
        else Assert.Equal(2, await verify.PaquetesDespacho.CountAsync());
    }

    [Fact]
    public async Task DespachoSimultaneoMismaOrden_SoloConsumeUnaVezYConservaFecha()
    {
        if (!_created) return;
        await using (var migrate = NewDb()) await migrate.Database.MigrateAsync();

        var ordenId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        var numero = "ORD-DESP-RACE-" + ordenId.ToString("N")[..10];
        const string sku = "SKU-DESP-RACE-QA";
        const string codigo = "PAS-D-RCK-01-NIV-01-POS-01";
        Guid paqueteId;

        await using (var seed = NewDb())
        {
            seed.Productos.Add(new Producto
            {
                Id = productoId, Sku = sku, Nombre = "Producto concurrency QA",
                CategoriaNombre = "Ropa y Accesorios", Subcategoria = "QA",
                PrecioB2C = 25m, PrecioB2B = 20m, StockInicial = 10,
                StockMinimo = 2, ProveedorNombre = "Proveedor QA",
                PaisOrigen = "El Salvador", PesoKg = 1m,
                DimensionesCm = "10x10x10", UnidadMedida = "Unidad",
                EstadoProducto = "Activo", Descripcion = "Prueba concurrente despacho",
                VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid()
            });
            var ubicacionId = Guid.NewGuid();
            seed.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = ubicacionId, Codigo = codigo, Pasillo = "D", Rack = "01",
                Nivel = 1, Posicion = "01", EstaOcupada = true
            });
            seed.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productoId,
                UbicacionAlmacenId = ubicacionId, UbicacionCodigo = codigo,
                CantidadTotal = 10
            });
            await seed.SaveChangesAsync();

            var stock = new StockService(seed, NullLogger<StockService>.Instance);
            var reserva = await stock.ReservarStockAsync(productoId, 2, ordenId);
            Assert.True(reserva.Exitoso, reserva.MensajeError);
            await stock.ConfirmarReservaAsync(reserva.ReservaId);
            var fulfillment = new FulfillmentService(seed, stock,
                NullLogger<FulfillmentService>.Instance);
            var pick = await fulfillment.GenerarPickListAsync(
                new GenerarPickListRequest(ordenId, numero, "QA",
                    new List<ItemPickingSolicitud>
                    {
                        new(productoId, sku, "Producto concurrency QA", 2)
                    }));
            await fulfillment.ConfirmarPickingAsync(pick.Id,
                pick.Items.Select(item =>
                    new ConfirmarItemPickingRequest(item.Id, item.CantidadSolicitada)).ToList());
            var paquete = await fulfillment.EmpacarYRotularAsync(
                new EmpacarDespachoRequest(ordenId, numero, 1m, 20m, 10m, 10m,
                    "QA Express", "San Salvador", "Centro",
                    "Direccion QA", "Receptor QA", "7000-0000"));
            paqueteId = paquete.Id;
        }

        var lockKey = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
        await using var blocker = new NpgsqlConnection(_qaConnection);
        await blocker.OpenAsync();
        await using var blockerTransaction = await blocker.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key)", blocker, blockerTransaction))
        {
            acquire.Parameters.AddWithValue("key", lockKey);
            await acquire.ExecuteNonQueryAsync();
        }

        async Task<PaqueteDespacho?> Despachar()
        {
            await using var db = NewDb();
            var stock = new StockService(db, NullLogger<StockService>.Instance);
            var service = new FulfillmentService(db, stock,
                NullLogger<FulfillmentService>.Instance);
            return await service.DespacharPaqueteAsync(paqueteId);
        }

        var one = Task.Run(Despachar);
        var two = Task.Run(Despachar);
        try
        {
            await using var inspector = new NpgsqlConnection(_qaConnection);
            await inspector.OpenAsync();
            var waiting = 0;
            for (var i = 0; i < 80; i++)
            {
                await using var query = new NpgsqlCommand("""
                    SELECT count(*) FROM pg_locks
                    WHERE locktype = 'advisory' AND granted = false
                      AND database = (SELECT oid FROM pg_database
                                      WHERE datname = current_database())
                    """, inspector);
                waiting = Convert.ToInt32(await query.ExecuteScalarAsync());
                if (waiting >= 2) break;
                await Task.Delay(100);
            }
            Assert.Equal(2, waiting);
            Assert.False(one.IsCompleted);
            Assert.False(two.IsCompleted);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        var results = await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(results, r => Assert.Equal(paqueteId, r?.Id));
        Assert.NotNull(results[0]?.FechaDespacho);
        Assert.NotNull(results[1]?.FechaDespacho);
        // PostgreSQL timestamp conserva microsegundos; DateTime.UtcNow incluye
        // ticks de 100 ns, por lo que el valor transient del primer contexto
        // puede tener una fracción que el segundo lee redondeada al persistir.
        static long PgMicroseconds(DateTime? fecha) => fecha!.Value.Ticks / 10;
        Assert.Equal(PgMicroseconds(results[0]?.FechaDespacho),
            PgMicroseconds(results[1]?.FechaDespacho));

        await using var verify = NewDb();
        var saved = Assert.Single(await verify.PaquetesDespacho.AsNoTracking().ToListAsync());
        Assert.Equal(EstadoPacking.Despachado, saved.Estado);
        Assert.NotNull(saved.FechaDespacho);
        Assert.Equal(PgMicroseconds(saved.FechaDespacho),
            PgMicroseconds(results[0]?.FechaDespacho));
        var balance = Assert.Single(await verify.StockFisico.AsNoTracking().ToListAsync());
        Assert.Equal(8, balance.CantidadTotal);
        Assert.Equal(0, balance.CantidadReservada);
        var outbounds = await verify.MovimientosInventario.AsNoTracking()
            .Where(m => m.OrdenId == ordenId &&
                        m.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound)
            .ToListAsync();
        Assert.Single(outbounds);
        Assert.Equal(2, outbounds.Sum(m => m.Cantidad));
    }

    [Fact]
    public async Task ConfirmacionPickingConcurrente_NoRevierteUnaListaCompletada()
    {
        if (!_created) return;
        await using (var migrate = NewDb()) await migrate.Database.MigrateAsync();

        var ordenId = Guid.NewGuid();
        var pickId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await using (var seed = NewDb())
        {
            seed.PickLists.Add(new PickList
            {
                Id = pickId, OrdenId = ordenId,
                NumeroOrden = "ORD-PICK-RACE-" + ordenId.ToString("N"),
                NumeroPickList = "PKL-PICK-RACE-" + pickId.ToString("N"),
                OperadorAsignado = "QA", Estado = EstadoPicking.Pendiente,
                Items = new List<ItemPickList>
                {
                    new()
                    {
                        Id = itemId, PickListId = pickId, ProductoId = Guid.NewGuid(),
                        Sku = "PICK-RACE-QA", NombreProducto = "Producto QA",
                        CantidadSolicitada = 2, CantidadRecogida = 0,
                        Pasillo = "A", Rack = "01", Nivel = 1, Posicion = "01",
                        UbicacionCodigo = "PAS-A-RCK-01-NIV-01-POS-01"
                    }
                }
            });
            await seed.SaveChangesAsync();
        }

        var lockKey = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
        await using var blocker = new NpgsqlConnection(_qaConnection);
        await blocker.OpenAsync();
        await using var blockTx = await blocker.BeginTransactionAsync();
        await using (var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key)", blocker, blockTx))
        {
            acquire.Parameters.AddWithValue("key", lockKey);
            await acquire.ExecuteNonQueryAsync();
        }

        async Task<bool> Confirmar(int cantidad)
        {
            await using var db = NewDb();
            var service = new FulfillmentService(db,
                new StockService(db, NullLogger<StockService>.Instance),
                NullLogger<FulfillmentService>.Instance);
            try
            {
                await service.ConfirmarPickingAsync(pickId,
                    new List<ConfirmarItemPickingRequest>
                    {
                        new(itemId, cantidad)
                    });
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var completa = Task.Run(() => Confirmar(2));
        var parcial = Task.Run(() => Confirmar(1));
        try
        {
            await using var inspector = new NpgsqlConnection(_qaConnection);
            await inspector.OpenAsync();
            var waiting = 0;
            for (var i = 0; i < 80; i++)
            {
                await using var q = new NpgsqlCommand("""
                    SELECT count(*) FROM pg_locks
                    WHERE locktype = 'advisory' AND granted = false
                      AND database = (SELECT oid FROM pg_database
                                      WHERE datname = current_database())
                    """, inspector);
                waiting = Convert.ToInt32(await q.ExecuteScalarAsync());
                if (waiting >= 2) break;
                await Task.Delay(100);
            }
            Assert.Equal(2, waiting);
            Assert.False(completa.IsCompleted);
            Assert.False(parcial.IsCompleted);
        }
        finally
        {
            await blockTx.RollbackAsync();
        }

        var resultado = await Task.WhenAll(completa, parcial).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Contains(true, resultado);

        await using var verify = NewDb();
        var saved = Assert.Single(await verify.PickLists.Include(p => p.Items)
            .AsNoTracking().ToListAsync());
        Assert.Equal(EstadoPicking.Completado, saved.Estado);
        Assert.NotNull(saved.FechaFin);
        var savedItem = Assert.Single(saved.Items);
        Assert.Equal(2, savedItem.CantidadRecogida);
    }

    public async Task DisposeAsync()
    {
        if (!_created || _adminConnection is null) return;
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", admin);
        await cmd.ExecuteNonQueryAsync();
    }
}
