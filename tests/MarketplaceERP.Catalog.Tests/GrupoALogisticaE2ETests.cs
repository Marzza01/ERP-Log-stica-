/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Catalog.Tests;

/// <summary>
/// Fase 8A: ensayo integral del núcleo logístico del Grupo A.
/// Conecta en una sola prueba Inbound, reserva, WMS, bloqueo, Fulfillment,
/// RMA, conteo cíclico, ajuste y alertas sin depender de PostgreSQL real.
/// </summary>
public class GrupoALogisticaE2ETests
{
    [Fact]
    public async Task FlujoLogisticoCompleto_ConservaReglasDeInventarioYTrazabilidad()
    {
        await using var db = CrearDb();

        var producto = CrearProducto();
        var ubicacionA = CrearUbicacion("PAS-A-RCK-01-NIV-01-POS-01", "Picking");
        var ubicacionB = CrearUbicacion("PAS-B-RCK-01-NIV-01-POS-01", "AlmacenamientoGeneral");
        db.AddRange(producto, ubicacionA, ubicacionB);
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db, NullLogger<InventoryService>.Instance);
        var stockService = new StockService(db, NullLogger<StockService>.Instance);
        var wms = new WmsService(db, inventory, NullLogger<WmsService>.Instance);
        var fulfillment = new FulfillmentService(db, stockService, NullLogger<FulfillmentService>.Instance);
        var rma = new RmaService(db, inventory, NullLogger<RmaService>.Instance);
        var ajustes = new AjusteInventarioService(db, NullLogger<AjusteInventarioService>.Instance);
        var conteos = new ConteoCiclicoService(db, ajustes, NullLogger<ConteoCiclicoService>.Instance);
        var alertas = new AlertaInventarioService(db);

