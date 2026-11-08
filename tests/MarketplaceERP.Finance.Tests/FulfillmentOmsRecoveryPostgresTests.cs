using MarketplaceERP.API.Controllers;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Interfaces;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Falla de Finance después de que WMS guarda un paquete. Solo PostgreSQL 15
/// del runner, en base nueva ci_fulfillment_*; jamás conecta a Neon.
/// </summary>
public sealed class FulfillmentOmsRecoveryPostgresTests : IAsyncLifetime
{
    private readonly string _dbName = "ci_fulfillment_" + Guid.NewGuid().ToString("N");
    private string? _admin;
    private string? _ephemeral;
    private bool _created;

    public async Task InitializeAsync()
    {
        var raw = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CI requiere PostgreSQL local para pruebas OMS/WMS.");
            return;
        }

        var cs = new NpgsqlConnectionStringBuilder(raw);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(
                cs.Host, StringComparer.OrdinalIgnoreCase) ||
            cs.Port != 5432 || cs.Database != "postgres" ||
            cs.Username != "postgres" || string.IsNullOrEmpty(cs.Password))
            throw new InvalidOperationException(
                "La prueba solo admite postgres@localhost:5432/postgres, nunca Neon.");

        _admin = cs.ConnectionString;
        cs.Database = _dbName;
        _ephemeral = cs.ConnectionString;

        await using (var admin = new NpgsqlConnection(_admin))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{_dbName}\"";
            await cmd.ExecuteNonQueryAsync();
            _created = true;
        }

        await using (var finance = Finance())
            await finance.Database.MigrateAsync();
        await using (var catalog = Catalog())
            await catalog.Database.MigrateAsync();
    }

    private FinanceDbContext Finance() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_ephemeral!, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "finance"))
            .Options);

    private CatalogDbContext Catalog() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_ephemeral!, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
            .Options);

    private static FulfillmentService Service(CatalogDbContext catalog) =>
        new(catalog, Mock.Of<IStockService>(), NullLogger<FulfillmentService>.Instance);

    [Fact]
    public async Task FallaFinanceTrasEmpacar_SeDetectaDivergencia_YReintentoReusaLaMismaGuia()
    {
        if (!_created) return;

        var id = Guid.NewGuid();
        var numero = "ORD-QA-" + Guid.NewGuid().ToString("N")[..10];
        var solicitud = new EmpacarDespachoRequest(
            id, numero, 2.0m, 20m, 15m, 10m, "QA Express",
            "San Salvador", "San Salvador Centro", "Dirección de ensayo",
            "Comprador QA", "7000-0000");

        await using (var finance = Finance())
        {
            finance.Ordenes.Add(new Orden
            {
                Id = id, NumeroOrden = numero,
                CompradorId = Guid.NewGuid(), VendedorId = Guid.NewGuid(),
                Estado = EstadoOrden.EnProceso
            });
            await finance.SaveChangesAsync();
        }

        await using (var catalog = Catalog())
        {
            catalog.PickLists.Add(new PickList
            {
                Id = Guid.NewGuid(), OrdenId = id, NumeroOrden = numero,
                NumeroPickList = "PKL-QA-" + Guid.NewGuid().ToString("N")[..10],
                Estado = EstadoPicking.Completado, FechaFin = DateTime.UtcNow
            });
            await catalog.SaveChangesAsync();
        }

        // Solo en ci_fulfillment_*: simulamos un error de Finance al guardar guía.
        await SqlAsync("""
            CREATE FUNCTION finance.ci_fail_guide() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."GuiaEnvio" IS DISTINCT FROM OLD."GuiaEnvio" THEN
                    RAISE EXCEPTION 'Fallo OMS simulado tras packing WMS';
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER ci_fail_guide
              BEFORE UPDATE ON finance.ordenes
              FOR EACH ROW EXECUTE FUNCTION finance.ci_fail_guide();
            """);

        try
        {
            await using var finance = Finance();
            await using var catalog = Catalog();
            var controller = new FulfillmentController(Service(catalog), finance);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                controller.EmpacarYRotular(id, solicitud, CancellationToken.None));
        }
        finally
        {
            await SqlAsync("DROP TRIGGER IF EXISTS ci_fail_guide ON finance.ordenes");
            await SqlAsync("DROP FUNCTION IF EXISTS finance.ci_fail_guide()");
        }

        string guiaWms;
        await using (var catalog = Catalog())
        {
            var paquete = await catalog.PaquetesDespacho.AsNoTracking().SingleAsync();
            Assert.Equal(id, paquete.OrdenId);
            Assert.Equal(EstadoPacking.ListoParaDespacho, paquete.Estado);
            guiaWms = paquete.NumeroGuia;
            Assert.False(string.IsNullOrWhiteSpace(guiaWms));
        }
        await using (var finance = Finance())
        {
            var orden = await finance.Ordenes.AsNoTracking().SingleAsync();
            Assert.True(string.IsNullOrWhiteSpace(orden.GuiaEnvio));
            Assert.Equal(EstadoOrden.EnProceso, orden.Estado);
        }

        // Reintento controlado: el servicio ya reutiliza el paquete si existe.
        await using (var finance = Finance())
        await using (var catalog = Catalog())
        {
            var controller = new FulfillmentController(Service(catalog), finance);
            var response = await controller.EmpacarYRotular(id, solicitud, CancellationToken.None);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response);
        }

        await using (var finance = Finance())
        await using (var catalog = Catalog())
        {
            Assert.Equal(1, await catalog.PaquetesDespacho.CountAsync());
            Assert.Equal(guiaWms, (await catalog.PaquetesDespacho.AsNoTracking().SingleAsync()).NumeroGuia);
            Assert.Equal(guiaWms, (await finance.Ordenes.AsNoTracking().SingleAsync()).GuiaEnvio);
        }
    }

    [Fact]
    public async Task FallaFinanceTrasGenerarPickList_ReintentoNoDebeDuplicarLista()
    {
        if (!_created) return;

        var ordenId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        var numero = "ORD-PICK-" + Guid.NewGuid().ToString("N")[..10];
        const string sku = "SKU-PICK-QA";
        const string ubicacionCodigo = "PAS-A-RCK-01-NIV-01-POS-01";

        await using (var finance = Finance())
        {
            finance.Ordenes.Add(new Orden
            {
                Id = ordenId, NumeroOrden = numero, Estado = EstadoOrden.Aprobada,
                CompradorId = Guid.NewGuid(), VendedorId = Guid.NewGuid(),
                Items = new List<ItemOrden>
                {
                    new()
                    {
                        Id = Guid.NewGuid(), OrdenId = ordenId,
                        ProductoId = productoId, SkuReferencia = sku,
                        NombreProducto = "Producto test picking", Cantidad = 2,
                        PrecioUnitario = 25m
                    }
                }
            });
            await finance.SaveChangesAsync();
        }

        await using (var catalog = Catalog())
        {
            var producto = new Producto
            {
                Id = productoId, Sku = sku, Nombre = "Producto test picking",
                CategoriaNombre = "Electrónica y Tecnología", Subcategoria = "Pruebas",
                PrecioB2C = 25m, PrecioB2B = 20m, StockInicial = 10, StockMinimo = 2,
                ProveedorNombre = "QA", PesoKg = 1m, DimensionesCm = "10x10x10",
                UnidadMedida = "Unidad", EstadoProducto = "Activo", CodigoEAN = null,
                Descripcion = "Prueba WMS", VendedorId = Guid.NewGuid(),
                CategoriaId = Guid.NewGuid()
            };
            var ubicacion = new UbicacionAlmacen
            {
                Id = Guid.NewGuid(), Codigo = ubicacionCodigo, Pasillo = "A",
                Rack = "01", Nivel = 1, Posicion = "01", EstaOcupada = true
            };
            catalog.Productos.Add(producto);
            catalog.UbicacionesAlmacen.Add(ubicacion);
            catalog.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productoId, UbicacionAlmacenId = ubicacion.Id,
                UbicacionCodigo = ubicacionCodigo, CantidadTotal = 10
            });
            await catalog.SaveChangesAsync();

            var reservas = new StockService(catalog, NullLogger<StockService>.Instance);
            var reserva = await reservas.ReservarStockAsync(productoId, 2, ordenId);
            Assert.True(reserva.Exitoso, reserva.MensajeError);
            await reservas.ConfirmarReservaAsync(reserva.ReservaId);
        }

        await SqlAsync("""
            CREATE FUNCTION finance.ci_fail_pick() RETURNS trigger
            LANGUAGE plpgsql AS $pick$
            BEGIN
                IF NEW."Estado" = 'EnProceso' AND OLD."Estado" = 'Aprobada' THEN
                    RAISE EXCEPTION 'Fallo OMS simulado tras guardar PickList WMS';
                END IF;
                RETURN NEW;
            END $pick$;
            CREATE TRIGGER ci_fail_pick
              BEFORE UPDATE ON finance.ordenes
              FOR EACH ROW EXECUTE FUNCTION finance.ci_fail_pick();
            """);

        try
        {
            await using var finance = Finance();
            await using var catalog = Catalog();
            var controller = new FulfillmentController(Service(catalog), finance);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                controller.GenerarPickList(ordenId, "Operador QA", CancellationToken.None));
        }
        finally
        {
            await SqlAsync("DROP TRIGGER IF EXISTS ci_fail_pick ON finance.ordenes");
            await SqlAsync("DROP FUNCTION IF EXISTS finance.ci_fail_pick()");
        }

        await using (var finance = Finance())
        await using (var catalog = Catalog())
        {
            Assert.Equal(EstadoOrden.Aprobada,
                (await finance.Ordenes.AsNoTracking().SingleAsync()).Estado);
            Assert.Equal(1, await catalog.PickLists.CountAsync());
            Assert.Equal(2, (await catalog.PickLists.Include(p => p.Items).SingleAsync())
                .Items.Sum(i => i.CantidadSolicitada));
        }

        // El estado OMS sigue Aprobada; la solicitud se reintenta.
        // Debe reconciliar la primera PickList, no emitir otra.
        await using (var finance = Finance())
        await using (var catalog = Catalog())
        {
            var controller = new FulfillmentController(Service(catalog), finance);
            var response = await controller.GenerarPickList(ordenId, "Operador QA", CancellationToken.None);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response);
        }
        await using (var finance = Finance())
        await using (var catalog = Catalog())
        {
            Assert.Equal(1, await catalog.PickLists.CountAsync());
            Assert.Equal(EstadoOrden.EnProceso,
                (await finance.Ordenes.AsNoTracking().SingleAsync()).Estado);
            var stock = await catalog.StockFisico.AsNoTracking().SingleAsync();
            Assert.Equal(10, stock.CantidadTotal);
            Assert.Equal(2, stock.CantidadReservada);
            Assert.Equal(0, await catalog.MovimientosInventario
                .CountAsync(m => m.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound));
        }
    }

    [Fact]
    public async Task FallaFinanceTrasDespacho_RetryNoDescuentaInventarioOtraVez()
    {
        if (!_created) return;

        var ordenId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        var numero = "ORD-DESP-" + Guid.NewGuid().ToString("N")[..10];
        const string sku = "SKU-DESP-QA";
        const string codigo = "PAS-B-RCK-01-NIV-01-POS-01";
        Guid paqueteId;
        string guia;

        await using (var catalog = Catalog())
        {
            catalog.Productos.Add(new Producto
            {
                Id = productoId, Sku = sku, Nombre = "Producto prueba despacho",
                CategoriaNombre = "Ropa y Accesorios", Subcategoria = "QA",
                PrecioB2C = 25m, PrecioB2B = 20m, StockInicial = 10,
                StockMinimo = 2, ProveedorNombre = "Proveedor QA",
                PaisOrigen = "El Salvador", PesoKg = 1m,
                DimensionesCm = "10x10x10", UnidadMedida = "Unidad",
                EstadoProducto = "Activo", Descripcion = "Prueba de despacho",
                VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid()
            });
            var ubicacionId = Guid.NewGuid();
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = ubicacionId, Codigo = codigo, Pasillo = "B", Rack = "01",
                Nivel = 1, Posicion = "01", EstaOcupada = true
            });
            catalog.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productoId,
                UbicacionAlmacenId = ubicacionId, UbicacionCodigo = codigo,
                CantidadTotal = 10
            });
            await catalog.SaveChangesAsync();

            var stockService = new StockService(catalog, NullLogger<StockService>.Instance);
            var reserva = await stockService.ReservarStockAsync(productoId, 2, ordenId);
            Assert.True(reserva.Exitoso, reserva.MensajeError);
            await stockService.ConfirmarReservaAsync(reserva.ReservaId);

            var fulfillment = new FulfillmentService(catalog, stockService,
                NullLogger<FulfillmentService>.Instance);
            var pick = await fulfillment.GenerarPickListAsync(
                new GenerarPickListRequest(ordenId, numero, "QA", new List<ItemPickingSolicitud>
                {
                    new(productoId, sku, "Producto prueba despacho", 2)
                }));
            await fulfillment.ConfirmarPickingAsync(pick.Id,
                pick.Items.Select(i =>
                    new ConfirmarItemPickingRequest(i.Id, i.CantidadSolicitada)).ToList());
            var paquete = await fulfillment.EmpacarYRotularAsync(new EmpacarDespachoRequest(
                ordenId, numero, 1m, 20m, 10m, 10m, "QA Express",
                "San Salvador", "San Salvador Centro", "Dirección prueba",
                "Receptor QA", "7000-0000"));
            paqueteId = paquete.Id;
            guia = paquete.NumeroGuia;
        }

        await using (var finance = Finance())
        {
            finance.Ordenes.Add(new Orden
            {
                Id = ordenId, NumeroOrden = numero, Estado = EstadoOrden.EnProceso,
                CompradorId = Guid.NewGuid(), VendedorId = Guid.NewGuid(),
                GuiaEnvio = guia
            });
            await finance.SaveChangesAsync();
        }

        await SqlAsync("""
            CREATE FUNCTION finance.ci_fail_ship() RETURNS trigger
            LANGUAGE plpgsql AS $ship$
            BEGIN
                IF NEW."Estado" = 'Enviada' AND OLD."Estado" = 'EnProceso' THEN
                    RAISE EXCEPTION 'Fallo OMS simulado después de consumir stock WMS';
                END IF;
                RETURN NEW;
            END $ship$;
            CREATE TRIGGER ci_fail_ship
              BEFORE UPDATE ON finance.ordenes
              FOR EACH ROW EXECUTE FUNCTION finance.ci_fail_ship();
            """);
        try
        {
            await using var catalog = Catalog();
            await using var finance = Finance();
            var stock = new StockService(catalog, NullLogger<StockService>.Instance);
            var fulfillment = new FulfillmentService(catalog, stock,
                NullLogger<FulfillmentService>.Instance);
            var controller = new FulfillmentController(fulfillment, finance);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                controller.DespacharPaquete(paqueteId, CancellationToken.None));
        }
        finally
        {
            await SqlAsync("DROP TRIGGER IF EXISTS ci_fail_ship ON finance.ordenes");
            await SqlAsync("DROP FUNCTION IF EXISTS finance.ci_fail_ship()");
        }

        await using (var catalog = Catalog())
        await using (var finance = Finance())
        {
            var paquete = await catalog.PaquetesDespacho.AsNoTracking().SingleAsync();
            Assert.Equal(EstadoPacking.Despachado, paquete.Estado);
            var stock = await catalog.StockFisico.AsNoTracking().SingleAsync();
            Assert.Equal(8, stock.CantidadTotal);
            Assert.Equal(0, stock.CantidadReservada);
            Assert.Equal(1, await catalog.MovimientosInventario.CountAsync(m =>
                m.OrdenId == ordenId &&
                m.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound));
            Assert.Equal(EstadoOrden.EnProceso,
                (await finance.Ordenes.AsNoTracking().SingleAsync()).Estado);
        }

        // Reintento: el paquete ya despachado se reutiliza; no consumir 2 veces.
        await using (var catalog = Catalog())
        await using (var finance = Finance())
        {
            var stock = new StockService(catalog, NullLogger<StockService>.Instance);
            var controller = new FulfillmentController(
                new FulfillmentService(catalog, stock, NullLogger<FulfillmentService>.Instance),
                finance);
            var result = await controller.DespacharPaquete(paqueteId, CancellationToken.None);
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        }

        await using (var catalog = Catalog())
        await using (var finance = Finance())
        {
            Assert.Equal(1, await catalog.PaquetesDespacho.CountAsync());
            Assert.Equal(8, (await catalog.StockFisico.AsNoTracking().SingleAsync()).CantidadTotal);
            Assert.Equal(1, await catalog.MovimientosInventario.CountAsync(m =>
                m.OrdenId == ordenId &&
                m.TipoMovimiento == TipoMovimientoInventario.DespachoOutbound));
            Assert.Equal(EstadoOrden.Enviada,
                (await finance.Ordenes.AsNoTracking().SingleAsync()).Estado);
        }
    }

    [Fact]
    public async Task DosPickListsConcurrentes_ParaLaMismaOrden_SoloCreaUnaEnPostgres()
    {
        if (!_created) return;

        var ordenId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        const string sku = "SKU-PICK-CONC-QA";
        const string ubicacion = "PAS-C-RCK-01-NIV-01-POS-01";
        const string numeroOrden = "ORD-PICK-CONC-QA";

        await using (var catalog = Catalog())
        {
            catalog.Productos.Add(new Producto
            {
                Id = productoId, Sku = sku, Nombre = "Prueba picking concurrente",
                CategoriaNombre = "Alimentos y Bebidas", Subcategoria = "Pruebas",
                PrecioB2C = 20m, PrecioB2B = 15m, StockInicial = 10, StockMinimo = 1,
                ProveedorNombre = "QA", PaisOrigen = "El Salvador", PesoKg = 1m,
                DimensionesCm = "10x10x10", UnidadMedida = "Unidad",
                EstadoProducto = "Activo", Descripcion = "Ensayo concurrente",
                VendedorId = Guid.NewGuid(), CategoriaId = Guid.NewGuid()
            });
            var ubicacionId = Guid.NewGuid();
            catalog.UbicacionesAlmacen.Add(new UbicacionAlmacen
            {
                Id = ubicacionId, Codigo = ubicacion, Pasillo = "C",
                Rack = "01", Nivel = 1, Posicion = "01", EstaOcupada = true
            });
            catalog.StockFisico.Add(new StockFisico
            {
                Id = Guid.NewGuid(), ProductoId = productoId,
                UbicacionAlmacenId = ubicacionId, UbicacionCodigo = ubicacion,
                CantidadTotal = 10
            });
            await catalog.SaveChangesAsync();
            var reservas = new StockService(catalog, NullLogger<StockService>.Instance);
            var reserva = await reservas.ReservarStockAsync(productoId, 2, ordenId);
            Assert.True(reserva.Exitoso, reserva.MensajeError);
            await reservas.ConfirmarReservaAsync(reserva.ReservaId);
        }

        var llave = BitConverter.ToInt64(ordenId.ToByteArray(), 0);
        await using var connection = new NpgsqlConnection(_ephemeral!);
        await connection.OpenAsync();
        await using var hold = await connection.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key)", connection, hold))
        {
            cmd.Parameters.AddWithValue("key", llave);
            await cmd.ExecuteNonQueryAsync();
        }

        async Task<PickList> GenerarAsync()
        {
            await using var db = Catalog();
            var service = Service(db);
            return await service.GenerarPickListAsync(new GenerarPickListRequest(
                ordenId, numeroOrden, "Operador QA",
                new List<ItemPickingSolicitud>
                {
                    new(productoId, sku, "Prueba picking concurrente", 2)
                }));
        }

        var primero = Task.Run(GenerarAsync);
        var segundo = Task.Run(GenerarAsync);
        try
        {
            var dosEsperando = false;
            for (var intento = 0; intento < 100; intento++)
            {
                await using var count = new NpgsqlCommand("""
                    SELECT count(*) FROM pg_locks
                    WHERE locktype = 'advisory' AND NOT granted
                      AND database = (
                        SELECT oid FROM pg_database WHERE datname = current_database()
                      )
                    """, connection, hold);
                var esperando = (long)(await count.ExecuteScalarAsync())!;
                if (esperando >= 2)
                {
                    dosEsperando = true;
                    break;
                }
                if (primero.IsCompleted || segundo.IsCompleted) break;
                await Task.Delay(100);
            }
            Assert.True(dosEsperando,
                "Ambas solicitudes deben esperar el bloqueo por orden antes de consultar WMS.");
        }
        finally
        {
            await hold.CommitAsync();
        }

        var resultado = await Task.WhenAll(primero, segundo)
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(resultado[0].Id, resultado[1].Id);
        Assert.Equal(ordenId, resultado[0].OrdenId);

        await using var verify = Catalog();
        Assert.Equal(1, await verify.PickLists.CountAsync(x => x.OrdenId == ordenId));
        Assert.Equal(2, (await verify.PickLists.Include(x => x.Items)
            .SingleAsync(x => x.OrdenId == ordenId)).Items.Sum(x => x.CantidadSolicitada));
        var stock = await verify.StockFisico.AsNoTracking().SingleAsync();
        Assert.Equal(10, stock.CantidadTotal);
        Assert.Equal(2, stock.CantidadReservada);
    }

    private async Task SqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_ephemeral!);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_created || _admin is null ||
            !_dbName.StartsWith("ci_fulfillment_", StringComparison.Ordinal)) return;

        if (_ephemeral is not null)
        {
            using var pool = new NpgsqlConnection(_ephemeral);
            NpgsqlConnection.ClearPool(pool);
        }
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
        _created = false;
    }
}
