# MarketplaceERP

Sistema ERP tipo Marketplace (estilo Amazon) para pequeñas empresas.
Arquitectura **Monolito Modular** en .NET 8 + PostgreSQL 15 + React 18.

---

## Estructura del Monorepo y Organización por Grupos

En Visual Studio / VS Code / Rider, la solución está organizada en **Carpetas de Solución (Solution Folders)** para delimitar claramente el trabajo de cada grupo:

```
MarketplaceERP.sln
│
├── 📁 Grupo A (Inventario, Envíos, Catálogo)
│   ├── MarketplaceERP.Catalog/         ← Productos, Stock Físico, Reservas
│   └── MarketplaceERP.Catalog.Tests/
│
├── 📁 Grupo B (Ventas, Finanzas, Core)
│   ├── MarketplaceERP.Core/            ← Vendedores, Compradores, Categorías, Comisiones
│   ├── MarketplaceERP.Core.Tests/
│   ├── MarketplaceERP.Finance/         ← Órdenes, Pagos, Splits, Liquidaciones, Saga Checkout
│   └── MarketplaceERP.Finance.Tests/
│
├── 📁 Grupo C (Shared, API, Control)
│   ├── MarketplaceERP.Shared/          ← DTOs, Excepciones, Settings, Interfaces
│   └── MarketplaceERP.API/             ← Capa de composición: Controllers, Program.cs
│
└── 📁 Docs & Config
    ├── README.md
    ├── docker-compose.yml
    └── nginx.conf
```

---

## Equipos y Responsabilidades (Grupos A, B y C)

| Grupo | Módulos Asignados | Alcance en el Marketplace | Responsabilidad Principal |
| :--- | :--- | :--- | :--- |
| **Grupo A** | `MarketplaceERP.Catalog` + Tests | Inventario multi-vendedor, Catálogo, Despachos | Gestión de productos por vendedor, almacén físico y reservas atómicas de stock. |
| **Grupo B** | `MarketplaceERP.Core` + `MarketplaceERP.Finance` + Tests | Core (Vendedores, Compradores, Comisiones) & Finanzas (Ventas, Pagos, Splits) | Entidades maestras, reglas dinámicas de comisiones, orquestación de checkout (Saga) y liquidación de pagos. |
| **Grupo C** | `MarketplaceERP.Shared` + `MarketplaceERP.API` + Frontend + DevOps | Misceláneos, Contratos, Gateway API, UI y Operaciones | DTOs comunes, autenticación JWT, orquestación global, dashboard frontend, backups y CI/CD. |

---

## ⚠️ Reglas de Oro (OBLIGATORIAS para todos)

### Regla 1: No referencias entre proyectos de dominio
Ningún proyecto de dominio puede referenciar directamente a otro proyecto de dominio.
La comunicación ocurre **ÚNICAMENTE** a través de las interfaces en `Core/Interfaces/`.

```
✅ PERMITIDO:
   Catalog  → Shared
   Catalog  → Core (para IStockService, ICommissionService, IPaymentGateway)
   Finance  → Shared
   Finance  → Core (para las mismas interfaces)

❌ PROHIBIDO:
   Catalog  → Finance
   Finance  → Catalog
   Core     → Catalog
   Core     → Finance
```

### Regla 2: Cada equipo corre sus propias migraciones
Nunca modifiques un `DbContext` que no sea del módulo de tu equipo.
Cada equipo tiene su propio esquema PostgreSQL (`core`, `catalog`, `finance`) y su propia tabla `__EFMigrationsHistory`.

---

## Cómo levantar el entorno local

### Prerequisitos
- Docker Desktop instalado y corriendo
- (Opcional) .NET 8 SDK para desarrollo local sin Docker

### Con Docker Compose (recomendado)

```bash
# Desde la raíz del monorepo (MarketplaceERP/)
docker-compose up --build
```

| Servicio | URL |
|----------|-----|
| API + Swagger | http://localhost:8080/swagger |
| Frontend | http://localhost:3000 |
| PostgreSQL | localhost:5432 |

### Sin Docker (desarrollo local)

```bash
# Requisitos: PostgreSQL corriendo en localhost:5432 con usuario marketplace_user

# Terminal 1 — API
cd src/MarketplaceERP.API
dotnet run

# Terminal 2 — Frontend
cd frontend/marketplace-ui
npm install
npm run dev
```

---

## Comandos de Migración por Equipo

> ⚠️ **Siempre ejecutar desde la raíz del monorepo (`MarketplaceERP/`).**
> Asegúrate de que PostgreSQL esté corriendo antes de ejecutar `database update`.

### Equipo 1 — Core (esquema: `core`)

```bash
# Crear migración inicial
dotnet ef migrations add InitialCreate \
  --context CoreDbContext \
  --project src/MarketplaceERP.Core \
  --startup-project src/MarketplaceERP.API

# Aplicar migraciones a la base de datos
dotnet ef database update \
  --context CoreDbContext \
  --project src/MarketplaceERP.Core \
  --startup-project src/MarketplaceERP.API
```

### Equipo 2 — Catalog (esquema: `catalog`)

```bash
# Crear migración inicial
dotnet ef migrations add InitialCreate \
  --context CatalogDbContext \
  --project src/MarketplaceERP.Catalog \
  --startup-project src/MarketplaceERP.API

# Aplicar migraciones a la base de datos
dotnet ef database update \
  --context CatalogDbContext \
  --project src/MarketplaceERP.Catalog \
  --startup-project src/MarketplaceERP.API
```

