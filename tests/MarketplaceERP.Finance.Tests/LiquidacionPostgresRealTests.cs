/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Data;
using FluentAssertions;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Tests para Liquidaciones sin pago doble contra PostgreSQL real.
/// Valida transacciones, exclusión mutua mediante FOR UPDATE / xmin, y migración de datos.
/// Requiere TEST_PG_CONNECTION (ej: "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=xxx").
/// Si no está definida en el entorno, los tests se omiten limpiamente.
/// </summary>
[Collection("PostgresRealTests")]
public class LiquidacionPostgresRealTests : IAsyncLifetime
{
    private static readonly string? RawConnStr = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
    private readonly string _dbName = $"test_liq_{Guid.NewGuid():N}";
    private string _adminConnStr = "";
    private string _testConnStr = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(RawConnStr))
        {
            // Skip.If se invoca en cada test; InitializeAsync no hace nada si no hay conexión.
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(RawConnStr);
        builder.Database = "postgres";
        _adminConnStr = builder.ConnectionString;

        builder.Database = _dbName;
        builder.IncludeErrorDetail = true;
        _testConnStr = builder.ConnectionString;

        // Crear base de datos aislada para esta ejecución
        await using var adminConn = new NpgsqlConnection(_adminConnStr);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_dbName}\";";
        await cmd.ExecuteNonQueryAsync();

        // Aplicar migraciones completas (InitialCreate + LiquidacionUnoAMuchosSplits)
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnStr)
            .Options;

        await using var context = new FinanceDbContext(options);
        await context.Database.MigrateAsync();
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

    private FinanceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnStr)
            .Options;
        return new FinanceDbContext(options);
    }

    private async Task<(Guid vendedorId, List<SplitPago> splits)> CrearSplitsPendientes(FinanceDbContext db, int cantidad, decimal montoVendedorUnitario)
    {
        var vendedorId = Guid.NewGuid();
        var splits = new List<SplitPago>();

        for (int i = 0; i < cantidad; i++)
        {
            var orden = new Orden
            {
                Id = Guid.NewGuid(),
                NumeroOrden = $"ORD-TEST-{Guid.NewGuid():N}"[..25],
                CompradorId = Guid.NewGuid(),
                VendedorId = vendedorId,
                MontoTotal = montoVendedorUnitario / 0.9m,
                SubTotal = montoVendedorUnitario / 0.9m,
                Estado = EstadoOrden.Entregada,
                FechaCreacion = DateTime.UtcNow
            };
            db.Ordenes.Add(orden);

            var split = new SplitPago
            {
                Id = Guid.NewGuid(),
                OrdenId = orden.Id,
                MontoTotal = orden.MontoTotal,
                MontoVendedor = montoVendedorUnitario,
                MontoMarketplace = orden.MontoTotal - montoVendedorUnitario,
                PorcentajeVendedor = 90m,
                PorcentajeMarketplace = 10m,
                TransaccionId = $"TX-{Guid.NewGuid():N}"[..20],
                LiquidacionId = null
            };
            db.SplitsPago.Add(split);
            splits.Add(split);
        }

        await db.SaveChangesAsync();
        return (vendedorId, splits);
    }

    [SkippableFact]
    public async Task Test_A_TresSplitsDeUnVendedor_GeneraUnaLiquidacionConSumaTotal_YTodosLosSplitsMarcados()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no definida; se requiere PostgreSQL real.");

        await using var db = CreateDbContext();
        var (vendedorId, splits) = await CrearSplitsPendientes(db, 3, 100m);

        var contabilidadMock = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var service = new LiquidacionService(db, contabilidadMock, NullLogger<LiquidacionService>.Instance);

        // Act
        var liq = await service.GenerarLiquidacionAsync(vendedorId);

        // Assert
        liq.Should().NotBeNull();
        liq.MontoLiquidar.Should().Be(300m, "debe consolidar la suma de los 3 splits ($100 * 3)");
        liq.Estado.Should().Be(EstadoLiquidacion.Pendiente);

        // Verificar en base de datos que los 3 splits ahora apuntan a la liquidación
        await using var verifyDb = CreateDbContext();
        var splitsActualizados = await verifyDb.SplitsPago
            .Where(s => s.LiquidacionId == liq.Id)
            .ToListAsync();

        splitsActualizados.Should().HaveCount(3, "todos los 3 splits deben tener el LiquidacionId asignado");
        splitsActualizados.Select(s => s.Id).Should().BeEquivalentTo(splits.Select(s => s.Id));
    }

    [SkippableFact]
    public async Task Test_B_SegundaGeneracionInmediata_LanzaExcepcion_SinCrearNuevaLiquidacion()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no definida; se requiere PostgreSQL real.");

        await using var db = CreateDbContext();
        var (vendedorId, _) = await CrearSplitsPendientes(db, 2, 50m);

        var contabilidadMock = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var service = new LiquidacionService(db, contabilidadMock, NullLogger<LiquidacionService>.Instance);

        // Primera generación exitosa
        var liq1 = await service.GenerarLiquidacionAsync(vendedorId);
        liq1.Should().NotBeNull();

        // Segunda generación inmediata para el mismo vendedor: debe fallar porque ya no hay splits pendientes
        var actSegunda = async () => await service.GenerarLiquidacionAsync(vendedorId);
        await actSegunda.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no tiene saldos pendientes*");

        // Verificar que solo existe exactamente 1 liquidación en la base de datos
        await using var verifyDb = CreateDbContext();
        var totalLiqs = await verifyDb.Liquidaciones
            .Where(l => l.VendedorId == vendedorId)
            .CountAsync();

        totalLiqs.Should().Be(1, "no se debe haber creado ninguna liquidación duplicada");
    }

    [SkippableFact]
    public async Task Test_B2_DosGenerarSimultaneos_ExactamenteUnaLiquidacionCreada_LaOtraFalla()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no definida; se requiere PostgreSQL real.");

        await using var dbInit = CreateDbContext();
        var (vendedorId, splits) = await CrearSplitsPendientes(dbInit, 3, 100m);

        await using var db1 = CreateDbContext();
        await using var db2 = CreateDbContext();

        var service1 = new LiquidacionService(db1, new ContabilidadService(db1, NullLogger<ContabilidadService>.Instance), NullLogger<LiquidacionService>.Instance);
        var service2 = new LiquidacionService(db2, new ContabilidadService(db2, NullLogger<ContabilidadService>.Instance), NullLogger<LiquidacionService>.Instance);

        // Act: Disparar GenerarLiquidacionAsync en paralelo para el mismo vendedor
        var task1 = Task.Run(async () =>
        {
            try
            {
                var l = await service1.GenerarLiquidacionAsync(vendedorId);
                return (Exito: true, Liquidacion: (Liquidacion?)l, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Exito: false, Liquidacion: (Liquidacion?)null, Error: ex);
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                var l = await service2.GenerarLiquidacionAsync(vendedorId);
                return (Exito: true, Liquidacion: (Liquidacion?)l, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Exito: false, Liquidacion: (Liquidacion?)null, Error: ex);
            }
        });

        var results = await Task.WhenAll(task1, task2);

        // Assert: Exactamente una debe tener éxito y una debe fallar
        var exitosos = results.Count(r => r.Exito);
        var fallidos = results.Count(r => !r.Exito);

        exitosos.Should().Be(1, "exactamente una generación debe completar con éxito");
        fallidos.Should().Be(1, "la otra generación simultánea debe fallar por falta de saldos o conflicto");

        var error = results.First(r => !r.Exito).Error;
        if (error is AggregateException agg) error = agg.Flatten().InnerException ?? error;
        error.Should().Match(e => e is InvalidOperationException || e is DbUpdateConcurrencyException || e is PostgresException);

        // Ningún split queda asignado a dos liquidaciones
        await using var verifyDb = CreateDbContext();
        var liquidacionesVendedor = await verifyDb.Liquidaciones
            .Where(l => l.VendedorId == vendedorId)
            .ToListAsync();
        liquidacionesVendedor.Should().HaveCount(1, "debe existir exactamente una liquidación");

        var splitsDb = await verifyDb.SplitsPago
            .Where(s => s.Orden.VendedorId == vendedorId)
            .ToListAsync();

        splitsDb.Should().OnlyContain(s => s.LiquidacionId == liquidacionesVendedor[0].Id);
    }

    [SkippableFact]
    public async Task Test_C_DosProcesarPagoSimultaneos_ExactamenteUnoCompleta_YUnoFallaPorConcurrencia_UnaSolaPartidaContable()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no definida; se requiere PostgreSQL real.");

        await using var db = CreateDbContext();
        var (vendedorId, _) = await CrearSplitsPendientes(db, 1, 200m);

        var contabilidadMock = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var service = new LiquidacionService(db, contabilidadMock, NullLogger<LiquidacionService>.Instance);

        var liq = await service.GenerarLiquidacionAsync(vendedorId);

        // Crear dos instancias de DbContext y Service independientes para simular 2 threads concurrentes
        await using var db1 = CreateDbContext();
        await using var db2 = CreateDbContext();

        var service1 = new LiquidacionService(db1, new ContabilidadService(db1, NullLogger<ContabilidadService>.Instance), NullLogger<LiquidacionService>.Instance);
        var service2 = new LiquidacionService(db2, new ContabilidadService(db2, NullLogger<ContabilidadService>.Instance), NullLogger<LiquidacionService>.Instance);

        // Act: Disparar ProcesarPagoAsync de forma simultánea
        var task1 = Task.Run(async () =>
        {
            try
            {
                await service1.ProcesarPagoAsync(liq.Id);
                return (Exito: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Exito: false, Error: ex);
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                await service2.ProcesarPagoAsync(liq.Id);
                return (Exito: true, Error: (Exception?)null);
            }
            catch (Exception ex)
            {
                return (Exito: false, Error: ex);
            }
        });

        var results = await Task.WhenAll(task1, task2);

        // Assert: Exactamente uno debe tener éxito y uno debe fallar por concurrencia o estado ya completado
        var exitosos = results.Count(r => r.Exito);
        var fallidos = results.Count(r => !r.Exito);

        exitosos.Should().Be(1, "exactamente un proceso debe completar el pago");
        fallidos.Should().Be(1, "el intento simultáneo debe ser rechazado");

        var error = results.First(r => !r.Exito).Error;
        if (error is AggregateException agg) error = agg.Flatten().InnerException ?? error;
        
        var esEsperado = error is DbUpdateConcurrencyException 
                         || error is DbUpdateException
                         || error is InvalidOperationException 
                         || error is PostgresException;
        esEsperado.Should().BeTrue($"el error debe ser de concurrencia optimista o estado previo inválido, pero fue: {error?.GetType().FullName} ({error?.Message})");

        // Verificar que solo se creó exactamente 1 partida contable de liquidación para este Id
        await using var verifyDb = CreateDbContext();
        var liqFinal = await verifyDb.Liquidaciones.FindAsync(liq.Id);
        liqFinal!.Estado.Should().Be(EstadoLiquidacion.Completada);

        var partidas = await verifyDb.PartidasContables
            .Include(p => p.Lineas)
            .Where(p => p.ReferenciaOrigenId == liq.Id)
            .ToListAsync();

        partidas.Should().HaveCount(1, "debe generarse una única partida contable en libros, sin duplicar desembolsos");
        var partida = partidas.First();
        partida.TotalDebe.Should().Be(partida.TotalHaber, "la partida debe tener Debe == Haber");
        partida.Lineas.Sum(l => l.Debe).Should().Be(partida.Lineas.Sum(l => l.Haber));
    }

    [SkippableFact]
    public async Task Test_D_MigracionDeDatos_ConservaVinculoDeLiquidacionPreexistente()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no definida; se requiere PostgreSQL real.");

        // En una base temporal, crear el esquema previo (con SplitPagoId en liquidaciones), insertar datos, y ejecutar la migración nueva
        var legacyDbName = $"test_mig_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(_adminConnStr);
        builder.Database = legacyDbName;
        builder.IncludeErrorDetail = true;
        var legacyConnStr = builder.ConnectionString;

        try
        {
            await using var adminConn = new NpgsqlConnection(_adminConnStr);
            await adminConn.OpenAsync();
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{legacyDbName}\";";
            await cmd.ExecuteNonQueryAsync();

            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseNpgsql(legacyConnStr)
                .Options;

            // 1. Aplicar solo la migración inicial "InitialCreate"
            await using (var context = new FinanceDbContext(options))
            {
                var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
                await migrator.MigrateAsync("20260925020051_InitialCreate");
            }

            // 2. Insertar registros con la estructura legacy (SplitPago + Liquidacion con FK SplitPagoId)
            var liqId = Guid.NewGuid();
            var splitId = Guid.NewGuid();
            var ordenId = Guid.NewGuid();
            var vendId = Guid.NewGuid();

            await using (var legacyConn = new NpgsqlConnection(legacyConnStr))
            {
                await legacyConn.OpenAsync();
                await using var insertCmd = legacyConn.CreateCommand();
                insertCmd.CommandText = $@"
                    INSERT INTO finance.ordenes (""Id"", ""NumeroOrden"", ""CompradorId"", ""VendedorId"", ""MontoTotal"", ""Moneda"", ""Estado"", ""TipoCliente"", ""NumeroComprobanteFiscal"", ""GuiaEnvio"", ""DireccionEntrega"", ""FechaCreacion"")
                    VALUES ('{ordenId}', 'ORD-LEGACY-01', '{Guid.NewGuid()}', '{vendId}', 100.00, 'USD', 'Entregada', 'ConsumidorFinal', 'FAC-001', 'GUIA-001', 'San Salvador Centro', NOW());

                    INSERT INTO finance.splits_pago (""Id"", ""OrdenId"", ""MontoTotal"", ""PorcentajeVendedor"", ""MontoVendedor"", ""PorcentajeMarketplace"", ""MontoMarketplace"", ""TransaccionId"", ""FechaProcesado"")
                    VALUES ('{splitId}', '{ordenId}', 100.00, 90.00, 90.00, 10.00, 10.00, 'TX-LEGACY-01', NOW());

                    INSERT INTO finance.liquidaciones (""Id"", ""VendedorId"", ""SplitPagoId"", ""MontoLiquidar"", ""Moneda"", ""Estado"", ""FechaCreacion"")
                    VALUES ('{liqId}', '{vendId}', '{splitId}', 90.00, 'USD', 'Pendiente', NOW());
                ";
                await insertCmd.ExecuteNonQueryAsync();
            }

            // 3. Aplicar la nueva migración LiquidacionUnoAMuchosSplits
            await using (var context = new FinanceDbContext(options))
            {
                await context.Database.MigrateAsync();
            }

            // 4. Verificar que el SplitPago ahora tiene LiquidacionId poblado con el Id de la liquidación preexistente
            await using (var checkConn = new NpgsqlConnection(legacyConnStr))
            {
                await checkConn.OpenAsync();
                await using var checkCmd = checkConn.CreateCommand();
                checkCmd.CommandText = $@"SELECT ""LiquidacionId"" FROM finance.splits_pago WHERE ""Id"" = '{splitId}';";
                var result = await checkCmd.ExecuteScalarAsync();

                result.Should().NotBeNull();
                result.Should().Be(liqId, "la migración de datos debe poblar LiquidacionId a partir del SplitPagoId preexistente");
            }
        }
        finally
        {
            try
            {
                await using var adminConn = new NpgsqlConnection(_adminConnStr);
                await adminConn.OpenAsync();
                await using var cmd = adminConn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT pg_terminate_backend(pg_stat_activity.pid)
                    FROM pg_stat_activity
                    WHERE pg_stat_activity.datname = '{legacyDbName}' AND pid <> pg_backend_pid();
                    DROP DATABASE IF EXISTS ""{legacyDbName}"";
                ";
                await cmd.ExecuteNonQueryAsync();
            }
            catch { /* ignore */ }
        }
    }
}
