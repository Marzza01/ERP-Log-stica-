# LOGIMKT S.A. de C.V. — Sistema Integrado ERP, OMS y Marketplace 🚀

Bienvenido al repositorio oficial del **Proyecto Integrador Final: Implementación de ERP y Marketplace B2B/B2C** para LOGIMKT S.A. de C.V. Este proyecto digitaliza completamente la cadena de suministro, automatiza el flujo de pedidos y genera KPIs de logística digital en tiempo real.

---

## 📑 Informe de Avance Técnico (Entrega 50%) — Evidencias y Contribución por Equipo

A continuación se presenta la documentación detallada por cada célula de trabajo para la entrega del informe técnico en PDF, respaldando los avances funcionales, flujos, endpoints y modelos de datos implementados.

---

### 🟢 GRUPO A: ERP — Módulo de Inventario y Back Office

#### 1. Resumen de Contribución
- **Soporte de Back Office a la Operación ERP:** El módulo centraliza la administración del catálogo maestro mediante una arquitectura desacoplada en .NET 8 (`MarketplaceERP.Catalog`), controlando inventario físico, asignación dinámica en WMS (pasillo, rack, nivel, posición) y aprovisionamiento.
- **Implementación de los 500 SKUs y Maestro B2B:** Se implementó el servicio de importación y seed masivo (`ICatalogImportService`) que inyecta 500 SKUs con sus 16 atributos obligatorios de logística digital (SKU único, categoría, subcategoría, dimensiones en cm, peso en kg, precio B2C y B2B diferenciado, stock inicial, punto de reorden y proveedor asociado). Los proveedores están estructurados con condiciones de pago B2B (crédito 30/60 días y retenciones de ley).

#### 2. Evidencia Funcional y Flujos (Capturas a Incluir en el Informe)
- **Vista de Stock y Alertas de Reorden:**
  - *Captura sugerida:* Pantalla de **Productos / Inventario** (`/productos` y `/wms`).
  - *Elementos a resaltar:* La columna de **Cantidad Disponible vs. Reservada**, la etiqueta de **Punto de Reorden** y el badge de alerta roja cuando el stock cae por debajo del umbral mínimo (`EsBajoMinimo = true`).
- **Flujo de Compras (Procurement):**
  - *Captura sugerida:* Módulo de **Compras** (`/compras`).
  - *Paso a paso:*
    1. **Solicitud / Borrador:** Generación de Orden de Compra (`PO-YYYY-XXXX`) con cálculo de IVA (13%).
    2. **Envío al Proveedor:** Cambio de estado a `Enviada`.
    3. **Recepción en Almacén:** Registro de recepción física de mercadería.
    4. **3-Way Match:** Conciliación tridireccional automática entre Orden de Compra, Factura de Proveedor y Recepción de Bodega con tolerancia porcentual configurable (2%).
- **Actualización de Inventario en Tiempo Real por el OMS:**
  - Al crearse una orden en el OMS, se ejecuta la reserva preventiva de stock (`ReservaStock`), evitando sobreventas (*overselling*). Al despacharse o cancelarse, el stock físico se debita o libera automáticamente en PostgreSQL.

#### 3. Respaldos Técnicos
- **Endpoints Swagger Relevantes (`ASP.NET Core Web API`):**
  - `GET /api/productos`: Listado paginado de catálogo.
  - `GET /api/productos/buscar`: Búsqueda multicriterio (categoría, rango precio, disponibilidad).
  - `POST /api/productos/carga-masiva`: Inyección y validación masiva de SKUs.
  - `GET /api/wms/bodega`: Mapa visual de ocupación de racks y alertas de capacidad.
  - `GET /api/wms/ubicaciones`: Consulta de disponibilidad por pasillo y nivel.
  - `POST /api/compras/po`: Creación de Orden de Compra (PO).
  - `POST /api/compras/validar-3way-match`: Validación tridireccional automatizada.
