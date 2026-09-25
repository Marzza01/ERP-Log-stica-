# LOGIMKT S.A. de C.V. — Sistema Integrado ERP, OMS y Marketplace 🚀

Bienvenido al repositorio oficial del **Proyecto Integrador Final: Implementación de ERP y Marketplace B2B/B2C** para LOGIMKT S.A. de C.V. Este proyecto digitaliza completamente la cadena de suministro, automatiza el flujo de pedidos y genera KPIs de logística digital en tiempo real.

---

## 📊 Estado de Avance del Proyecto (Basado en la Rúbrica de Evaluación)

Actualmente, el **desarrollo técnico del sistema está completado al 100%**, cumpliendo con los criterios de la escala **EXCELENTE (100%)** detallados en la Sección 8 de la rúbrica oficial.

A continuación se detalla el progreso por módulo y equipo responsable:

### 🟢 GRUPO A: ERP — Módulo de Inventario y Back Office (100% Completado)
**Responsabilidad:** Gestión interna, abastecimiento y catálogo.
- [x] **Carga Masiva de Catálogo:** 500 SKUs importados y verificados.
- [x] **Maestro de Proveedores:** Configurado para la gestión B2B.
- [x] **Gestión de Stock:** Puntos de reorden automáticos y alertas de stock mínimo.
- [x] **Proceso de Compras:** Flujo documentado (Solicitud → Cotización → OC → Recepción).
- [x] **Integración:** Actualización de stock en tiempo real al recibir pedidos del OMS.

### 🟢 GRUPO B: OMS — Gestión de Pedidos (100% Completado)
**Responsabilidad:** Procesamiento del flujo del pedido B2B y B2C de inicio a fin.
- [x] **Flujo Diferenciado:** Soporte para pedidos B2B (volumen) y B2C (unitarios).
- [x] **Estados del Pedido:** Tracking completo (Recibido → Confirmado → En Preparación → Enviado → Entregado → Cerrado).
- [x] **Fulfillment:** Generación de guías de envío y procesos de picking/packing.
- [x] **Devoluciones (RMA):** Módulo para gestionar logística inversa y devoluciones.
- [x] **KPIs Logísticos:** Cálculo automatizado del **OTIF** (On Time In Full) y **Lead Time del Pedido**.

### 🟢 GRUPO C: Marketplace + Dashboard Analytics (100% Completado)
**Responsabilidad:** Front-end del cliente y panel de control ejecutivo.
- [x] **Marketplace B2B/B2C:** Tienda online con los 500 productos, buscador, filtros y carrito de compras.
- [x] **Checkout Funcional:** Proceso de pago y generación del pedido hacia el OMS.
- [x] **Dashboard Ejecutivo:** Panel con **8 KPIs en tiempo real** (OTIF, Lead Time, Fill Rate, Tasa de Devoluciones, Costo Logístico, Rotación, Tiempo OMS, NPS).
- [x] **Semáforo Automático:** Alertas visuales (Óptimo, Atención, Crítico) para los KPIs.
- [x] **Reportes Inteligentes:** Análisis ABC del catálogo y modelos predictivos (Forecast).

---

## 🏗️ Arquitectura y Stack Tecnológico

El proyecto sigue una arquitectura moderna, desacoplada (Frontend/Backend) y diseñada para ser escalable, simulando un entorno de producción real.

### 💻 Frontend (Interfaz de Usuario)
- **Framework:** React 18 + TypeScript + Vite.
- **Estilos:** Diseño "Dark Enterprise" con CSS Modules / TailwindCSS. Inspirado en estéticas modernas (21st.dev).
- **Componentes Core:** Tablas dinámicas (DataTables), Modales, Gráficos (Recharts) para KPIs, Context API para estado global (Auth, Carrito).
- **Comunicación API:** Cliente Axios con interceptores y proxy configurado para evitar problemas de CORS.

### ⚙️ Backend (Servidor y Lógica de Negocio)
- **Framework:** .NET 8 (ASP.NET Core Web API).
- **Arquitectura:** Domain-Driven Design (DDD) con módulos separados (Catálogo, Core OMS, Finanzas).
- **ORM & Base de Datos:** Entity Framework Core + **PostgreSQL** (Puerto 5432).
- **Seguridad:** Autenticación y Autorización basada en JWT (JSON Web Tokens).
- **Documentación API:** Swagger (OpenAPI) expuesto en `/swagger`.
- **Health Checks:** Endpoint `/health` para monitoreo en vivo de la infraestructura.

---

## 🚀 Guía de Inicio Rápido (Para Nuevos Miembros y Auditoría)

Si eres un nuevo miembro del equipo o estás preparando el entorno para la **Defensa Técnica (Semana 10)**, sigue estos pasos al pie de la letra:

### Requisitos Previos
1. **Node.js** (v18+)
2. **.NET 8 SDK**
3. **PostgreSQL** (Instalado y corriendo en el puerto `5432`). *Nota: Durante la instalación usar contraseña `postgres`.*

### Paso 1: Configurar la Base de Datos
1. Asegúrate de que el servicio de PostgreSQL esté en ejecución.
2. Abre una terminal en la carpeta del backend (`src/MarketplaceERP.API`).
3. Ejecuta las migraciones para crear las tablas automáticamente:
   ```bash
   dotnet ef database update
   ```

### Paso 2: Iniciar el Backend (API)
En la misma terminal del backend, ejecuta:
```bash
dotnet run
```
*El sistema inyectará automáticamente los 500 SKUs del catálogo semilla si la base de datos está vacía. La API estará disponible en `http://localhost:8080`.*

### Paso 3: Iniciar el Frontend (UI)
Abre otra terminal en la carpeta del frontend (`frontend/marketplace-ui`):
```bash
npm install
npm run dev
```
*El sistema abrirá el navegador en `http://localhost:3000`. El indicador superior derecho deberá mostrar "🟢 API online".*

---

## 📋 Tareas Pendientes (Semana 9 y 10)
Aunque el código está al 100%, para la calificación final (**40% restante**) cada grupo debe completar:
1. Redactar el **Informe Técnico Final** (mínimo 50 páginas, APA 7).
2. Crear la **Presentación Ejecutiva** y el **Manual de Usuario**.
3. Preparar el **Modelo Financiero en Excel**.
4. **Ensayar la Defensa Oral:** Entender profundamente cómo el sistema calcula cada KPI, cómo interactúan los módulos y saber justificar decisiones logísticas bajo estrés. *(Regla de Oro: "Eso lo hizo mi compañero" = Cero).*

---
*Desarrollado para la materia "Logística en Negocios Digitales". UNICAES 2026.*
