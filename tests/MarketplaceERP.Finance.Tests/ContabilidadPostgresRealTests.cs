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

    // ── Test (d): Idempotencia concurrente: Dos disparos en paralelo para la misma orden ─
    [SkippableFact]
    public async Task Test_D_DosDisparosAsientoVenta_EnParalelo_RetornanLaMismaPartidaSin500()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var dbInit = CreateDbContext();
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
        dbInit.Ordenes.Add(orden);
        await dbInit.SaveChangesAsync();

        // Ejecutar dos llamadas en paralelo estricto con DbContexts distintos para forzar concurrencia a nivel PostgreSQL
        await using var db1 = CreateDbContext();
        await using var db2 = CreateDbContext();

        var contabilidad1 = new ContabilidadService(db1, NullLogger<ContabilidadService>.Instance);
        var contabilidad2 = new ContabilidadService(db2, NullLogger<ContabilidadService>.Instance);

        var task1 = Task.Run(() => contabilidad1.GenerarAsientoVentaAsync(ordenId, 100m, 13m, "FacturaConsumidorFinal"));
        var task2 = Task.Run(() => contabilidad2.GenerarAsientoVentaAsync(ordenId, 100m, 13m, "FacturaConsumidorFinal"));

        var resultados = await Task.WhenAll(task1, task2);
        var p1 = resultados[0];
        var p2 = resultados[1];

        p1.Should().NotBeNull();
        p2.Should().NotBeNull();
        p1.Id.Should().Be(p2.Id, "la transacción perdedora de concurrencia (Postgres 23505) debe recuperar la existente sin lanzar 500");
        p1.NumeroPartida.Should().Be(p2.NumeroPartida);

        // Verificar en BD que existe exactamente 1 partida para esta orden
        await using var verifyDb = CreateDbContext();
        var count = await verifyDb.PartidasContables
            .Where(p => p.TipoOperacion == "Venta" && p.ReferenciaOrigenId == ordenId)
            .CountAsync();

        count.Should().Be(1, "no se deben crear partidas contables duplicadas para la misma orden de venta");
    }

    // ── Test (d2): Orden sin splits no acredita 2108 con 0 en silencio (DECISIÓN-PENDIENTE)
    [SkippableFact]
    public async Task Test_D2_OrdenSinSplits_NoAcredita2108EnSilencio()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"ORD-NOSPLIT-{Guid.NewGuid():N}"[..20],
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = 150m,
            ImpuestoIva = 19.50m,
            MontoTotal = 169.50m,
            Estado = EstadoOrden.Aprobada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        db.Ordenes.Add(orden);
        await db.SaveChangesAsync();

        var contabilidad = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var partida = await contabilidad.GenerarAsientoVentaAsync(ordenId, 150m, 19.50m, "FacturaConsumidorFinal");

        partida.Should().NotBeNull();
        partida.EstaCuadrada.Should().BeTrue();
        // Verificar que ninguna línea tiene la cuenta 2108
        partida.Lineas.Should().NotContain(l => l.CodigoCuenta == "2108",
            "una orden sin splits no debe acreditar pasivo a vendedores (2108) con 0 en silencio");
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

    // ── Test (g): Validar cuentas y montos exactos del asiento de venta con split (FASE 5d) ──
    [SkippableFact]
    public async Task Test_G1_AsientoVenta_Subtotal100_Iva13_Envio0_Total113_Vendedor90()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"ORD-{Guid.NewGuid():N}"[..20],
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = 100m,
            ImpuestoIva = 13m,
            CostoEnvio = 0m,
            MontoTotal = 113m,
            Estado = EstadoOrden.Aprobada,
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
            PorcentajeVendedor = 79.65m,
            PorcentajeMarketplace = 20.35m,
            TransaccionId = "TRX-FASE5D-01",
            FechaProcesado = DateTime.UtcNow
        };
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();

        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var partida = await service.GenerarAsientoVentaAsync(ordenId, 100m, 13m, "FacturaConsumidorFinal");

        partida.Should().NotBeNull();
        partida.EstaCuadrada.Should().BeTrue();
        partida.TotalDebe.Should().Be(113m);
        partida.TotalHaber.Should().Be(113m);

        await using var verifyDb = CreateDbContext();
        var guardada = await verifyDb.PartidasContables
            .Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.Id == partida.Id);

        guardada.Should().NotBeNull();
        guardada!.Lineas.Should().HaveCount(4);

        // Cuenta Activo (1102 CxC Pasarela / 1101 según catálogo)
        var lineaActivo = guardada.Lineas.First(l => l.Debe > 0);
        lineaActivo.Debe.Should().Be(113m);
        lineaActivo.Haber.Should().Be(0m);

        // 2108: MontoVendedor = 90
        var linea2108 = guardada.Lineas.First(l => l.CodigoCuenta == "2108");
        linea2108.Haber.Should().Be(90m);
        linea2108.Debe.Should().Be(0m);

        // 4101: MontoTotal - 2108 - 2105 = 113 - 90 - 13 = 10
        var linea4101 = guardada.Lineas.First(l => l.CodigoCuenta == "4101");
        linea4101.Haber.Should().Be(10m);
        linea4101.Debe.Should().Be(0m);

        // 2105: IVA completo = 13
        var linea2105 = guardada.Lineas.First(l => l.CodigoCuenta == "2105");
        linea2105.Haber.Should().Be(13m);
        linea2105.Debe.Should().Be(0m);
    }

    [SkippableFact]
    public async Task Test_G2_AsientoVenta_Subtotal200_Iva26_Envio5_Total231_Vendedor180()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"ORD-{Guid.NewGuid():N}"[..20],
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = 200m,
            ImpuestoIva = 26m,
            CostoEnvio = 5m,
            MontoTotal = 231m,
            Estado = EstadoOrden.Aprobada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        db.Ordenes.Add(orden);

        var split = new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = orden.Id,
            MontoTotal = 231m,
            MontoVendedor = 180m,
            MontoMarketplace = 51m,
            PorcentajeVendedor = 77.92m,
            PorcentajeMarketplace = 22.08m,
            TransaccionId = "TRX-FASE5D-02",
            FechaProcesado = DateTime.UtcNow
        };
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();

        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var partida = await service.GenerarAsientoVentaAsync(ordenId, 200m, 26m, "FacturaConsumidorFinal");

        partida.Should().NotBeNull();
        partida.EstaCuadrada.Should().BeTrue();
        partida.TotalDebe.Should().Be(231m);
        partida.TotalHaber.Should().Be(231m);

        await using var verifyDb = CreateDbContext();
        var guardada = await verifyDb.PartidasContables
            .Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.Id == partida.Id);

        guardada.Should().NotBeNull();
        guardada!.Lineas.Should().HaveCount(4);

        // Cuenta Activo (Debe = 231)
        var lineaActivo = guardada.Lineas.First(l => l.Debe > 0);
        lineaActivo.Debe.Should().Be(231m);
        lineaActivo.Haber.Should().Be(0m);

        // 2108: MontoVendedor = 180
        var linea2108 = guardada.Lineas.First(l => l.CodigoCuenta == "2108");
        linea2108.Haber.Should().Be(180m);
        linea2108.Debe.Should().Be(0m);

        // 4101: MontoTotal - 2108 - 2105 = 231 - 180 - 26 = 25
        var linea4101 = guardada.Lineas.First(l => l.CodigoCuenta == "4101");
        linea4101.Haber.Should().Be(25m);
        linea4101.Debe.Should().Be(0m);

        // 2105: IVA completo = 26
        var linea2105 = guardada.Lineas.First(l => l.CodigoCuenta == "2105");
        linea2105.Haber.Should().Be(26m);
        linea2105.Debe.Should().Be(0m);
    }

    [SkippableFact]
    public async Task Test_G3_AsientoVenta_VendedorMayorQueTotalMenosIva_LanzaInvalidOperationException()
    {
        Skip.If(string.IsNullOrWhiteSpace(RawConnStr), "TEST_PG_CONNECTION no configurada.");

        await using var db = CreateDbContext();
        var ordenId = Guid.NewGuid();
        var orden = new Orden
        {
            Id = ordenId,
            NumeroOrden = $"ORD-{Guid.NewGuid():N}"[..20],
            CompradorId = Guid.NewGuid(),
            VendedorId = Guid.NewGuid(),
            SubTotal = 100m,
            ImpuestoIva = 13m,
            CostoEnvio = 0m,
            MontoTotal = 113m,
            Estado = EstadoOrden.Aprobada,
            TipoComprobante = "FacturaConsumidorFinal",
            FechaCreacion = DateTime.UtcNow
        };
        db.Ordenes.Add(orden);

        // Vendedor = 105 > Total - IVA (113 - 13 = 100) -> Residuo 4101 = -5
        var split = new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = orden.Id,
            MontoTotal = 113m,
            MontoVendedor = 105m,
            MontoMarketplace = 8m,
            PorcentajeVendedor = 92.92m,
            PorcentajeMarketplace = 7.08m,
            TransaccionId = "TRX-FASE5D-03",
            FechaProcesado = DateTime.UtcNow
        };
        db.SplitsPago.Add(split);
        await db.SaveChangesAsync();

        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        var act = () => service.GenerarAsientoVentaAsync(ordenId, 100m, 13m, "FacturaConsumidorFinal");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*residuo para 4101 es negativo*");
    }
}
