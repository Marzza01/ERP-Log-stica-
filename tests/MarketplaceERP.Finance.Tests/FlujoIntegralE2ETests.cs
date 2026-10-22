/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

using FluentAssertions;
using MarketplaceERP.Catalog.Data;
using MarketplaceERP.Catalog.Entities;
using MarketplaceERP.Catalog.Services;
using MarketplaceERP.Core.Data;
using MarketplaceERP.Core.Entities;
using MarketplaceERP.Finance.Data;
using MarketplaceERP.Finance.Entities;
using MarketplaceERP.Finance.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MarketplaceERP.Finance.Tests;

/// <summary>
/// Prueba integral End-to-End (E2E) que simula el ciclo de vida comercial y operativo completo:
/// 1. Alta de proveedor salvadoreño y aprovisionamiento (PO).
/// 2. Recepción en almacén WMS y verificación de Match Tridireccional (3-Way Matching).
/// 3. Registro de partida contable de compra con IVA Crédito Fiscal (13%).
/// 4. Venta en Marketplace (B2C con Factura o B2B con CCF) con partida doble.
/// 5. Generación de Pick List con ruta óptima S-Shape en bodega.
/// 6. Confirmación de picking, estación de empaque, cálculo de cubicaje m3 y emisión de guía salvadoreña.
/// 7. Despacho y entrega.
/// 8. Liquidación periódica al vendedor aplicando la regla Split 90/10 y asiento contable automático.
/// </summary>
public class FlujoIntegralE2ETests
{
    [Fact]
    public async Task FlujoCompleto_DesdeAprovisionamientoHastaLiquidacion_EjecutaConExito()
    {
        // ── 0. Infraestructura InMemory por módulo ────────────────────────────
        var dbName = Guid.NewGuid().ToString();

        var coreOptions = new DbContextOptionsBuilder<CoreDbContext>()
            .UseInMemoryDatabase($"Core_{dbName}")
            .Options;
        var catalogOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"Catalog_{dbName}")
            .Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"Finance_{dbName}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var coreDb = new CoreDbContext(coreOptions);
        await using var catalogDb = new CatalogDbContext(catalogOptions);
        await using var financeDb = new FinanceDbContext(financeOptions);

        var matchingService = new TresWayMatchingService(financeDb, NullLogger<TresWayMatchingService>.Instance);
        var contabilidadService = new ContabilidadService(financeDb, NullLogger<ContabilidadService>.Instance);
        var fulfillmentService = new FulfillmentService(catalogDb, NullLogger<FulfillmentService>.Instance);
        var liquidacionService = new LiquidacionService(financeDb, contabilidadService, NullLogger<LiquidacionService>.Instance);

        // ── 1. Alta de Proveedor y Catálogo de Producto ─────────────────────────
        var proveedor = new Proveedor
        {
            Id = Guid.NewGuid(),
            RazonSocial = "Distribuidora Morazán S.A. de C.V.",
            NombreComercial = "Distribuidora Morazán",
            Nit = "0614-010190-101-1",
            Nrc = "123456-7",
            CategoriaSuministro = "Herramientas y Ferretería",
            PlazoCreditoDias = 30
        };
        coreDb.Proveedores.Add(proveedor);
        await coreDb.SaveChangesAsync();

        var vendedorId = Guid.NewGuid();
        var productoId = Guid.NewGuid();
        var ubicacionBodega = new UbicacionAlmacen
        {
            Id = Guid.NewGuid(),
            Codigo = "PAS-A-RCK-01-NIV-01-POS-01",
            Pasillo = "A",
            Rack = "01",
            Nivel = 1,
            Posicion = "01",
            Zona = "AlmacenamientoGeneral"
        };
        catalogDb.UbicacionesAlmacen.Add(ubicacionBodega);

        var stock = new StockFisico
        {
            Id = Guid.NewGuid(),
            ProductoId = productoId,
            CantidadTotal = 100,
            CantidadReservada = 0,
            UbicacionAlmacen = ubicacionBodega,
            UbicacionCodigo = ubicacionBodega.Codigo
        };
        catalogDb.StockFisico.Add(stock);
        await catalogDb.SaveChangesAsync();