- **Esquema de Tablas Principales (PostgreSQL):**
  - `catalog.productos`: `Id (UUID)`, `Sku (VARCHAR)`, `Nombre`, `CategoriaNombre`, `PrecioB2C`, `PrecioB2B`, `StockInicial`, `StockMinimo`, `ProveedorNombre`, `PesoKg`, `DimensionesCm`, `EstaPublicado`.
  - `catalog.stock_fisico`: `Id (UUID)`, `ProductoId (FK)`, `CantidadTotal`, `CantidadReservada`, `PuntoReorden`, `UbicacionCodigo`, `UltimaActualizacion`.
  - `catalog.ubicaciones_almacen`: `Id (UUID)`, `Codigo`, `Pasillo`, `Rack`, `Nivel`, `Posicion`, `CapacidadMaxima`, `OcupacionActual`.
  - `finance.ordenes_compra`: `Id (UUID)`, `NumeroPO`, `ProveedorId`, `Estado`, `SubTotal`, `ImpuestoIva`, `MontoTotal`, `TerminosPago`, `FechaEmision`.

---

### 🟢 GRUPO B: OMS — Gestión de Pedidos

#### 1. Resumen de Contribución
- **Rol del OMS en la Automatización End-to-End:** El módulo (`MarketplaceERP.Finance` / Core OMS) coordina el ciclo de vida del pedido con una máquina de estados estricta y trazabilidad en minutos para cada fase.
- **Segmentación B2B vs. B2C:**
  - **B2C:** Compra unitaria desde $5, pago inmediato, emisión de Factura de Consumidor Final y despacho express.
  - **B2B:** Pedidos corporativos de volumen con umbral mínimo ($500), emisión automática de Comprobante de Crédito Fiscal (CCF) con IVA 13% desglosado y condiciones de crédito comercial (30/60 días).

#### 2. Evidencia Funcional del Flujo de Pedidos (Capturas a Incluir)
- **Trazabilidad y Ciclo de Vida del Pedido:**
  - *Captura sugerida:* Vista de **Órdenes** (`/ordenes`).
  - *Secuencia de Estados:* `Pendiente` ➔ `Aprobada` ➔ `EnProceso` ➔ `Enviada` ➔ `Entregada` (o `Cancelada`). Se aprecia el modal de transición rápida con badges de color de acuerdo al estado.
- **Fulfillment (Picking, Packing y Despacho):**
  - *Captura sugerida:* Módulo de **Fulfillment** (`/fulfillment`).
  - *Elementos a mostrar:* Pestaña de **Picking** (PickLists generadas por orden, visualización de pasillo/rack/nivel de cada ítem con checklist interactivo) y pestaña de **Packing / Despacho** (cálculo de peso volumétrico y asignación de guía de transporte `LOGIMKT Express` o `DHL`).
- **Devoluciones y Logística Inversa (RMA):**
  - *Captura sugerida:* Módulo de **RMA** (`/rma`).
  - *Evidencia visual:* Registro de incidencias por cliente, dictamen técnico en bodega (`ReStock`, `MermaScrap` o `Remate`) y autorización de reembolso al comprador.

#### 3. Indicadores y Lógica Técnica
- **Fórmulas de Cálculo en Backend (`KpisDashboardController.cs`):**
  - **OTIF (On-Time In-Full):**
    $$\text{OTIF} = \left(\frac{\text{Órdenes Entregadas cuya Fecha Real} \le \text{Fecha Estimada}}{\text{Total Órdenes Entregadas con Fecha Estimada}}\right) \times 100$$
    *Lógica de código:* Compara `o.FechaEntregaReal <= o.FechaEntregaEstimada` en el subconjunto de órdenes cerradas.
  - **Lead Time (Tiempo Promedio de Ciclo del Pedido):**
    $$\text{Lead Time Promedio (horas)} = \frac{\sum (\text{FechaEntregaReal} - \text{FechaCreacion}).\text{TotalHours}}{N_{\text{entregadas}}}$$