        // 1. Inbound: 30 unidades físicas en ubicación A.
        var ordenCompraId = Guid.NewGuid();
        var inbound = await inventory.RecibirInboundAsync(
            producto.Sku,
            30,
            ubicacionA.Id,
            ordenCompraId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Fase 8A - recepción integral");

        inbound.Exitoso.Should().BeTrue();
        var stockAId = inbound.StockFisicoId;
        var stockA = await db.StockFisico.SingleAsync(s => s.Id == stockAId);
        stockA.CantidadTotal.Should().Be(30);
        stockA.CantidadDisponible.Should().Be(30);

        // 2. Checkout lógico: reservar y confirmar 5; físico todavía no cambia.
        var ordenVentaId = Guid.NewGuid();
        var reserva = await stockService.ReservarStockAsync(producto.Id, 5, ordenVentaId);
        reserva.Exitoso.Should().BeTrue();
        await stockService.ConfirmarReservaAsync(reserva.ReservaId);

        stockA.CantidadTotal.Should().Be(30);
        stockA.CantidadReservada.Should().Be(5);
        stockA.CantidadDisponible.Should().Be(25);

        // 3. WMS: transferir 10 disponibles a B. La reserva permanece en A.
        var transferencia = await wms.TransferirStockAsync(
            stockA.Id,
            ubicacionB.Id,
            10,
            Guid.NewGuid(),
            "Fase 8A - transferencia parcial");

        transferencia.Exitoso.Should().BeTrue();
        await db.Entry(stockA).ReloadAsync();
        var stockB = await db.StockFisico.SingleAsync(s =>
            s.ProductoId == producto.Id && s.UbicacionAlmacenId == ubicacionB.Id);

        stockA.CantidadTotal.Should().Be(20);
        stockA.CantidadReservada.Should().Be(5);
        stockB.CantidadTotal.Should().Be(10);
        (stockA.CantidadTotal + stockB.CantidadTotal).Should().Be(30);

        // 4. Incidencia: bloquear 2 en B sin alterar físico.
        var bloqueo = await inventory.BloquearStockAsync(
            stockB.Id,
            2,
            Guid.NewGuid(),
            "Fase 8A - cuarentena");

        bloqueo.Exitoso.Should().BeTrue();
        await db.Entry(stockB).ReloadAsync();
        stockB.CantidadTotal.Should().Be(10);
        stockB.CantidadBloqueada.Should().Be(2);
        stockB.CantidadDisponible.Should().Be(8);

        // 5. Fulfillment: picking + packing + despacho consume la reserva real.
        var pickList = await fulfillment.GenerarPickListAsync(new GenerarPickListRequest(
            ordenVentaId,
            "ORD-F8A-001",
            "QA Grupo A",
            new List<ItemPickingSolicitud>
            {
                new(producto.Id, producto.Sku, producto.Nombre, 5)
            }));

        pickList.Items.Should().NotBeEmpty();
        pickList.Items.Sum(i => i.CantidadSolicitada).Should().Be(5);

        var picking = await fulfillment.ConfirmarPickingAsync(
            pickList.Id,
            pickList.Items
                .Select(i => new ConfirmarItemPickingRequest(i.Id, i.CantidadSolicitada))
                .ToList());

        picking.Should().NotBeNull();
        picking!.Estado.Should().Be(EstadoPicking.Completado);

        var paquete = await fulfillment.EmpacarYRotularAsync(new EmpacarDespachoRequest(
            ordenVentaId,
            "ORD-F8A-001",
            2.5m,
            30m,
            20m,
            15m,
            "LOGIMKT Express",
            "San Salvador",
            "San Salvador Centro",
            "Dirección de ensayo Fase 8A",
            "Cliente QA",
            "7000-0000"));

        var despachado = await fulfillment.DespacharPaqueteAsync(paquete.Id);
        despachado.Should().NotBeNull();
        despachado!.Estado.Should().Be(EstadoPacking.Despachado);

        await db.Entry(stockA).ReloadAsync();
        stockA.CantidadTotal.Should().Be(15);
        stockA.CantidadReservada.Should().Be(0);
        (stockA.CantidadTotal + stockB.CantidadTotal).Should().Be(25);

        // 6. RMA ReStock: 2 unidades vuelven físicamente a B.
        var solicitudRma = await rma.RegistrarSolicitudRmaAsync(new RegistrarRmaRequest(
            ordenVentaId,
            producto.Id,
            2,
            "Devolución de ensayo integral"));

        var rmaCompletado = await rma.InspeccionarDevolucionAsync(
            solicitudRma.Id,
            new DictaminarRmaRequest(
                ClasificacionRMA.ReStock,
                "Inspector QA",
                "Producto apto para reingreso",
                0m,
                ubicacionB.Id,
                Guid.NewGuid()));

        rmaCompletado.Should().NotBeNull();
        rmaCompletado!.Estado.Should().Be(EstadoRMA.Completado);
        await db.Entry(stockB).ReloadAsync();
        stockB.CantidadTotal.Should().Be(12);

        // 7. Liberar cuarentena: físico no cambia, disponibilidad sí.
        var desbloqueo = await inventory.DesbloquearStockAsync(
            stockB.Id,
            2,
            Guid.NewGuid(),
            "Fase 8A - inspección superada");

        desbloqueo.Exitoso.Should().BeTrue();
        await db.Entry(stockB).ReloadAsync();
        stockB.CantidadTotal.Should().Be(12);
        stockB.CantidadBloqueada.Should().Be(0);

        // 8. Conteo cíclico confirma una diferencia de -1 y genera ajuste pendiente.
        var plan = await conteos.PlanificarAsync(stockB.Id, "Fase 8A", "qa");
        plan.Exitoso.Should().BeTrue();

        var inicio = await conteos.IniciarAsync(plan.Conteo!.Id, "qa");
        inicio.Exitoso.Should().BeTrue();
        inicio.Conteo!.CantidadSistema.Should().Be(12);

        var primerConteo = await conteos.RegistrarPrimerConteoAsync(plan.Conteo.Id, 11, "qa");
        primerConteo.Exitoso.Should().BeTrue();
        primerConteo.Conteo!.Estado.Should().Be(EstadoConteoCiclico.RequiereReconteo);

        var reconteo = await conteos.RegistrarReconteoAsync(plan.Conteo.Id, 11, "qa");
        reconteo.Exitoso.Should().BeTrue();
        reconteo.Conteo!.Estado.Should().Be(EstadoConteoCiclico.CompletadoConAjuste);
        reconteo.Conteo.AjusteInventarioId.Should().NotBeNull();

        // Generar el ajuste no cambia stock; aprobar tampoco.
        await db.Entry(stockB).ReloadAsync();
        stockB.CantidadTotal.Should().Be(12);

        var ajusteId = reconteo.Conteo.AjusteInventarioId!.Value;
        var aprobado = await ajustes.AprobarAsync(ajusteId, "supervisor-qa");
        aprobado.Exitoso.Should().BeTrue();
        stockB.CantidadTotal.Should().Be(12);

        var aplicado = await ajustes.AplicarAsync(ajusteId, "supervisor-qa");
        aplicado.Exitoso.Should().BeTrue();
        await db.Entry(stockB).ReloadAsync();
        stockB.CantidadTotal.Should().Be(11);
        (stockA.CantidadTotal + stockB.CantidadTotal).Should().Be(26);

        // 9. Alertas derivadas: disponible global 26 <= mínimo 30 => CriticalStock.
        var activas = await alertas.ObtenerActivasAsync();
        var alerta = activas.Single(a => a.ProductoId == producto.Id);
        alerta.Tipo.Should().Be(TipoAlertaInventario.CriticalStock);
        alerta.Prioridad.Should().Be(PrioridadAlertaInventario.High);
        alerta.CantidadFisica.Should().Be(26);
        alerta.CantidadDisponible.Should().Be(26);
        alerta.Estado.Should().Be(EstadoAlertaInventario.Open);

        var reconocida = await alertas.ReconocerAsync(producto.Id, alerta.Tipo, "qa");
        reconocida.Should().BeTrue();
        var alertaAck = (await alertas.ObtenerActivasAsync()).Single(a => a.ProductoId == producto.Id);
        alertaAck.Estado.Should().Be(EstadoAlertaInventario.Acknowledged);
        alertaAck.CantidadFisica.Should().Be(26, "reconocer una alerta no modifica inventario");

        // 10. Trazabilidad: el ledger contiene cada tipo físico esperado.
        var tipos = await db.MovimientosInventario
            .Where(m => m.ProductoId == producto.Id)
            .Select(m => m.TipoMovimiento)
            .ToListAsync();

        tipos.Should().Contain(TipoMovimientoInventario.RecepcionInbound);
        tipos.Should().Contain(TipoMovimientoInventario.Reserva);
        tipos.Should().Contain(TipoMovimientoInventario.TransferenciaSalida);
        tipos.Should().Contain(TipoMovimientoInventario.TransferenciaEntrada);
        tipos.Should().Contain(TipoMovimientoInventario.BloqueoDano);
        tipos.Should().Contain(TipoMovimientoInventario.DespachoOutbound);
        tipos.Should().Contain(TipoMovimientoInventario.RmaRestock);
        tipos.Should().Contain(TipoMovimientoInventario.Desbloqueo);
        tipos.Should().Contain(TipoMovimientoInventario.AjusteNegativo);

        // Invariantes finales.
        stockA.CantidadReservada.Should().Be(0);
        stockA.CantidadBloqueada.Should().Be(0);
        stockB.CantidadReservada.Should().Be(0);
        stockB.CantidadBloqueada.Should().Be(0);
        (stockA.CantidadDisponible + stockB.CantidadDisponible).Should().Be(26);
    }

