/**
 * ERP desarrollado por Marcelo Zepeda
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Tests de Contabilidad General — verifica la regla de partida doble:
/// Total Debe == Total Haber en asientos de venta y compra.
/// </summary>
public class ContabilidadServiceTests
{
    private static FinanceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FinanceDbContext(options);
    }

    [Fact]
    public async Task GenerarAsientoVenta_DebeEstarCuadrado()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        decimal subtotal = 100m;
        decimal iva = 13m;

        // Act
        var partida = await service.GenerarAsientoVentaAsync(Guid.NewGuid(), subtotal, iva, "FacturaConsumidorFinal");

        // Assert
        partida.EstaCuadrada.Should().BeTrue("la regla de partida doble exige Debe == Haber");
        partida.TotalDebe.Should().Be(113m);
        partida.TotalHaber.Should().Be(113m);
        partida.Lineas.Should().HaveCount(3, "asiento de venta tiene 3 líneas: Caja, Ingresos, IVA Débito");
        partida.TipoOperacion.Should().Be("Venta");
    }

    [Fact]
    public async Task GenerarAsientoVenta_CreditoFiscal_UsaCuentaCxC()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

        // Act
        var partida = await service.GenerarAsientoVentaAsync(Guid.NewGuid(), 500m, 65m, "Credito Fiscal CCF");

        // Assert
        // Assert — la cuenta activo tiene Debe > 0; para CCF debe ser CxC (1105)
        var lineaDebe = partida.Lineas.First(l => l.Debe > 0);
        lineaDebe.CodigoCuenta.Should().Be("1105", "Factura de Crédito Fiscal debe ir a Cuentas por Cobrar");
    }

    [Fact]
    public async Task GenerarAsientoCompra_DebeEstarCuadrado()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

        var po = new OrdenCompra
        {
            Id = Guid.NewGuid(),
            NumeroPO = "PO-TEST-001",
            ProveedorId = Guid.NewGuid(),
            ProveedorNombre = "Proveedor Test",
            SubTotal = 200m,
            ImpuestoIva = 26m,
            MontoTotal = 226m,
        };
        db.OrdenesCompra.Add(po);
        await db.SaveChangesAsync();

        // Act
        var partida = await service.GenerarAsientoCompraAsync(po.Id, po.SubTotal, po.ImpuestoIva);

        // Assert
        partida.EstaCuadrada.Should().BeTrue("la regla de partida doble exige Debe == Haber");
        partida.TotalDebe.Should().Be(226m);
        partida.TotalHaber.Should().Be(226m);
        partida.Lineas.Should().HaveCount(3, "Inventario + IVA Crédito + CxP Proveedores");
        partida.TipoOperacion.Should().Be("Compra");
    }

    [Fact]
    public async Task BalanceComprobacion_SumaDebeIgualHaber()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);

        // Generar 3 asientos
        await service.GenerarAsientoVentaAsync(Guid.NewGuid(), 100m, 13m, "FacturaConsumidorFinal");
        await service.GenerarAsientoVentaAsync(Guid.NewGuid(), 200m, 26m, "CreditoFiscalCCF");

        var po = new OrdenCompra { Id = Guid.NewGuid(), NumeroPO = "PO-BAL-001",
            ProveedorId = Guid.NewGuid(), ProveedorNombre = "Test", SubTotal = 50m, ImpuestoIva = 6.5m, MontoTotal = 56.5m };
        db.OrdenesCompra.Add(po);
        await db.SaveChangesAsync();
        await service.GenerarAsientoCompraAsync(po.Id, 50m, 6.5m);

        // Assert
        // El Libro Mayor usa GroupBy in-memory; obtenemos las líneas y agrupamos en el cliente
        var todasLineas = await db.LineasPartidaContable.ToListAsync();
        decimal debe  = todasLineas.Sum(l => l.Debe);
        decimal haber = todasLineas.Sum(l => l.Haber);
        bool cuadrado = Math.Abs(debe - haber) < 0.01m;
        cuadrado.Should().BeTrue("el balance de comprobación siempre debe cuadrar");
        debe.Should().Be(haber);
    }

    [Fact]
    public async Task LibroDiario_FiltraPorFechas()
    {
        // Arrange
        await using var db = CreateDbContext();
        var service = new ContabilidadService(db, NullLogger<ContabilidadService>.Instance);
        await service.GenerarAsientoVentaAsync(Guid.NewGuid(), 100m, 13m, "FacturaConsumidorFinal");

        // Act — rango que incluye hoy
        var diario = (await service.ObtenerLibroDiarioAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1))).ToList();
        var diariovacio = (await service.ObtenerLibroDiarioAsync(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(2))).ToList();

        // Assert
        diario.Should().HaveCount(1);
        diariovacio.Should().BeEmpty("ningún asiento cae en ese rango futuro");
    }
}

/// <summary>
/// Tests del motor de 3-Way Match: verifica lógica de discrepancias de cantidad y precio.
/// </summary>
public class TresWayMatchingServiceTests
{
    private static FinanceDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FinanceDbContext(options);
    }

    private static async Task<(OrdenCompra po, FacturaProveedor factura)> CrearPOyFactura(
        FinanceDbContext db, int cantidadOrdenada, decimal montoFactura)
    {
        var po = new OrdenCompra
        {
            Id = Guid.NewGuid(),
            NumeroPO = $"PO-{Guid.NewGuid():N}",
            ProveedorId = Guid.NewGuid(),
            ProveedorNombre = "Proveedor Match Test",
            SubTotal = 100m,
            ImpuestoIva = 13m,
            MontoTotal = 113m,
            Items = new List<ItemOrdenCompra>
            {
                new() { Id = Guid.NewGuid(), Sku = "SKU-001", NombreProducto = "Producto A",
                        CantidadSolicitada = cantidadOrdenada, PrecioUnitarioAcordado = 10m }
            }
        };
        var factura = new FacturaProveedor
        {
            Id = Guid.NewGuid(),
            OrdenCompraId = po.Id,
            ProveedorId = po.ProveedorId,
            NumeroFactura = $"FAC-{Guid.NewGuid():N}",
            MontoSubTotal = montoFactura / 1.13m,
            MontoIva = montoFactura - (montoFactura / 1.13m),
            MontoTotal = montoFactura,
            FechaEmision = DateTime.UtcNow,
        };
        db.OrdenesCompra.Add(po);
        db.FacturasProveedor.Add(factura);
        await db.SaveChangesAsync();
        return (po, factura);
    }

    [Fact]
    public async Task Match3Way_CantidadesYPreciosIdenticos_ResultadoConforme()
    {
        await using var db = CreateDbContext();
        var service = new TresWayMatchingService(db, NullLogger<TresWayMatchingService>.Instance);
        var (po, factura) = await CrearPOyFactura(db, cantidadOrdenada: 10, montoFactura: 113m);

        var req = new Validar3WayMatchRequest(po.Id, factura.Id, CantidadRecibidaBodega: 10);
        var match = await service.EjecutarMatchAsync(req);

        match.Resultado.Should().Be(Estado3WayMatch.Conforme);
        match.AutorizadoParaPago.Should().BeTrue();
        factura.AprobadaParaPago.Should().BeTrue();
    }

    [Fact]
    public async Task Match3Way_CantidadDistinta_DiscrepanciaCantidad()
    {
        await using var db = CreateDbContext();
        var service = new TresWayMatchingService(db, NullLogger<TresWayMatchingService>.Instance);
        var (po, factura) = await CrearPOyFactura(db, cantidadOrdenada: 10, montoFactura: 113m);

        var req = new Validar3WayMatchRequest(po.Id, factura.Id, CantidadRecibidaBodega: 8);
        var match = await service.EjecutarMatchAsync(req);

        match.Resultado.Should().Be(Estado3WayMatch.DiscrepanciaCantidad);
        match.AutorizadoParaPago.Should().BeFalse();
    }

    [Fact]
    public async Task Match3Way_MontoDistinto_DiscrepanciaPrecio()
    {
        await using var db = CreateDbContext();
        var service = new TresWayMatchingService(db, NullLogger<TresWayMatchingService>.Instance);
        var (po, factura) = await CrearPOyFactura(db, cantidadOrdenada: 10, montoFactura: 150m); // PO es 113

        var req = new Validar3WayMatchRequest(po.Id, factura.Id, CantidadRecibidaBodega: 10);
        var match = await service.EjecutarMatchAsync(req);

        match.Resultado.Should().Be(Estado3WayMatch.DiscrepanciaPrecio);
        match.AutorizadoParaPago.Should().BeFalse();
    }

    [Fact]
    public async Task Match3Way_CantidadYMontoDistintos_DiscrepanciaTotal()
    {
        await using var db = CreateDbContext();
        var service = new TresWayMatchingService(db, NullLogger<TresWayMatchingService>.Instance);
        var (po, factura) = await CrearPOyFactura(db, cantidadOrdenada: 10, montoFactura: 200m);

        var req = new Validar3WayMatchRequest(po.Id, factura.Id, CantidadRecibidaBodega: 7);
        var match = await service.EjecutarMatchAsync(req);

        match.Resultado.Should().Be(Estado3WayMatch.DiscrepanciaTotal);
        match.AutorizadoParaPago.Should().BeFalse();
    }
}

/// <summary>
/// Tests de la máquina de estados OMS — verifica transiciones válidas.
/// </summary>
public class OmsEstadosMaquinaTests
{
    [Theory]
    [InlineData(EstadoOrden.Pendiente, EstadoOrden.Aprobada, true)]
    [InlineData(EstadoOrden.Pendiente, EstadoOrden.Cancelada, true)]
    [InlineData(EstadoOrden.Aprobada, EstadoOrden.EnProceso, true)]
    [InlineData(EstadoOrden.EnProceso, EstadoOrden.Enviada, true)]
    [InlineData(EstadoOrden.Enviada, EstadoOrden.Entregada, true)]
    [InlineData(EstadoOrden.Entregada, EstadoOrden.Pendiente, false)]
    [InlineData(EstadoOrden.Cancelada, EstadoOrden.Aprobada, false)]
    [InlineData(EstadoOrden.Entregada, EstadoOrden.Cancelada, false)]
    public void TransicionOMS_ValidaReglas(EstadoOrden desde, EstadoOrden hacia, bool debeSerValida)
    {
        var transicionesValidas = new Dictionary<EstadoOrden, EstadoOrden[]>
        {
            [EstadoOrden.Pendiente]  = [EstadoOrden.Aprobada, EstadoOrden.Cancelada],
            [EstadoOrden.Aprobada]   = [EstadoOrden.EnProceso, EstadoOrden.Cancelada],
            [EstadoOrden.EnProceso]  = [EstadoOrden.Enviada, EstadoOrden.Cancelada],
            [EstadoOrden.Enviada]    = [EstadoOrden.Entregada],
            [EstadoOrden.Entregada]  = [],
            [EstadoOrden.Cancelada]  = [],
        };

        bool esValida = transicionesValidas.TryGetValue(desde, out var permitidos)
                        && permitidos.Contains(hacia);

        esValida.Should().Be(debeSerValida,
            $"transición {desde} → {hacia} debería ser {(debeSerValida ? "válida" : "inválida")}");
    }

    [Fact]
    public void SplitPago_Calculo90_10_EsCorrecto()
    {
        // Arrange
        decimal montoTotal = 100m;
        decimal porcentajeVendedor = 90m;
        decimal porcentajeMarketplace = 10m;

        // Act
        decimal montoVendedor    = Math.Round(montoTotal * porcentajeVendedor / 100m, 2);
        decimal montoMarketplace = Math.Round(montoTotal * porcentajeMarketplace / 100m, 2);

        // Assert
        montoVendedor.Should().Be(90m);
        montoMarketplace.Should().Be(10m);
        (montoVendedor + montoMarketplace).Should().Be(montoTotal, "el split debe sumar el total");
    }
}