- **Rutas de API (Swagger):**
  - `GET /api/ordenes`: Lista de pedidos con filtros por estado y tiempos de permanencia.
  - `POST /api/ordenes`: Recepción de nuevo pedido desde marketplace o canal B2B.
  - `PATCH /api/ordenes/{id}/estado`: Máquina de transición de estado.
  - `GET /api/fulfillment/picklists`: Listado de listas de recolección optimizadas por ruta de almacén.
  - `POST /api/fulfillment/empacar`: Consolidación de bulto, peso real y generación de guía de rastreo.
  - `GET /api/rma` y `POST /api/rma/{id}/dictamen`: Administración y resolución de logística inversa.
- **Modelo de Datos de Pedidos (`finance.ordenes`):**
  - `NumeroOrden`, `TipoCliente (B2B/B2C)`, `Estado`, `SubTotal`, `ImpuestoIva`, `CostoEnvio`, `MontoTotal`, `TipoComprobante`, `GuiaEnvio`, `FechaCreacion`, `FechaEntregaEstimada`, `FechaEntregaReal`.

---

### 🟢 GRUPO C: Marketplace + Dashboard Analytics

#### 1. Resumen de Contribución
- **Integración de Canales y Visibilidad Ejecutiva:** La interfaz unificada (`marketplace-ui`) conecta la experiencia de catálogo y compra con el ERP/OMS central. El **Dashboard Ejecutivo** proporciona a la gerencia indicadores de rendimiento clave en tiempo real, permitiendo identificar cuellos de botella operativos en picking, demoras de transportistas o picos de devolución RMA de inmediato.

#### 2. Evidencia de Interfaz y Componentes (Capturas a Incluir)
- **Marketplace y Catálogo Dinámico:**
  - *Captura sugerida:* Vista de **Productos** (`/productos`).
  - *Elementos a mostrar:* Buscador en tiempo real por texto o SKU, selector por categorías, visualización de precios y disponibilidad de stock en tiempo real.
- **Dashboard Ejecutivo (Visualizaciones con Recharts):**
  - *Captura sugerida:* Panel principal del **Dashboard** (`/dashboard`).
  - *Elementos a mostrar:* Los 8 KPIs ejecutivos en tarjetas interactivas:
    1. **Fill Rate (%)**
    2. **OTIF (On-Time In-Full) (%)**
    3. **Tasa de Cancelación (%)**
    4. **Tiempo de Ciclo del Pedido (horas)**
    5. **Rotación de Inventario (veces)**
    6. **Tasa de Devolución RMA (%)**
    7. **Precisión del 3-Way Match (%)**
    8. **Margen de Comisión Marketplace (10%)**
  - *Gráficos Recharts:* Gráfico de líneas con tendencia temporal y barras comparativas de ingresos proyectados vs. costos operativos.
- **Semáforo Automático y Reportes Inteligentes:**
  - *Evidencia visual:* Indicadores codificados por color (**Verde: Óptimo**, **Amarillo: Atención**, **Rojo: Crítico**) con base en umbrales de negocio (ej. Fill Rate meta $\ge 95\%$). Proyección del modelo financiero mensualizado a 12 meses.
- **Diseño Responsive Multidispositivo:**
  - *Evidencia visual:* Captura en vista escritorio (sidebar completa) y captura en vista móvil/tablet (menú hamburguesa, drawer deslizante y etiquetas compactas sin desbordes de texto).

#### 3. Respaldos Tecnológicos
- **Arquitectura de Frontend:**
  - **Framework & Build:** React 18 + TypeScript + Vite.
  - **Gestión de Estado Global:** `AuthContext` (almacenamiento de tokens JWT, roles de usuario y persistencia en sesión) y estado local optimista para interacciones fluidas.
  - **Cliente HTTP y Comunicación con Backend:** Capa centralizada en `src/api/client.ts` con manejo de tokens en cabeceras `Authorization: Bearer <token>`, tipado estricto en TypeScript y proxy reverso en Vite (`vite.config.ts`) que enruta `/api` hacia `http://localhost:5000` eliminando incidentes de CORS tanto en red local como bajo túneles ngrok.

