<!-- ERP desarrollado por Marcelo Zepeda (@Marzza01) | Todos los derechos reservados © 2026 -->

# Registro de Cambios y Mejoras del Proyecto — MarketplaceERP

Este documento centraliza y documenta cada una de las fases, cambios de arquitectura, nuevas entidades, servicios de negocio, endpoints y cobertura de pruebas implementadas en el sistema de acuerdo con los requerimientos logísticos y contables del proyecto.

---

## Índice
1. [Resumen Ejecutivo de Fases](#resumen-ejecutivo-de-fases)
2. [Fase 1: Núcleo de Catálogo, WMS y Flujo Base](#fase-1-núcleo-de-catálogo-wms-y-flujo-base)
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

## Matriz de Control y Cobertura de Pruebas

Toda la solución compila sin advertencias y ejecuta con éxito el 100% de la suite de pruebas automatizadas:

```bash
dotnet test MarketplaceERP.sln
```

### Resultados de Ejecución:
- **`MarketplaceERP.Core.Tests`**: 6 pasadas (0 errores)
  - Validaciones de dominio, entidades y tipos de valor.
- **`MarketplaceERP.Catalog.Tests`**: 13 pasadas (0 errores)
  - Pruebas de stock, cubicaje, almacenes, flujo de movimientos WMS y Fulfillment (Picking óptimo S-Shape, packing y emisión de guía).
- **`MarketplaceERP.Finance.Tests`**: 22 pasadas (0 errores) *(+1 prueba integral E2E)*
  - Validaciones de partida doble contable (Debe == Haber).
  - Cálculo de IVA salvadoreño (13%).
  - Auditoría de 4 casos de 3-Way Matching (conforme, discrepancia de precio, discrepancia de cantidad, faltante).
  - Máquina de estados OMS de 6 etapas.
  - Conciliación de split 90/10 y generación de liquidaciones.
  - **Prueba Integral E2E de ciclo de vida completo transaccionado.**

**Total general:** **41 pruebas ejecutadas, 41 superadas, 0 fallidas.**