### Equipo 3 — Finance (esquema: `finance`)

```bash
# Crear migración inicial
dotnet ef migrations add InitialCreate \
  --context FinanceDbContext \
  --project src/MarketplaceERP.Finance \
  --startup-project src/MarketplaceERP.API

# Aplicar migraciones a la base de datos
dotnet ef database update \
  --context FinanceDbContext \
  --project src/MarketplaceERP.Finance \
  --startup-project src/MarketplaceERP.API
```

---

## Autenticación JWT

### Obtener token

```bash
POST http://localhost:8080/api/auth/login
Content-Type: application/json

{
  "username": "admin",
  "password": "admin123"
}
```

### Usuarios de prueba

| Usuario | Contraseña | Rol | Permisos |
|---------|------------|-----|----------|
| `admin` | `admin123` | Admin | Todo |
| `vendedor1` | `pass` | Vendedor | Publicar productos |
| `comprador1` | `pass` | Comprador | Realizar checkout |

### Endpoints por Rol

| Endpoint | Método | Rol requerido |
|----------|--------|---------------|
| `POST /api/auth/login` | POST | Público |
| `GET /api/vendedores` | GET | Admin, Vendedor |
| `POST /api/vendedores` | POST | Admin |
| `GET /api/productos` | GET | Autenticado |
| `POST /api/productos` | POST | Vendedor, Admin |
| `PATCH /api/productos/{id}/publicar` | PATCH | Vendedor, Admin |
| `GET /api/comisiones` | GET | Admin |
| `POST /api/comisiones` | POST | Admin |
| `POST /api/checkout` | POST | Comprador |
| `GET /api/checkout/ordenes` | GET | Admin |

---

## Flujo del Checkout (Saga)

```
Comprador → POST /api/checkout
    ↓
[1] CheckoutOrchestrator.ProcesarCheckoutAsync()
    ↓
[2] IStockService.ReservarStockAsync()
    → Falla: lanza StockInsuficienteException (HTTP 409)
    ↓
[3] IPaymentGateway.ProcesarPagoAsync()
    → Falla: LiberarStockAsync() + lanza PagoFallidoException (HTTP 409)
    ↓
[4] ICommissionService.CalcularComisionAsync() (10% por defecto)
    ↓
[5] Guardar SplitPago en DB (90% vendedor / 10% marketplace)
    ↓
[6] IStockService.ConfirmarDespachoAsync()
    ↓
[7] Retornar CheckoutResult (HTTP 200)
```

---

## Tests

```bash
# Ejecutar todos los tests
dotnet test MarketplaceERP.sln

# Con cobertura
dotnet test MarketplaceERP.sln --collect:"XPlat Code Coverage"

# Solo un proyecto
dotnet test tests/MarketplaceERP.Finance.Tests/
```

---

## Flujo de trabajo diario (Git)

### Convenciones de ramas
- `main` — producción (solo merge desde `develop` con PR aprobado)
- `develop` — integración continua
- `feature/<equipo>/<descripcion>` — ramas de feature

**Ejemplos:**
```
feature/core/reglas-comision-por-categoria
feature/catalog/stock-service-real
feature/finance/integracion-stripe
```

### Reglas de merge
1. Cada desarrollador trabaja en su rama feature.
2. **Merge obligatorio a `develop` todos los días a las 13:00** (aunque el feature no esté completo).
3. Nunca merges directos a `main`. Solo desde `develop` con Pull Request aprobado.
4. Si hay conflicto en `ci.yml` o `docker-compose.yml`, el **Tech Lead** resuelve.

---

## Arquitectura de Referencias

```
┌─────────────────────────────────────────────────────┐
│              MarketplaceERP.API                     │
│        (Capa de composición: Controllers)           │
└──────┬────────────┬────────────┬────────────────────┘
       │            │            │
       ▼            ▼            ▼
┌──────────┐  ┌──────────┐  ┌──────────┐
│  Core    │  │ Catalog  │  │ Finance  │
│ (Equipo1)│  │ (Equipo2)│  │ (Equipo3)│
│          │  │          │  │          │
│Vendedor  │  │Producto  │  │Orden     │
│Comprador │  │StockFisico│  │SplitPago │
│Categoria │  │Reserva   │  │Checkout  │
│ReglaComis│  │          │  │Liquidaci │
└──────┬───┘  └────┬─────┘  └─────┬────┘
       │           │               │
       └───────────┴───────────────┘
                   │
                   ▼
       ┌───────────────────────┐
       │   MarketplaceERP.Shared  │
       │  DTOs, Excepciones,      │
       │  Settings, Interfaces    │
       └───────────────────────┘
```

---

## Variables de Entorno Importantes

| Variable | Descripción | Valor por defecto (dev) |
|----------|-------------|------------------------|
| `ConnectionStrings__DefaultConnection` | Cadena de conexión PostgreSQL | Ver `appsettings.json` |
| `Jwt__Key` | Clave secreta JWT (min. 32 chars) | ⚠️ Cambiar en producción |
| `Jwt__Issuer` | Emisor del token | `MarketplaceERP` |
| `Jwt__Audience` | Audiencia del token | `MarketplaceERP.Clients` |
| `Jwt__ExpirationMinutes` | Duración del token (minutos) | `60` |

> ⚠️ **NUNCA comitees la `Jwt:Key` real en el repositorio.** Usa variables de entorno del servidor o un gestor de secretos (Azure Key Vault, AWS Secrets Manager) en producción.