---

## 🏗️ Arquitectura y Stack Tecnológico

El proyecto sigue una arquitectura desacoplada Frontend/Backend diseñada para simular un entorno corporativo real:

### 💻 Frontend (Interfaz de Usuario)
- **Framework:** React 18 + TypeScript + Vite.
- **Estilos:** Dark Enterprise UI con TailwindCSS / utilidades modernas de alta densidad de datos.
- **Visualización:** Gráficos analíticos con Recharts (`LineChart`, `BarChart`, `ResponsiveContainer`).
- **Navegación:** React Router v7 con protección de rutas y adaptación en 4 breakpoints (Mobile, Tablet, Laptop, Desktop).

### ⚙️ Backend (Servidor y Lógica de Negocio)
- **Framework:** .NET 8 (ASP.NET Core Web API).
- **Patrones:** Domain-Driven Design (DDD) dividido en capas: `Catalog`, `Finance`, `Shared` y `API`.
- **Persistencia:** Entity Framework Core + **PostgreSQL** (Puerto 5432).
- **Seguridad:** Autenticación y autorización basada en claims JWT.
- **Documentación API:** Swagger (OpenAPI) disponible en `/swagger`.
- **Health Checks:** Endpoint `/health` para monitoreo de conectividad de servicios.

---

## 🚀 Guía de Inicio Rápido

### Requisitos Previos
1. **Node.js** (v18+)
2. **.NET 8 SDK**
3. **PostgreSQL** (Puerto `5432`)

### Paso Rápido: Inicio con un solo clic (Windows)
Ejecuta el archivo:
```bat
INICIAR_LOCAL.bat
```
El script levantará automáticamente la API de .NET (puerto 5000) y la interfaz Vite (puerto 5173), detectando tu IP local para acceso desde celulares y tablets.

### Inicio Manual por Consola

1. **Backend (.NET API):**
   ```bash
   cd MarketplaceERP
   dotnet run --project src/MarketplaceERP.API/MarketplaceERP.API.csproj --urls=http://localhost:5000
   ```
   *Disponible en: `http://localhost:5000` y Swagger en `http://localhost:5000/swagger`.*

2. **Frontend (Vite UI):**
   ```bash
   cd MarketplaceERP/frontend/marketplace-ui
   npm install
   npm run dev
   ```
   *Disponible en: `http://localhost:5173`.*

---

## 📱 Acceso y Diseño Responsive Multidispositivo

El sistema cuenta con una arquitectura de interfaz adaptable a 4 perfiles de pantalla con directrices UI empresariales:
- **Desktop / Pantalla Grande (≥1024px):** Barra lateral completa con navegación agrupada y vista de alta densidad.
- **Laptop / Pantalla Mediana (768px - 1023px):** Navegación adaptativa optimizada para productividad.
- **Tablets y Celulares (<768px):** Drawer lateral retráctil mediante menú hamburguesa, overlay táctil y etiquetas compactas para evitar desbordes visuales en pantallas reducidas.
- **Acceso en Red Local:** Disponible en la misma red Wi-Fi mediante `http://<IP-LOCAL>:5173` sin vencimiento de sesión.
- **Túnel Remoto Seguro:** Compatible con **ngrok** (`ngrok http 5173`) para acceso externo temporal.

---

## 📋 Tareas Pendientes (Semana 9 y 10)
1. Redactar el **Informe Técnico Final** (mínimo 50 páginas, APA 7) consolidando estos avances.
2. Crear la **Presentación Ejecutiva** y el **Manual de Usuario**.
3. Preparar el **Modelo Financiero en Excel**.
4. **Ensayar la Defensa Oral:** Entender profundamente cómo el sistema calcula cada KPI, cómo interactúan los módulos y saber justificar decisiones logísticas bajo estrés.

---
*Desarrollado para la materia "Logística en Negocios Digitales". UNICAES 2026.*