        // ── 2. Orden de Compra (PO) y 3-Way Matching ───────────────────────────
        var po = new OrdenCompra
        {
            Id = Guid.NewGuid(),
            NumeroPO = "PO-2026-001",
            ProveedorId = proveedor.Id,
            ProveedorNombre = proveedor.RazonSocial,
            SubTotal = 1000m,
            ImpuestoIva = 130m,
            MontoTotal = 1130m,
            Estado = EstadoOrdenCompra.RecibidaCompleta,
            Items = new List<ItemOrdenCompra>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductoId = productoId,
                    Sku = "SKU-TLD-01",
                    NombreProducto = "Taladro Percutor",
                    CantidadSolicitada = 10,
                    CantidadRecibida = 10,
                    PrecioUnitarioAcordado = 100m
                }
            }
        };
        financeDb.OrdenesCompra.Add(po);

        var facturaProv = new FacturaProveedor
        {
            Id = Guid.NewGuid(),
            NumeroFactura = "CCF-PROV-9988",
            OrdenCompraId = po.Id,
            ProveedorId = proveedor.Id,
            MontoSubTotal = 1000m,
            MontoIva = 130m,
            MontoTotal = 1130m,
            FechaEmision = DateTime.UtcNow
        };
        financeDb.FacturasProveedor.Add(facturaProv);
        await financeDb.SaveChangesAsync();

        // Ejecutar 3-Way Match
        var match = await matchingService.EjecutarMatchAsync(
            new Validar3WayMatchRequest(po.Id, facturaProv.Id, CantidadRecibidaBodega: 10)
        );

        match.Resultado.Should().Be(Estado3WayMatch.Conforme);
        match.AutorizadoParaPago.Should().BeTrue();

        // ── 3. Asiento Contable de Compra (Partida Doble con IVA 13%) ───────────
        var partidaCompra = await contabilidadService.GenerarAsientoCompraAsync(
            po.Id,
            montoCompra: 1000m,
            iva: 130m
        );

        partidaCompra.EstaCuadrada.Should().BeTrue();
        partidaCompra.TotalDebe.Should().Be(1130m);
        partidaCompra.TotalHaber.Should().Be(1130m);

        // ── 4. Venta en Marketplace (Orden de Cliente) ──────────────────────────
        var compradorId = Guid.NewGuid();
        var ordenVenta = new Orden
        {
            Id = Guid.NewGuid(),
            NumeroOrden = "ORD-E2E-2026-001",
            CompradorId = compradorId,
            VendedorId = vendedorId,
            SubTotal = 200m,
            ImpuestoIva = 26m, // 13% IVA
            CostoEnvio = 5m,
            MontoTotal = 231m,
            Estado = EstadoOrden.Aprobada,
            TipoCliente = TipoCliente.B2C,
            TipoComprobante = "FacturaConsumidorFinal",
            DireccionEntrega = "Colonia San Benito, San Salvador",
            Items = new List<ItemOrden>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ProductoId = productoId,
                    NombreProducto = "Taladro Inalámbrico 20V",
                    Cantidad = 2,
                    PrecioUnitario = 100m
                }
            }
        };
        financeDb.Ordenes.Add(ordenVenta);

        // Split de Pago 90/10 asociado
        var splitPago = new SplitPago
        {
            Id = Guid.NewGuid(),
            OrdenId = ordenVenta.Id,
            MontoTotal = 200m,
            PorcentajeVendedor = 90m,
            MontoVendedor = 180m,
            PorcentajeMarketplace = 10m,
            MontoMarketplace = 20m,
            TransaccionId = "TRX-E2E-9988"
        };
        financeDb.SplitsPago.Add(splitPago);
        ordenVenta.SplitPago = splitPago;
        await financeDb.SaveChangesAsync();

        // Asiento de venta
        var partidaVenta = await contabilidadService.GenerarAsientoVentaAsync(
            ordenVenta.Id,
            ordenVenta.SubTotal,
            ordenVenta.ImpuestoIva,
            ordenVenta.TipoComprobante
        );
        partidaVenta.EstaCuadrada.Should().BeTrue();
        partidaVenta.TotalDebe.Should().Be(ordenVenta.MontoTotal);

        // ── 5. Fulfillment: Pick List con Enrutamiento Óptimo S-Shape ─────────
        var pickList = await fulfillmentService.GenerarPickListAsync(new GenerarPickListRequest(
            OrdenId: ordenVenta.Id,
            NumeroOrden: ordenVenta.NumeroOrden,
            OperadorAsignado: "Operador Bodega Central",
            Items: new List<ItemPickingSolicitud>
            {
                new(productoId, "SKU-TLD-20V", "Taladro Inalámbrico 20V", 2)
            }
        ));

        pickList.Should().NotBeNull();
        pickList.Items.First().UbicacionCodigo.Should().Be("PAS-A-RCK-01-NIV-01-POS-01");

        // Confirmar picking
        var pickListConfirmada = await fulfillmentService.ConfirmarPickingAsync(
            pickList.Id,
            new List<ConfirmarItemPickingRequest>
            {
                new(pickList.Items.First().Id, CantidadRecogida: 2)
            }
        );
        pickListConfirmada!.Estado.Should().Be(EstadoPicking.Completado);

        // ── 6. Packing, Cubicaje y Rotulado con Guía Salvadoreña ───────────────
        var paquete = await fulfillmentService.EmpacarYRotularAsync(new EmpacarDespachoRequest(
            OrdenId: ordenVenta.Id,
            NumeroOrden: ordenVenta.NumeroOrden,
            PesoRealKg: 3.2m,
            LargoCm: 35,
            AnchoCm: 25,
            AltoCm: 15,
            Transportista: "LOGIMKT Express",
            DepartamentoDestino: "San Salvador",
            MunicipioDestino: "San Salvador Centro",
            DireccionEntrega: ordenVenta.DireccionEntrega,
            ContactoReceptor: "Cliente B2C",
            TelefonoReceptor: "7888-9999"
        ));

        paquete.NumeroGuia.Should().StartWith("SV-LOGI-");
        paquete.VolumenM3.Should().BeGreaterThan(0);

        // Despacho
        var paqueteDespachado = await fulfillmentService.DespacharPaqueteAsync(paquete.Id);
        paqueteDespachado!.FechaDespacho.Should().NotBeNull();

        // ── 7. Entrega de Orden y Cierre OMS ───────────────────────────────────
        ordenVenta.Estado = EstadoOrden.Entregada;
        ordenVenta.FechaEntregaReal = DateTime.UtcNow;
        ordenVenta.CumplioOTIF = true;
        await financeDb.SaveChangesAsync();

        // ── 8. Liquidación a Vendedor (Split 90/10) con Asiento Contable ───────
        var liquidacionGenerada = await liquidacionService.GenerarLiquidacionAsync(vendedorId);
        liquidacionGenerada.Should().NotBeNull();
        liquidacionGenerada.MontoLiquidar.Should().Be(180m); // 90% de $200
        liquidacionGenerada.Estado.Should().Be(EstadoLiquidacion.Pendiente);

        // Procesar pago de liquidación (asiento 2108 CxP -> 1101 Bancos)
        var liquidacionPagada = await liquidacionService.ProcesarPagoAsync(liquidacionGenerada.Id);
        liquidacionPagada.Estado.Should().Be(EstadoLiquidacion.Completada);

        // Verificar cuadratura final de comprobación contable
        var balance = await contabilidadService.ObtenerBalanceComprobacionAsync();
        balance.EstaCuadrado.Should().BeTrue("el balance final del sistema debe tener Total Debe == Total Haber");
        balance.TotalDebe.Should().Be(balance.TotalHaber);
    }
}
