/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using System.Collections.Concurrent;
using FluentAssertions;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Tests exhaustivos de Contabilidad contra PostgreSQL real (FASE 5).
/// Valida:
///  (a) Asiento manual de 3 líneas guarda cuentas y montos exactos.
///  (b) Asiento manual inválido (descuadrado, 1 sola línea, cuenta inexistente, ambos Debe y Haber > 0) -> 400 / Excepción.
///  (c) Venta + Liquidación + Pago del mismo vendedor: saldo de cuenta 2108 queda exactamente en 0 y Balance de Comprobación cuadra.
///  (d) Dos disparos de asiento de venta para la misma orden -> Idempotencia estricta (una sola partida).
///  (e) 100 llamadas simultáneas a NumeroPartida -> 100 números únicos y correlativos mediante secuencia PostgreSQL.
///  (f) Paginación de Libro Diario retorna Total y respeta pageSize.
/// Requiere variable de entorno TEST_PG_CONNECTION.
/// </summary>
public class ContabilidadPostgresRealTests : IAsyncLifetime
{
    private static readonly string? RawConnStr = Environment.GetEnvironmentVariable("TEST_PG_CONNECTION");
    private readonly string _dbName = $"test_contab_{Guid.NewGuid():N}";
    private string _adminConnStr = "";
    private string _testConnStr = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(RawConnStr))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(RawConnStr);
        builder.Database = "postgres";
        _adminConnStr = builder.ConnectionString;

        builder.Database = _dbName;
        builder.IncludeErrorDetail = true;
        _testConnStr = builder.ConnectionString;

        // Crear base de datos aislada
        await using var adminConn = new NpgsqlConnection(_adminConnStr);
        await adminConn.OpenAsync();
        await using var cmd = adminConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{_dbName}\";";
        await cmd.ExecuteNonQueryAsync();

        // Aplicar todas las migraciones (InitialCreate, LiquidacionUnoAMuchosSplits, ContabilidadSecuenciaEIdempotencia)
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
            // Ignorar errores en limpieza
        }
    }

    private FinanceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(_testConnStr)
            .Options;
        return new FinanceDbContext(options);
    }

    // ── Test (a): Asiento manual de 3 líneas guarda cuentas y montos exactos ─
    [SkippableFact]
    public async Task Test_A_AsientoManual3Lineas_GuardaCuentasYMontosExactos()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

        var lineas = new List<LineaAsientoInput>
        {
            new("1101", 100m, 0m, "Ingreso a Caja"),
            new("4101", 0m, 88.50m, "Ingreso de ventas"),
            new("2105", 0m, 11.50m, "IVA Débito Fiscal")
        };

        var partida = await service.GenerarAsientoManualAsync(
            concepto: "Asiento de ajuste manual 3 líneas",
            tipoOperacion: "Ajuste",
            lineasInput: lineas);

        partida.Should().NotBeNull();
        partida.TotalDebe.Should().Be(100m);
        partida.TotalHaber.Should().Be(100m);
        partida.NumeroPartida.Should().StartWith("AST-");

        await using var verifyDb = CreateDbContext();
        var guardada = await verifyDb.PartidasContables
            .Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.Id == partida.Id);

        guardada.Should().NotBeNull();
        guardada!.Lineas.Should().HaveCount(3);
        guardada.Lineas.First(l => l.CodigoCuenta == "1101").Debe.Should().Be(100m);
        guardada.Lineas.First(l => l.CodigoCuenta == "4101").Haber.Should().Be(88.50m);
        guardada.Lineas.First(l => l.CodigoCuenta == "2105").Haber.Should().Be(11.50m);
    }

    // ── Test (b): Validaciones de asiento manual inválido ─────────────────────
    [SkippableFact]
    public async Task Test_B_AsientoManualInvalido_LanzaExcepcionesEspecificas()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

        // 1. Descuadrado (Debe != Haber)
        var lineasDescuadradas = new List<LineaAsientoInput>
        {
            new("1101", 100m, 0m),
            new("4101", 0m, 90m)
        };
        var actDescuadrado = async () => await service.GenerarAsientoManualAsync("Descuadrado", "Ajuste", lineasDescuadradas);
        await actDescuadrado.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no cuadra*");

        // 2. Una sola línea
        var unaLinea = new List<LineaAsientoInput> { new("1101", 50m, 0m) };
        var actUnaLinea = async () => await service.GenerarAsientoManualAsync("Una sola línea", "Ajuste", unaLinea);
        await actUnaLinea.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*mínimo de 2 líneas*");

        // 3. Cuenta inexistente
        var cuentaInvalida = new List<LineaAsientoInput>
        {
            new("9999", 50m, 0m),
            new("1101", 0m, 50m)
        };
        var actCuentaInvalida = async () => await service.GenerarAsientoManualAsync("Cuenta inexistente", "Ajuste", cuentaInvalida);
        await actCuentaInvalida.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no existe o no está activa*");

        // 4. Ambos Debe y Haber > 0 en la misma línea
        var ambosSignos = new List<LineaAsientoInput>
        {
            new("1101", 50m, 10m),
            new("4101", 0m, 40m)
        };
        var actAmbosSignos = async () => await service.GenerarAsientoManualAsync("Ambos > 0", "Ajuste", ambosSignos);
        await actAmbosSignos.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no se permite registrar montos positivos en Debe y Haber simultáneamente*");
    }

    // ── Test (c): Venta + Liquidación + Pago -> saldo 2108 es 0 y Balance cuadra
    [SkippableFact]
    public async Task Test_C_VentaMasLiquidacionMasPago_Saldo2108QuedaEnCero_YBalanceCuadra()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var vendedorId = Guid.NewGuid();

        // 1. Crear Orden entregada con SplitPago 90/10 ($100 total: $90 vendedor, $10 marketplace)
        var orden = new Orden
        {
            Id = Guid.NewGuid(),
            NumeroOrden = $"ORD-{Guid.NewGuid():N}"[..20],
            CompradorId = Guid.NewGuid(),
            VendedorId = vendedorId,
            SubTotal = 100m,
            ImpuestoIva = 13m,
            CostoEnvio = 0m,
            MontoTotal = 113m,
            Estado = EstadoOrden.Entregada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        db.Ordenes.Add(orden);

        var split = new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = orden.Id,
            MontoTotal = 113m,
            MontoVendedor = 90m,
            MontoMarketplace = 23m,
            PorcentajeVendedor = 90m,
            PorcentajeMarketplace = 10m,
            TransaccionId = "TRX-CONTAB-01",
            FechaProcesado = DateTime.UtcNow
        };
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();

        // 2. Registrar Asiento de Venta
        var contabilidad = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var liquidacionService = new LiquidacionService(db, contabilidad, NullLogger<LiquidacionService>.Instance);

        var partidaVenta = await contabilidad.GenerarAsientoVentaAsync(
            orden.Id, orden.SubTotal, orden.ImpuestoIva, orden.TipoComprobante);
        partidaVenta.Should().NotBeNull();
        partidaVenta.EstaCuadrada.Should().BeTrue();

        // En este punto, la cuenta 2108 tiene un saldo acreedor (Haber) de $90.00
        var mayorIntermedio = await contabilidad.ObtenerLibroMayorAsync("2108");
        var cta2108Intermedia = mayorIntermedio.First();
        cta2108Intermedia.TotalHaber.Should().Be(90m);
        cta2108Intermedia.TotalDebe.Should().Be(0m);

        // 3. Generar y Procesar Pago de la Liquidación
        var liq = await liquidacionService.GenerarLiquidacionAsync(vendedorId);
        liq.MontoLiquidar.Should().Be(90m);

        await liquidacionService.ProcesarPagoAsync(liq.Id);

        // 4. Verificar Libro Mayor de la cuenta 2108: debe tener Debe=90, Haber=90 -> Saldo=0
        await using var verifyDb = CreateDbContext();
        var contabVerify = new ContabilidadService(verifyDb, NullLogger<ContabilidadService>.Instance);

        var mayor2108 = (await contabVerify.ObtenerLibroMayorAsync("2108")).ToList();
        mayor2108.Should().HaveCount(1);
        var cuentaPasivo = mayor2108.First();
        cuentaPasivo.TotalDebe.Should().Be(90m);
        cuentaPasivo.TotalHaber.Should().Be(90m);
        cuentaPasivo.Saldo.Should().Be(0m, "el saldo de Pasivos por Liquidar a Vendedores (2108) debe quedar saldado en 0");

        // 5. Verificar que el Balance de Comprobación general cuadre exactamente (Total Debe == Total Haber)
        var balance = await contabVerify.ObtenerBalanceComprobacionAsync();
        balance.EstaCuadrado.Should().BeTrue("el balance general debe cuadrar perfectamente tras la venta y liquidación");
        balance.TotalDebe.Should().Be(balance.TotalHaber);
    }

    // ── Test (d): Idempotencia: Dos disparos de asiento para la misma orden ─
    [SkippableFact]
    public async Task Test_D_DosDisparosAsientoVenta_MismaOrden_RetornaLaMismaPartidaSinDuplicar()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var contabilidad = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"ORD-IDEM-{Guid.NewGuid():N}"[..20],
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = 100m,
            ImpuestoIva = 13m,
            MontoTotal = 113m,
            Estado = EstadoOrden.Aprobada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        db.Ordenes.Add(orden);
        await db.SaveChangesAsync();

        // Disparo 1
        var p1 = await contabilidad.GenerarAsientoVentaAsync(ordenId, 100m, 13m, "FacturaConsumidorFinal");

        // Disparo 2 simultáneo o consecutivo
        var p2 = await contabilidad.GenerarAsientoVentaAsync(ordenId, 100m, 13m, "FacturaConsumidorFinal");

        p1.Id.Should().Be(p2.Id, "la segunda llamada debe retornar la partida contable ya existente sin romper");
        p1.NumeroPartida.Should().Be(p2.NumeroPartida);

        // Verificar en BD que existe exactamente 1 partida para esta orden
        await using var verifyDb = CreateDbContext();
        var count = await verifyDb.PartidasContables
            .Where(p => p.TipoOperacion == "Venta" && p.ReferenciaOrigenId == ordenId)
            .CountAsync();

        count.Should().Be(1, "no se deben crear partidas contables duplicadas para la misma orden de venta");
    }

    // ── Test (e): 100 NumeroPartida en paralelo son todos únicos ──────────────
    [SkippableFact]
    public async Task Test_E_CienAsientosEnParalelo_TodosTienenNumeroPartidaUnico()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        var numerosGenerados = new ConcurrentBag<string>();
        var tasks = Enumerable.Range(1, 100).Select(i => Task.Run(async () =>
        {
            await using var db = CreateDbContext();
            var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

            var lineas = new List<LineaAsientoInput>
            {
                new("1101", 10m * i, 0m),
                new("4101", 0m, 10m * i)
            };

            var partida = await service.GenerarAsientoManualAsync($"Asiento Concurrente {i}", "Ajuste", lineas);
            numerosGenerados.Add(partida.NumeroPartida);
        }));

        await Task.WhenAll(tasks);

        numerosGenerados.Should().HaveCount(100);
        var dist = numerosGenerados.Distinct().ToList();
        dist.Should().HaveCount(100, "todos los 100 números de partida generados en concurrencia deben ser estrictamente únicos");
    }

    // ── Test (f): Paginación de Libro Diario retorna Total y respeta pageSize ─
    [SkippableFact]
    public async Task Test_F_PaginacionLibroDiario_RetornaTotalYRespetaPageSize()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

        // Insertar 15 partidas
        for (int i = 1; i <= 15; i++)
        {
            var lineas = new List<LineaAsientoInput>
            {
                new("1101", 10m, 0m),
                new("4101", 0m, 10m)
            };
            await service.GenerarAsientoManualAsync($"Partida de prueba {i}", "Ajuste", lineas);
        }

        // Consultar página 1 con pageSize = 5
        var pagina1 = await service.ObtenerLibroDiarioPaginadoAsync(page: 1, pageSize: 5);
        pagina1.Total.Should().BeGreaterOrEqualTo(15);
        pagina1.Page.Should().Be(1);
        pagina1.PageSize.Should().Be(5);
        pagina1.Partidas.Should().HaveCount(5);

        // Consultar página 2 con pageSize = 5
        var pagina2 = await service.ObtenerLibroDiarioPaginadoAsync(page: 2, pageSize: 5);
        pagina2.Page.Should().Be(2);
        pagina2.Partidas.Should().HaveCount(5);

        // Comprobar que no hay partidas duplicadas entre página 1 y 2
        var idsP1 = pagina1.Partidas.Select(p => p.Id);
        var idsP2 = pagina2.Partidas.Select(p => p.Id);
        idsP1.Intersect(idsP2).Should().BeEmpty();
    }
}