    private static CatalogDbContext CrearDb()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"GrupoA_E2E_{Guid.NewGuid()}")
            .Options;
        return new CatalogDbContext(options);
    }

    private static Producto CrearProducto() => new()
    {
        Id = Guid.NewGuid(),
        Sku = "SKU-F8A-001",
        Nombre = "Producto ensayo integral Grupo A",
        CategoriaNombre = "PRUEBAS",
        Subcategoria = "INTEGRACION",
        PrecioB2C = 100m,
        PrecioB2B = 80m,
        Moneda = "USD",
        StockInicial = 0,
        StockMinimo = 30,
        ProveedorNombre = "Proveedor QA",
        PaisOrigen = "El Salvador",
        PesoKg = 1m,
        DimensionesCm = "30x20x15",
        UnidadMedida = "Unidad",
        EstadoProducto = "Activo",
        CodigoEAN = string.Empty,
        Descripcion = "Registro aislado para ensayo integral",
        VendedorId = Guid.NewGuid(),
        CategoriaId = Guid.NewGuid(),
        EstaPublicado = true,
        FechaCreacion = DateTime.UtcNow
    };

    private static UbicacionAlmacen CrearUbicacion(string codigo, string zona) => new()
    {
        Id = Guid.NewGuid(),
        Codigo = codigo,
        Pasillo = codigo.Contains("PAS-A", StringComparison.Ordinal) ? "A" : "B",
        Rack = "01",
        Nivel = 1,
        Posicion = "01",
        CapacidadMaxima = 100,
        EstaOcupada = false,
        Zona = zona,
        FechaCreacion = DateTime.UtcNow
    };
}
