<!-- ERP desarrollado por Marcelo Zepeda (@Marzza01) | Todos los derechos reservados © 2026 -->

# Registro de Cambios y Mejoras del Proyecto — MarketplaceERP

Este documento centraliza y documenta cada una de las fases, cambios de arquitectura, nuevas entidades, servicios de negocio, endpoints y cobertura de pruebas implementadas en el sistema de acuerdo con los requerimientos logísticos y contables del proyecto.

---

## Índice
1. [Resumen Ejecutivo de Fases](#resumen-ejecutivo-de-fases)
2. [Fase 7a: Checkout Seguro, Idempotencia y Asiento Contable Atómico](#fase-7a-checkout-seguro-idempotencia-y-asiento-contable-atómico)
3. [Fase 1: Núcleo de Catálogo, WMS y Flujo Base](#fase-1-núcleo-de-catálogo-wms-y-flujo-base)
3. [Fase 2: Backbone Financiero, OMS de 6 Estados, Compras, 3-Way Match y Contabilidad General](#fase-2-backbone-financiero-oms-de-6-estados-compras-3-way-match-y-contabilidad-general)
4. [Fase 3: Dashboard Ejecutivo de KPIs, Liquidaciones a Vendedores y Tests Financieros](#fase-3-dashboard-ejecutivo-de-kpis-liquidaciones-a-vendedores-y-tests-financieros)
5. [Fase 4: Dashboard Interactivo y Consola Operativa en Frontend](#fase-4-dashboard-interactivo-y-consola-operativa-en-frontend)
6. [Fase 5: Flujo E2E de Fulfillment, Picking Óptimo, Packing y Despacho](#fase-5-flujo-e2e-de-fulfillment-picking-óptimo-packing-y-despacho)
7. [Fase 6: Infraestructura de Despliegue y Suite de Integración E2E Transversal](#fase-6-infraestructura-de-despliegue-y-suite-de-integración-e2e-transversal)
8. [Fase 7: Torre de Control Operativa de Fulfillment, Simulación WMS y Mapa de Bodega en Frontend](#fase-7-torre-de-control-operativa-de-fulfillment-simulación-wms-y-mapa-de-bodega-en-frontend)
9. [Fase 8: Módulo Interactivo de RMA (Logística Inversa) y Auditoría Visual 3-Way Match en Frontend](#fase-8-módulo-interactivo-de-rma-logística-inversa-y-auditoría-visual-3-way-match-en-frontend)
10. [Matriz de Control y Cobertura de Pruebas](#matriz-de-control-y-cobertura-de-pruebas)

---

## Resumen Ejecutivo de Fases

| Fase | Enfoque Principal | Estado | Pruebas Unitarias / Build |
|---|---|---|---|
| **Fase 1** | Catálogo multicanal, WMS de almacén, recepción, picking y RMA | ✅ Completada | 10 pruebas pasadas |
| **Fase 2** | OMS (6 estados), Procurement, 3-Way Match y Contabilidad (partida doble, IVA 13% El Salvador) | ✅ Completada | Integrado en Core/Finance |
| **Fase 3** | Dashboard de 8 KPIs con semáforos, Split 90/10 de Liquidaciones a vendedores, Modelo Financiero 12 meses | ✅ Completada | 21 pruebas Finance pasadas (37 totales en la solución) |
| **Fase 4** | Consola web ejecutiva glassmorphism (React 18 + TS + Vite), Semáforos de KPIs, Proyección interactiva y Tabla contable | ✅ Completada | Compilación y bundle limpios |
| **Fase 5** | Fulfillment WMS/OMS: Pick Lists con algoritmo de ruta óptima S-Shape, estación de packing con cubicaje real y despacho con guía de última milla | ✅ Completada | 13 pruebas Catalog pasadas (40 totales en la solución) |
| **Fase 6** | Orquestación Docker Compose (PostgreSQL, Backend API, Frontend Nginx) y Suite de Prueba Integral E2E de ciclo de vida comercial completo | ✅ Completada | 22 pruebas Finance pasadas (41 totales en la solución) |
| **Fase 7** | Torre de control de almacén en Frontend: simulación interactiva de órdenes, gestión de Pick Lists en vivo y catálogo WMS con coordenadas (Pasillo/Rack/Nivel) | ✅ Completada | Compilación y bundle limpios |
| **Fase 8** | Mesa de inspección de RMA (ReStock, MermaScrap, Remate) y consola de auditoría 3-Way Match con aprobación de pago para Cuentas por Pagar | ✅ Completada | Compilación y bundle limpios |

---

## Fase 7a: Checkout Seguro, Idempotencia y Asiento Contable Atómico

### 1. Nuevo Contrato Seguro de Checkout (`POST /api/checkout`)
- **Payload Sanitizado:** El cliente envía exclusivamente `items: [{ productoId, cantidad }]`, `direccionEntrega`, `zonaEnvio` y `metodoPago`. El servidor descarta e ignora cualquier parámetro de precios (`precioUnitario`), SKUs o vendedor enviado por el cliente.
- **Precios Protegidos desde Catálogo:** `SubTotal`, `ImpuestoIva` (13%), `CostoEnvio` ($5.00) y `MontoTotal` se calculan en el servidor directamente desde `catalog.productos`, aplicando redondeo a 2 decimales (`MidpointRounding.AwayFromZero`). Los precios B2C o B2B se definen según el tipo de cliente determinado por el servidor.
- **Validaciones:**
  - Cantidad > 0 y máximo 1,000 unidades por ítem.
  - Productos existentes y publicados (`EstaPublicado = true`).
  - Regla Marketplace: Una orden = un solo vendedor (si hay productos de distintos vendedores, rechaza con HTTP 400).

### 2. Idempotencia y Transaccionalidad
- **Header `Idempotency-Key` Obligatorio:** Se valida al inicio (HTTP 400 si falta).
- **Tabla `finance.idempotency_keys`:** Registra `(Key, UsuarioId, RequestHash, StatusCode, ResponseJson, FechaCreacion, FechaExpiracion)` con índice único `(UsuarioId, Key)`.
  - Misma clave + mismo request: Retorna la misma respuesta previa sin duplicar cobros ni órdenes.
  - Misma clave + distinto request: Retorna HTTP 409 Conflict.
  - Expiración documentada: 24 horas.
- **Transacción Atómica Única:** Creación de la orden, procesamiento de la saga (reserva de stock, cobro, split) y generación del asiento contable de venta se ejecutan dentro de una misma transacción en PostgreSQL.
- **Rollback por Pago Fallido:** Si la pasarela rechaza el cobro, la transacción revierte completamente la orden y el asiento contable (no quedan registros huérfanos).
- **Estado de la Orden:** La orden tras el cobro exitoso queda en estado `Aprobada` (pagada) y **NUNCA** `Entregada`. La orden pasa a `Entregada` únicamente mediante fulfillment y el endpoint de transición de estados por un Administrador o Vendedor.
- **Integración con Liquidaciones:** El servicio de liquidaciones toma la orden únicamente después de ser marcada como `Entregada`.

### 3. Pasarela de Pagos
- Fallo simulado del mock condicionado a `Payments:SimulateFailures` únicamente en entornos `Development` y `Test`.
- En entorno `Production`, si no hay pasarela real configurada, rechaza con HTTP 503 Service Unavailable y mensaje claro salvo que `Payments:AllowMock=true` esté explícitamente configurado.
- Reembolso automático (`IPaymentGateway.ReembolsarAsync`) ante compensación de la saga tras cobro exitoso; si la pasarela falla o no dispone de API de reembolso, se persiste un registro en `finance.pagos_conciliacion_huerfanos` para auditoría y conciliación financiera.

### 4. Decisiones de Negocio y Pendientes Documentados
- **Costo de Envío:** Establecido por defecto en $5.00 (`Checkout:CostoEnvioDefault`), configurable mediante appsettings / variables de entorno. Queda **pendiente de definición de negocio** la tarificación dinámica según peso/zona/transportista de última milla.
- **Tipo de Cliente:** Determinado exclusivamente por el backend como `B2C` para compradores estándar. El flujo `B2B` (Crédito Fiscal CCF, condiciones de crédito 30/60 días) queda **pendiente de integración** de validaciones fiscales y de crédito.
- **Reembolso Real de Pasarela:** La interfaz `IPaymentGateway` cuenta con el método `ReembolsarAsync` y la tabla `finance.pagos_conciliacion_huerfanos`. Queda **pendiente de negocio e infraestructura** la integración con las APIs oficiales de pasarelas reales (ej: Pagadito, Wompi, Serfinsa, Stripe) y su webhook de reverso.
- **Dependencia `Microsoft.Extensions.Hosting.Abstractions` en `MarketplaceERP.Finance`:** Requerida por `IHostEnvironment` en `PagoServiceMock` para verificar de forma segura y tipada si el entorno es `Production` (`_environment.IsProduction()`).

### 5. Procedimiento Manual de Conciliación de Pagos Huérfanos
Cuando un checkout realiza el cobro en la pasarela de pagos pero la transacción posterior en base de datos falla (por ejemplo por excepción contable, desconexión o fallo en persistencia) y la pasarela no admite reembolso automático:
1. **Detección y Monitoreo:**
   - La API registra el incidente con nivel `CRITICAL/ERROR` y persiste los datos del cobro en la tabla `finance.pagos_conciliacion_huerfanos`.
   - El equipo de Finanzas/Auditoría consulta los pagos sin resolver a través del endpoint seguro `GET /api/contabilidad/pagos-huerfanos?soloSinResolver=true` (restringido al rol `Admin`).
2. **Verificación en Portal de Pasarela:**
   - El analista financiero toma el `TransaccionId`, `Monto` y `FechaRegistro` reportados en la tabla.
   - Accede a la consola de la pasarela bancaria (ej: portal de Pagadito, Wompi, Serfinsa) y localiza la transacción cobrada al cliente.
3. **Resolución:**
   - **Opción A (Reverso Directo en Portal):** Se procesa el reembolso manual al tarjetahabiente a través del portal de la pasarela. Al confirmarse el reverso bancario, se actualiza el registro en `finance.pagos_conciliacion_huerfanos` marcando `Reembolsado = true`, `ReembolsoTransaccionId = <ID_REVERSO>` y `FechaResolucion = UtcNow`.
   - **Opción B (Regeneración de Pedido):** Si el cliente solicita conservar su compra, se asocia el cobro a una nueva orden manual y se genera el asiento de ajuste contable correspondiente, cerrando el registro con notas de auditoría.

---

## Fase 1: Núcleo de Catálogo, WMS y Flujo Base

### Componentes y Mejoras:
1. **Catálogo & Gestión de Inventario:**
   - Registro de productos con SKUs normalizados, códigos de barra, dimensiones y pesos para cubicaje.
   - Manejo de categorías de productos y atributos flexibles.
2. **WMS (Warehouse Management System):**
   - Entidades de bodegas físicas, zonas (recepción, almacenaje, picking) y ubicaciones tridimensionales (pasillo, estantería, nivel).
   - Control de lotes, números de serie y fechas de caducidad.
3. **Flujos Operativos de Almacén:**
   - Movimientos de inventario con trazabilidad (entradas, transferencias internas, ajustes por conteo cíclico).
   - Registro de devoluciones (RMA) con inspección y retorno a inventario o desecho.

---

## Fase 2: Backbone Financiero, OMS de 6 Estados, Compras, 3-Way Match y Contabilidad General

### 1. Modelo de Datos y Entidades
- **`Proveedor`** (`MarketplaceERP.Core/Entities/Proveedor.cs`):
  - Razón social, NIT, NRC, giro de negocio, dirección salvadoreña, condición de pago (días crédito), evaluación histórica de OTIF.
- **`Orden` (OMS 6 Estados)** (`MarketplaceERP.Finance/Entities/Orden.cs`):
  - Estados del ciclo de vida del pedido:
    1. `Recibido`
    2. `Confirmado`
    3. `EnPreparacion`
    4. `ListoParaEnvio`
    5. `Enviado`
    6. `Entregado` (y posterior `Cerrado`).
  - Diferenciación de cliente: B2C (Factura Consumidor Final) y B2B (Comprobante de Crédito Fiscal - CCF).
  - Trazabilidad temporal de cada transición de estado para medición de SLA y Lead Time.
- **`OrdenCompra` & `ItemOrdenCompra`** (`MarketplaceERP.Finance/Entities/`):
  - Gestión de aprovisionamiento de stock con estados (`Borrador`, `Emitida`, `RecibidaParcial`, `RecibidaCompleta`, `Cancelada`).
- **`FacturaProveedor` & `MatchTridireccional`** (`MarketplaceERP.Finance/Entities/`):
  - Registro de facturas de proveedores salvadoreños y auditoría tridireccional.
- **`PartidaContable` & `LineaPartidaContable`** (`MarketplaceERP.Finance/Entities/`):
  - Estructura para el Libro Diario bajo partida doble salvadoreña:
    - 1101: Caja y Bancos
    - 1105: Cuentas por Cobrar Comerciales
    - 1108: Inventario de Mercaderías
    - 2101: Cuentas por Pagar Proveedores
    - 2105: IVA Débito Fiscal (13%)
    - 1109: IVA Crédito Fiscal (13%)
    - 2108: Cuentas por Pagar a Vendedores (Marketplace)
    - 4101: Ingresos por Venta / Comisión
    - 5101: Costo de Mercadería Vendida

### 2. Servicios de Negocio
- **`TresWayMatchingService`** (`MarketplaceERP.Finance/Services/`):
  - Algoritmo de conciliación automática entre:
    1. Orden de Compra original
    2. Recepción de almacén
    3. Factura del proveedor
  - Detección de discrepancias en cantidades recibidas vs facturadas y variaciones en precios unitarios pactados.
- **`ContabilidadService`** (`MarketplaceERP.Finance/Services/`):
  - Generación automatizada del asiento contable al aprobar venta (Debe: Caja/CxC; Haber: Ingresos e IVA Débito 13%).
  - Asiento de compra al aprobar 3-Way Match (Debe: Inventario e IVA Crédito 13%; Haber: Cuentas por Pagar Proveedores).
  - Consulta de Libro Diario, Libro Mayor y Balance de Comprobación con validación estricta de cuadratura contable.

### 3. Controladores API
- **`OmsController`**:
  - `PATCH /api/oms/ordenes/{id}/estado`: Transición controlada por máquina de estados.
  - `POST /api/oms/ordenes/{id}/asignar-envio`: Asignación de guía y operador logístico.
  - `GET /api/oms/ordenes`: Consulta con filtros multi-criterio.
- **`ComprasController`**:
  - `GET /api/compras/proveedores`: Catálogo de proveedores.
  - `POST /api/compras/ordenes`: Creación y seguimiento de POs.
  - `POST /api/compras/match-3way`: Ejecución del match tridireccional.
- **`ContabilidadController`**:
  - `GET /api/contabilidad/libro-diario`: Partidas cronológicas.
  - `GET /api/contabilidad/libro-mayor`: Saldos y movimientos acumulados.
  - `GET /api/contabilidad/balance`: Comprobación de sumas iguales.

---

## Fase 3: Dashboard Ejecutivo de KPIs, Liquidaciones a Vendedores y Tests Financieros

### 1. Liquidaciones a Vendedores (Split 90/10)
- **`ILiquidacionService` & `LiquidacionService`**:
  - Cálculo de retención automática: 90% para el vendedor, 10% de comisión para la plataforma marketplace.
  - Agrupación por vendedor de órdenes entregadas en un período contable.
  - Procesamiento de pago con asiento automático en partida doble:
    - Debe: 2108 (Cuentas por Pagar a Vendedores)
    - Haber: 1101 (Caja y Bancos)
  - Métricas individuales por vendedor: tasa de cumplimiento (Fill Rate), total liquidado, saldo retenido y pendiente.
- **`LiquidacionesController`**:
  - `POST /api/liquidaciones/generar`: Generación periódica de liquidaciones.
  - `POST /api/liquidaciones/{id}/procesar-pago`: Ejecución y contabilización del pago.
  - `GET /api/liquidaciones/vendedor/{vendedorId}`: Historial y liquidaciones activas.
  - `GET /api/liquidaciones/kpis/vendedor/{vendedorId}`: Métricas de vendedor con semáforo.

### 2. Dashboard Ejecutivo de 8 KPIs Logísticos
- **`KpisDashboardController`**:
  - Provee visión en tiempo real para gerencia de operaciones y finanzas con semáforos estandarizados (`Verde`, `Amarillo`, `Rojo`):
    1. **Fill Rate (% Cumplimiento de Pedidos)**: Meta ≥ 95%.
    2. **OTIF (% A Tiempo y Completo)**: Meta ≥ 90%.
    3. **Tiempo de Ciclo de Pedido (Order Lead Time)**: Meta ≤ 24 hrs.
    4. **Tasa de Devolución RMA (% sobre ventas)**: Meta ≤ 2%.
    5. **Rotación de Inventario**: Salud de rotación de stock.
    6. **Efectividad de 3-Way Match**: Meta ≥ 98% sin discrepancias.
    7. **Margen de Comisión Marketplace**: Control del 10% medio.
    8. **Días Promedio de Pago a Proveedores (DPO)**: Meta 30 a 60 días.
  - **Modelo Financiero de 12 Meses**:
    - `GET /api/kpis/dashboard/proyeccion-financiera`: Genera proyección mensual escalable (ingresos brutos, comisiones 10%, costos operativos de fulfillment, margen neto) exportable o consumible directamente en Excel y BI.

---

## Fase 4: Dashboard Interactivo y Consola Operativa en Frontend

### 1. Sistema de Diseño e Interfaz Glassmorphism
- **`frontend/marketplace-ui/src/index.css`**:
  - Tema oscuro de alta fidelidad con efectos de desenfoque (`backdrop-filter: blur(16px)`), gradientes modernos (`#6366F1` a `#A855F7`), sombras volumétricas y semáforos cromáticos (`Verde #10B981`, `Amarillo #F59E0B`, `Rojo #EF4444`).
  - Adaptabilidad y diseño responsivo para monitores de control de operaciones logísticas.

### 2. Componente Principal y Funcionalidades
- **`frontend/marketplace-ui/src/App.tsx`**:
  - **Pestaña 1: Semáforo de KPIs Logísticos**:
    - Tarjetas reactivas con los 8 indicadores clave (Fill Rate 97.4%, OTIF 92.8%, Lead Time 18.2h, RMA 1.6%, Rotación 8.4x, 3-Way Match 98.6%, Comisión 10%, DPO 42 días) mostrando badges con semáforo dinámico.
    - Integración con el endpoint `/api/kpis/dashboard?dias=30` y modo presentación asistido.
  - **Pestaña 2: Proyección Financiera 12 Meses**:
    - Tabla comparativa mensual proyectando órdenes crecientes, GMV transaccionado, retención de comisión (10%), costos de fulfillment y margen operativo neto.
    - Botón de descarga/exportación directa hacia el backend.
  - **Pestaña 3: Mapeo de Partida Doble (El Salvador)**:
    - Guía visual interactiva de las cuentas del catálogo contable (`1101`, `1108`, `1109`, `2101`, `2105`, `2108`, `4101`), su naturaleza y los eventos logísticos que las disparan.

### 3. Validación y Empaquetado
- Compilación de TypeScript estricto y empaquetado de producción con Vite (`tsc --noEmit && vite build`) verificado sin fallas.

---

## Fase 5: Flujo E2E de Fulfillment, Picking Óptimo, Packing y Despacho

### 1. Entidades de Dominio WMS/Fulfillment
- **`PickList` & `ItemPickList`** (`MarketplaceERP.Catalog/Entities/PickList.cs`):
  - Número de lista trazable (`PKL-YYYYMMDD-XXXX`), asignación de operador de almacén, estado de recolección (`Pendiente`, `EnProceso`, `Completado`).
  - Items con mapeo a coordenadas WMS (Pasillo, Rack, Nivel, Posición) para optimización de recorrido de picking.
- **`PaqueteDespacho`** (`MarketplaceERP.Catalog/Entities/PaqueteDespacho.cs`):
  - Estación de packing: control de peso real en kilogramos, dimensiones físicas (`Largo x Ancho x Alto` en cm) y volumen real calculado en metros cúbicos ($m^3$).
  - Rotulado con guía de última milla salvadoreña (`SV-LOGI-YYYYMMDD-XXXXX`) y datos de transportista y receptor en El Salvador.

### 2. Lógica de Negocio y Enrutamiento Óptimo
- **`FulfillmentService`** (`MarketplaceERP.Catalog/Services/FulfillmentService.cs`):
  - **Algoritmo de Ruta S-Shape**: Ordenamiento automático de los ítems de recolección priorizando `Pasillo ➔ Rack ➔ Nivel ➔ Posición` para minimizar el tiempo de desplazamiento de los operarios en bodega.
  - **Confirmación Dinámica**: Actualización del progreso de recolección en tiempo real y cierre de la lista al alcanzar el 100%.
  - **Packing y Despacho**: Cálculo de cubicaje volumétrico y emisión de guías de envío.

### 3. Endpoints de la API
- **`FulfillmentController`** (`MarketplaceERP.API/Controllers/FulfillmentController.cs`):
  - `POST /api/fulfillment/ordenes/{ordenId}/generar-picklist`: Genera la lista de recolección y avanza la orden en el OMS a `EnProceso`.
  - `POST /api/fulfillment/picklists/{pickListId}/confirmar`: Confirma la recolección física en bodega.
  - `POST /api/fulfillment/ordenes/{ordenId}/empacar`: Empaca, cubicita y genera la guía de última milla.
  - `POST /api/fulfillment/paquetes/{paqueteId}/despachar`: Despacha el paquete y avanza el estado de la orden en el OMS a `Enviada`.
  - `GET /api/fulfillment/picklists`: Monitoreo de listas de picking activas.
  - `GET /api/fulfillment/paquetes`: Monitoreo de paquetes en tránsito.

---

## Fase 6: Infraestructura de Despliegue y Suite de Integración E2E Transversal

### 1. Orquestación y Contenedorización (`docker-compose.yml`)
- **Base de Datos**: PostgreSQL 15 (`marketplace_db`) con persistencia por volumen, healthcheck y credenciales seguras de entorno.
- **Backend API**: Contenedor multi-stage .NET 8 SDK ➔ ASP.NET Core Runtime (`marketplace_api`), auto-seeding de los 500 SKUs de catálogo con WMS dinámico y Swagger UI en desarrollo.
- **Frontend Nginx**: Contenedor multi-stage Node 20 ➔ Nginx Alpine (`marketplace_frontend`), compresión Gzip, caché de assets estáticos y proxy inverso a `/api/` en puerto `3000:80`.
- **Configuración Nginx**: Configuración dedicada en `frontend/marketplace-ui/nginx.conf` para empaquetado autónomo sin dependencias externas.

### 2. Prueba Integral End-to-End (E2E)
- **`FlujoIntegralE2ETests.cs`** (`MarketplaceERP.Finance.Tests/FlujoIntegralE2ETests.cs`):
  - Ejecuta de forma integrada y secuencial todo el ciclo operacional y financiero:
    1. Registro y parametrización de Proveedor local en El Salvador (`Distribuidora Morazán S.A. de C.V.`).
    2. Creación de Orden de Compra (PO) y recepción física en bodega WMS (`Pasillo A, Rack 01, Nivel 01`).
    3. Emisión de Factura de Proveedor y validación por **Match Tridireccional** (`Resultado: Conforme`, autorizado para pago).
    4. Asiento automático de compra con partida doble e **IVA Crédito Fiscal (13%)**.
    5. Venta B2C en Marketplace y generación de asiento de venta con **IVA Débito Fiscal (13%)**.
    6. Generación de **Pick List** con algoritmo de ordenamiento S-Shape por coordenadas de almacén.
    7. Confirmación de recolección física en bodega.
    8. Estación de packing: control de peso en kg, cálculo volumétrico en $m^3$ y emisión de **Guía de Remisión Salvadoreña** (`SV-LOGI-YYYYMMDD-XXXXX`).
    9. Despacho y actualización a `Enviada` y `Entregada` cumpliendo métrica OTIF.
    10. Liquidación periódica al vendedor aplicando la regla **Split 90/10** (90% vendedor, 10% comisión retenida).
    11. Desembolso bancario con asiento contable (`2108 Pasivos Vendedores ➔ 1101 Caja y Bancos`).
    12. Verificación de cuadratura perfecta del **Balance de Comprobación** (`Total Debe == Total Haber`).

---

## Fase 7: Torre de Control Operativa de Fulfillment, Simulación WMS y Mapa de Bodega en Frontend

### 1. Panel de Control WMS en React (`frontend/marketplace-ui/src/App.tsx`)
- **Pestaña "Fulfillment & WMS"**:
  - **Simulación Interactiva de Órdenes**: Botón en vivo que genera automáticamente una nueva orden de venta simulada en el WMS con su correspondiente `PickList` (`PKL-YYYYMMDD-XXXX`), asignación a operador de bodega y ruta de desplazamiento calculada.
  - **Gestión Operativa de Pick Lists**: Tabla de seguimiento en tiempo real con estados (`Pendiente`, `EnProceso`, `Completado`), conteo de ítems recolectados y botón de acción directa para confirmar la recolección física en almacén.
  - **Mapa de Ubicaciones y Catálogo Dinámico**:
    - Visualización de productos con precios B2C/B2B y niveles de stock físico con alerta visual cuando el stock cae por debajo del punto mínimo de reorden.
    - Asignación espacial detallada en coordenadas WMS: `Pasillo`, `Rack`, `Nivel` y `Posición`.
    - Campo de filtrado dinámico en tiempo real por SKU, nombre de producto o categoría.

### 2. Verificación de Compilación Frontend
- Compilación de producción con Vite (`tsc --noEmit && vite build`): **100% exitosa** (0 errores de TypeScript, bundles optimizados en `dist/assets/`).

---

## Fase 8: Módulo Interactivo de RMA (Logística Inversa) y Auditoría Visual 3-Way Match en Frontend

### 1. Mesa Técnica de Logística Inversa y Devoluciones (RMA) (`App.tsx`)
- **Pestaña "🔄 RMA & Inversa"**:
  - Triage y dictamen pericial directo en interfaz: clasificación de solicitudes en **ReStock** (reingreso a inventario activo en bodega), **MermaScrap** (baja contable por daño) o **Remate** (descuento del 30% en canal de liquidación).
  - Trazabilidad del impacto contable y operativo en tiempo real: generación de contra-asientos, emisión de notas de crédito y ajuste de inventarios en la cuenta `1108` o imputación a `5101` (Pérdidas operativas por merma).

### 2. Tablero de Auditoría Tridireccional (3-Way Matching) (`App.tsx`)
- **Pestaña "🔍 3-Way Match"**:
  - Comparador simultáneo de tres columnas en paralelo: **PO Emitida**, **Recepción Física en Bodega (WMS)** y **Factura Recibida del Proveedor**.
  - Alerta cromática instantánea de discrepancias (`Ninguna`, `Cantidad`, `Precio`, `Ambas`).
  - Botón de **Aprobación de CxP**: permite a supervisores financieros aprobar manualmente órdenes con pequeñas variaciones autorizadas o enviar la orden al estado conforme para programación de pago con crédito local.

---

## Fase 3 (Seguridad & Frontend): Eliminación del Bypass de Autenticación y Route Guards por Rol

### 1. Eliminación de Tokens Mock y Cuentas Falsas
- **`AuthContext.tsx`**:
  - Eliminado el token permanente `'permanent-admin-token-logimkt'` y la cuenta falsa por defecto.
  - Validación de expiración de sesión (`expiresAt` y claim `exp` de JWT) al cargar la aplicación; si la sesión expiró o es inválida, se limpian los datos en almacenamiento local.
  - Eliminado el bypass de credenciales en el bloque catch: si el backend no responde o rechaza la autenticación, se lanza un error y **no** se genera sesión ficticia.
- **`LoginPage.tsx`**:
  - Campos de correo electrónico y contraseña inicializados vacíos por defecto (`""`), sin credenciales expuestas en el código fuente.
  - Mensajes de error específicos: mensaje genérico para 401 (*"Correo electrónico o contraseña incorrectos."*), mensaje específico para 429 (*"Demasiados intentos fallidos. Has alcanzado el límite de seguridad..."*) y para red caída (*"No es posible conectar con el servidor de autenticación..."*).

### 2. Cliente HTTP y Manejo Centralizado de Respuestas
- **`client.ts`**:
  - Inyección del token Bearer en el encabezado `Authorization` para cada petición.
  - Intercepción ante HTTP 401: ejecución de logout y redirección a `/login` (excepto en `/api/auth/login`).
  - Intercepción ante HTTP 403: notificación de acceso denegado sin destruir la sesión activa del usuario.
  - Intercepción ante HTTP 429: mensaje claro de tasa límite excedida.
  - Consumo exclusivo de URL base desde la variable de entorno `VITE_API_URL` (sin URLs hardcodeadas).

### 3. Matriz de Autorización y Guards de Ruta
- **`App.tsx` & `RoleRouteGuard`**:
  - **Solo Admin**: Contabilidad (`/contabilidad`), Compras / 3-Way Match (`/compras`), Dashboard KPIs (`/dashboard`).
  - **Admin y Vendedor**: Órdenes (`/ordenes`), WMS / Bodega (`/wms`), Fulfillment (`/fulfillment`), RMA (`/rma`), Productos (`/productos`, con escritura permitida).
  - **Admin y Vendedor**: Liquidaciones (`/liquidaciones`), filtrando las liquidaciones para que el Vendedor únicamente visualice las propias (`vendedorId`).
  - **Solo Comprador**: Checkout y Catálogo de Compra (`/checkout`).
    > [!NOTE]
    > **Aviso de Implementación Provisional (`CheckoutPage.tsx`):**
    > La vista y payload de `CheckoutPage.tsx` (que envía `{ direccionEnvio, zonaEnvio, metodoPago, items }` al endpoint `/api/checkout`) es una implementación provisional para soporte de interfaz y rol de Comprador. El contrato definitivo del endpoint de checkout y su flujo transaccional se rehará formalmente en la FASE 7.
  - Redirección automática a pantalla amigable **"Sin Acceso"** (`SinAccesoPage.tsx`) ante rutas no autorizadas (evitando pantallas en blanco).
- **`Sidebar.tsx`**:
  - Ocultación dinámica de enlaces y secciones de navegación según el rol del usuario autenticado.

### 4. Riesgo de Seguridad Documentado: Almacenamiento de Token en localStorage
> [!WARNING]
> **Riesgo Documentado (Almacenamiento de Token JWT):**
> En esta fase, el token JWT se almacena temporalmente en el `localStorage` del navegador.
> - **Riesgo:** Los tokens almacenados en `localStorage` son accesibles mediante scripts del lado del cliente, haciéndolos vulnerables a ataques de Cross-Site Scripting (XSS) si existiera alguna brecha en el renderizado de datos externos.
> - **Mitigación futura recomendada:** Migrar la persistencia de autenticación a **cookies `httpOnly`, `Secure` y `SameSite=Strict`**, lo cual requerirá ajustes coordinados en los endpoints de autenticación del backend ASP.NET Core (`Set-Cookie` con refresh tokens).

---

## Fase 4: Liquidaciones 1 a N sin Pago Doble, Concurrencia y Bloqueo Pesimista

### 1. Modelo de Datos y Relación 1:N (`SplitPago` -> `Liquidacion`)
- Eliminada la columna `SplitPagoId` en la tabla `finance.liquidaciones`.
- Añadida la columna `LiquidacionId` (UUID nullable) con índice en `finance.splits_pago`, con clave foránea hacia `finance.liquidaciones` con regla de borrado `ON DELETE RESTRICT` para proteger la integridad contable.
- Migración de datos automatizada en EF Core: migra el histórico preservando los vínculos 1 a 1 preexistentes antes de descartar la columna anterior.
- Token de concurrencia optimista mediante el pseudocampo nativo `xmin` de PostgreSQL (`uint Version` mapeado con `IsRowVersion()` en Npgsql).

### 2. Prevención de Carreras Críticas en Liquidaciones
- **`GenerarLiquidacionAsync` (Bloqueo Pesimista):** Se implementó `SELECT ... FOR UPDATE OF s` sobre los registros candidatos de `finance.splits_pago` dentro de una transacción atómica. Si dos ejecuciones simultáneas intentan generar liquidación para el mismo vendedor, la segunda se bloquea hasta que la primera confirma, procesando 0 splits pendientes y retornando conflicto o excepción de falta de saldos pendientes, garantizando que ningún split pueda quedar asignado a dos liquidaciones distintas.
- **`ProcesarPagoAsync` (Transacción Atómica y Concurrencia Optimista):** El cambio de estado de la liquidación a `Completada` y el asiento contable de desembolso (`DEBE 2108 / HABER 1101`) se ejecutan dentro de la misma transacción atómica con validación de estado previo y token `xmin`. Dos llamadas concurrentes resultan en exactamente una liquidación pagada y un rechazo por conflicto (HTTP 409).

### 3. Códigos HTTP Restituidos en API
- `POST /api/liquidaciones/generar`: Retorna `409 Conflict` cuando el vendedor no tiene saldos pendientes.
- `POST /api/liquidaciones/procesar-pago/{id}`: Retorna `404 NotFound` si la liquidación no existe, o `409 Conflict` si ya fue procesada previamente o colisiona por concurrencia (`DbUpdateConcurrencyException`).

### 4. Riesgo de Datos Históricos: Splits Huérfanos
> [!WARNING]
> **Riesgo Operativo en Producción (Splits Huérfanos):**
> En bases de datos existentes donde operaba el modelo anterior (1 liquidación a 1 split), si un vendedor acumuló múltiples órdenes entregadas pero solo se vinculó un split por liquidación, los splits restantes permanecen con `LiquidacionId IS NULL`.
> Al ejecutar la nueva lógica 1 a N, `GenerarLiquidacionAsync` consolidará y liquidará todos los splits pendientes de ese vendedor.
> **Mitigación obligatoria:** Antes de habilitar la generación en bases de datos con histórico previo, debe ejecutarse el script de solo lectura `docs/sql/auditoria_splits_huerfanos.sql` para identificar y validar los splits huérfanos antes de emitir nuevos pagos.

---

## Fase 5: Contabilidad Centralizada, Secuencia PostgreSQL, Split de Checkout Coherente (Opción A)

### 1. Plan de Cuentas Fijo en Código (Catálogo Estándar de 9 Cuentas)
- Se consolidó y centralizó el plan contable salvadoreño en [`ContabilidadService.cs`](file:///c:/Users/Marcelo/Documents/Logistica%20(ERP)/MarketplaceERP/src/MarketplaceERP.Finance/Services/ContabilidadService.cs) como un catálogo fijo en código compuesto por 9 cuentas:
  - `1101` Caja y Bancos (Activo)
  - `1102` Cuentas por Cobrar Pasarela de Pagos (Activo)
  - `1103` Cuentas por Cobrar Clientes (Activo)
  - `1105` Inventario para la Venta (Activo)
  - `2101` Cuentas por Pagar Proveedores (Pasivo)
  - `2105` Débito Fiscal IVA 13% (Pasivo)
  - `2108` Pasivo por Liquidaciones Vendedores Marketplace (Pasivo)
  - `4101` Ingresos por Ventas / Comisiones Marketplace (Ingreso)
  - `5101` Costo de Mercadería Vendida (Gasto/Costo)
- Validaciones estrictas: prohibición de montos simultáneos en Debe y Haber en una misma línea, verificación de cuentas válidas contra el catálogo, comprobación de balance exacto (tolerancia cero en redondeo) y generación centralizada de todas las partidas del sistema (`IContabilidadService`).

### 2. Idempotencia y Secuencia Correlativa PostgreSQL
- Correlativo único mediante secuencia nativa `finance."PartidaContableNumeroSeq"` (`AST-yyyyMMdd-00000001`).
- Índice único compuesto `(TipoOperacion, ReferenciaOrigenId)` para idempotencia en asientos de venta, compra y liquidación.
- Manejo refinado de concurrencia: si ocurre una colisión PostgreSQL `23505` dentro de una transacción activa (ej. `ProcesarPagoAsync`), se propaga la excepción hacia arriba para forzar el rollback y mapear a HTTP 409 Conflict. Si no hay transacción activa, se recupera y retorna la partida ganadora.

### 3. Split de Pagos en Checkout y Coherencia Contable (Opción A)
- **Cálculo de Split en CheckoutOrchestrator:**
  - Comisión de intermediación calculada sobre el **SubTotal** de la orden (sin IVA ni Costo de Envío).
  - `MontoVendedor` = `SubTotal − comisión`.
  - `MontoMarketplace` = `comisión + CostoEnvio`.
  - El IVA (`ImpuestoIva`) completo va a la cuenta `2105` (Débito Fiscal) y no pertenece al vendedor.
  - Validación de integridad en el orquestador: `MontoVendedor + MontoMarketplace + ImpuestoIva == MontoTotal` con tolerancia cero antes de persistir.
- **Asiento de Venta:**
  - **Debe:** Cuenta de Activo (`1101`/`1102`/`1103`) por el `MontoTotal` cobrado (incluye envío e IVA).
  - **Haber `2108`:** `split.MontoVendedor` (obligación a liquidar al vendedor).
  - **Haber `2105`:** `orden.ImpuestoIva` íntegro (IVA Débito Fiscal de la venta).
  - **Haber `4101`:** Residuo exacto (`MontoTotal − 2108 − 2105`), equivalente a comisión marketplace + costo de envío. Lanza `InvalidOperationException` si resulta negativo.
- **DECISIÓN-PENDIENTE / Validar con Contador:**
  - (a) Tratamiento tributario si la comisión marketplace genera débito fiscal adicional y mecanismo de emisión de comprobante de retención / factura por servicios de intermediación al vendedor.
  - (b) Clasificación del `CostoEnvio` dentro de la cuenta `4101` vs cuenta separada de ingresos por transporte/flete.

---

## Fase 8: Numeración Única Secuencial y Concurrencia Optimista

### 1. Numeración Correlativa Basada en Secuencias de Base de Datos
- **Eliminación de Aleatoriedad y Colisiones:** Se eliminaron las llamadas a `Random.Shared.Next(...)` y `Count()+1` para la generación de identificadores de negocio.
- **Secuencias Dedicadas en PostgreSQL:**
  - `finance."NumeroOrdenSeq"`: Genera identificadores con formato `ORD-yyyyMMdd-NNNNN` (mínimo 5 dígitos, expansible).
  - `finance."NumeroPOSeq"`: Genera identificadores con formato `PO-yyyyMMdd-NNNN` (mínimo 4 dígitos, expansible).
  - `catalog."NumeroPickListSeq"`: Genera identificadores con formato `PKL-yyyyMMdd-NNNN` (mínimo 4 dígitos, expansible).
  - `catalog."NumeroGuiaSeq"`: Genera identificadores con formato `SV-LOGI-yyyyMMdd-NNNNN` (mínimo 5 dígitos, expansible).
- **Servicio Centralizado `INumeracionService`:** Implementado en `MarketplaceERP.API.Services.NumeracionService` e inyectado en `CheckoutController`, `OrdenesController`, `ComprasController` y `FulfillmentService`.
- **Comportamiento ante Fallos:** No existe fallback silencioso; si PostgreSQL falla al avanzar la secuencia, la excepción se propaga. Para tests unitarios no relacionales (InMemory), se suministran contadores atómicos `Interlocked` independientes sin afectar la lógica de producción.
- **Índices Únicos:** Migraciones dedicadas aplican restricciones e índices `UNIQUE` sobre `NumeroOrden`, `NumeroPO`, `NumeroPickList` y `NumeroGuia`.

> [!NOTE]
> **Comportamiento de Secuencias PostgreSQL y Huecos Correlativos:**
> Las secuencias de PostgreSQL (`nextval`) avanzan de manera atómica fuera del alcance de las transacciones ACID convencionales.
> Por diseño del motor relacional, si una transacción que consumió un valor de secuencia realiza un `ROLLBACK`, dicho valor no se retrocede ni se reutiliza para evitar bloqueos y condiciones de carrera entre transacciones concurrentes.
> En consecuencia, pueden presentarse saltos o huecos numéricos en el correlativo en caso de errores o cancelaciones en checkout o creación de órdenes. Si por requerimientos de la administración tributaria salvadoreña el correlativo contable o fiscal exige una serie continua estricta y sin saltos, se debe validar con el contador la implementación de una tabla de control secuencial con bloqueo pesimista `SELECT ... FOR UPDATE` o numeración diferida al momento de la emisión formal del comprobante fiscal.

### 2. Concurrencia Optimista en Entidades Transaccionales
- **Tokens de Concurrencia (`xmin` / `IsRowVersion`):**
  - Mapeado en `Orden` (`finance.ordenes`), `OrdenCompra` (`finance.ordenes_compra`) y `PickList` (`catalog.PickLists`).
  - `Liquidacion` ya contaba con `IsRowVersion`.
  - **StockFisico:** No utiliza token de concurrencia optimista dado que todas las reservas, liberaciones y consumos se ejecutan de manera atómica directa a nivel SQL (`UPDATE catalog.stock_fisico SET ... WHERE ... AND (CantidadTotal - CantidadReservada) >= @cant`).
- **Manejo Global de Concurrencia:** Filtro global `ConcurrencyExceptionFilter` (`IExceptionFilter`) registrado en el pipeline MVC que captura `DbUpdateConcurrencyException` y responde de manera estandarizada con `HTTP 409 Conflict` sin exponer detalles internos de la base de datos.
- **Protección de Máquina de Estados:** En transiciones concurrentes de órdenes (ej. `Cancelada` vs `Enviada`), la primera transacción en confirmar gana y la segunda recibe `409 Conflict`. La liberación de stock en cancelación solo se ejecuta tras confirmar la persistencia de la orden en la BD, evitando desalineaciones de inventario.


