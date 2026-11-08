# Auditoría de rescate seguro: catálogo CSV, WMS y checkout — 2026-11-08

**Ámbito:** inspección de los archivos que ya están en la rama candidata de GitHub y en el historial `integracion-main-auditoria`. **No se ha inspeccionado el contenido de los 23 archivos locales sin confirmar**: la consola del usuario contiene nombres, conteos y hashes, pero el ZIP `ERP_Revision_23_Archivos_20261108_143610.zip` no ha sido adjuntado a esta conversación. Este documento no aprueba automáticamente ninguna fusión.

**Destino permitido de futuras modificaciones:** `consolidacion/erp-segura-20261108`, PR borrador #64. **Prohibido por ahora:** tocar `main`, deploys de Vercel/Render, Neon producción, eliminar ramas y mover worktrees.

## 1. Hallazgos de importación y stock del código actual

| Identificador | Evidencia del código en rama candidata | Riesgo / alcance | Gate antes de implementar |
|---|---|---|---|
| CAT-01 | `src/MarketplaceERP.Catalog/Services/CatalogImportService.cs:45-66` lee encabezado, pero interpreta siempre 16 columnas por posiciones, sin validar nombres, orden ni duplicados de SKU | Un CSV con encabezados reordenados podría cargar valores incorrectos. | Validación estricta de esquema, rangos, unicidad de SKU y dry-run con errores por fila |
| CAT-02 | `CatalogImportService.cs:76-87,239-250` usa precios/stock por defecto si el texto no se puede convertir | Valores monetarios o cantidades mal formados pueden aceptarse silenciosamente. | Rechazar números inválidos, negativos, relaciones B2B/B2C no válidas y moneda inconsistente; probar formato decimal es-SV |
| CAT-03 | `CatalogImportService.cs:89-95,123-210` calcula ubicación a partir de la posición ordinal de la fila; si el SKU ya existe, mueve los punteros de `StockFisico` a nueva ubicación sin registrar un `MovimientoInventario` | Reimportar un CSV reordenado puede alterar el mapa WMS sin trazabilidad de traslado. | No mover stock mediante importación de datos maestros; separar alta/actualización de producto de movimientos físicos |
| CAT-04 | `CatalogImportService.cs:214-226` cuenta filas como exitosas antes de `SaveChangesAsync`; persistencia se hace una vez al final | El conteo no demuestra persistencia fila por fila ni asegura control transaccional de lotes. | Resultado por fila + conteo poscommit + transacción planificada, sin crear saldos duplicados |
| CAT-05 | `src/MarketplaceERP.API/Controllers/ProductosController.cs:270-286` acepta CSV con rol Admin, sin comprobación del entorno; `seed-500` sí bloquea staging/producción en líneas 295-300 | La importación por Administrador puede escribir en entornos conectados a datos operativos; no confundir con seed bloqueado. | Guard temporal/permiso de mantenimiento, límites de archivo, dry-run y evidencia en BD desechable antes de habilitar operación real |
| CAT-06 | `CatalogDbContext.cs:64,76-77` define EAN nullable y SKU único; la migración `20261106005614_CodigoEanOpcional.cs` ya está en staging | Reaplicar migración histórica o reemplazar snapshot podría romper versiones y rollback. | Revisar `__EFMigrationsHistory`, estados de datos, `dotnet ef migrations has-pending-model-changes` y generar migración incremental solo cuando haga falta |
| CAT-07 | `tests/MarketplaceERP.Catalog.Tests/CatalogLogisticsTests.cs:301-318` llama `CatalogDataGenerator.GenerarCsv(rutaCsv)` sobre un path que puede apuntar a `docs/data/Catalogo_500_Productos.csv`. `CatalogDataGenerator.cs:152` usa `File.WriteAllText`. | **Una prueba puede sobrescribir el CSV del propio checkout del repositorio** cuando esa ruta exista. No ejecutar esta prueba contra un archivo de catálogo del usuario. | Corregir test para generar CSV en carpeta temporal con limpieza y comprobar el archivo original mediante SHA-256 |

**Pruebas existentes, no suficientes para cierre:** `CatalogLogisticsTests.cs:29-61` valida carga de dos filas y WMS en EF InMemory; `CatalogLogisticsTests.cs:295-345` valida 500 filas/6 categorías con generador propio. Estas pruebas no validan las tres versiones locales distintas del CSV ni la concurrencia/constraints reales de PostgreSQL.

## 2. Rescate histórico del checkout multiproducto

- Código actual: `src/MarketplaceERP.API/Controllers/CheckoutController.cs:26`: `CheckoutRequest(Guid ProductoId, int Cantidad)`; el frontend actual `CheckoutCompradorPage.tsx:2,112-131` indica explícitamente **un producto por orden**.
- La implementación actual incluye controles esenciales en `CheckoutController.cs:51-194`: `PaymentOperationsGuard`, clave de idempotencia (8–128 caracteres), identidad JWT GUID, hash del payload, precio consultado en catálogo, `UNIQUE(CompradorId, IdempotencyKey)`, gestión de colisión por constraint y pago mediante `CheckoutOrchestrator`.
- Código histórico `integracion-main-auditoria`, `CheckoutController.cs:27-28,146-202`: admite `List<ItemCheckoutRequest>`, valida lotes y construye orden con ítems; `CheckoutPage.tsx:94-102` envía `items` e `Idempotency-Key`. El diseño histórico NO equivale a que esos controles actuales se preserven sin integración.

**Propuesta de contrato seguro (no implementada):** solicitud `Items: [{ProductoId, Cantidad}]`, sin aceptar precios/propietario del cliente; normalizar y ordenar ítems para fingerprint, rechazar duplicados incompatibles o agruparlos consistentemente; cotizar cada ítem en servidor; verificar vendedor, USD, límites y precisión; persistir clave única y hash **antes** de autorizar pago; reservar stock de todos los ítems de forma atómica con prueba de rollback por falla de uno; respetar status de orden y no generar liquidación/asiento repetido. Evitar reinterpretar pedido confirmado cuando cambian precios.

**Gates de aceptación antes de fusión:**
1. Compra B2C de 2 ítems distintos con saldos correctos y una sola orden.
2. Reintento con igual key/payload devuelve la misma orden, incluso si cambia el precio de catálogo; key igual + payload diferente devuelve 409.
3. Dos solicitudes concurrentes con la misma key no cobran dos veces.
4. Stock insuficiente en uno de los ítems deshace reservas de todo el lote sin pagos parciales.
5. Error/corte entre orden y pago, reintento seguro y conciliación de asientos/stock.
6. `PaymentOperationsGuard` y autorización continúan actuando en staging/preview; nunca ejecutar cobros reales en pruebas.

## 3. Estado de los 23 archivos locales pendientes

La comparación PowerShell entregó **32 registros**: **9** idénticos a consolidación, **16** distintos de ambas ramas y **7** ausentes de ambas. Se necesitan **las copias locales** para verificar contenido; diferencias de hashes por sí solas no acreditan nuevas funciones.

**Orden sugerido de lectura del ZIP privado:**
1. `backend-integ`: `ControlledCatalogImportService.cs`, `IControlledCatalogImportService.cs`, sus pruebas, `ProductosController.cs`, DI, EF context/migración y tres CSV.
2. `MarketplaceERP`: `middleware.js`, `auth-proxy.test.cjs`, `DashboardPage.tsx`.
3. `staging-500`: `ProductoCompradorResponse.cs` para comprobar que no expone datos indebidos.
4. `Matriz_Integracion_MarketplaceERP.xlsx`, `Catalogo_500_Importacion_Controlada.md` y dos `.gitignore`.

**Importante:** los 23 archivos no están publicados en la rama remota. No subir el ZIP ni la matriz con posibles datos confidenciales a un repositorio público sin revisar su contenido.

## 4. Decisión de esta auditoría

- **Aprobado solo para diagnóstico:** lectura de código remoto, documentar riesgos y diseñar pruebas seguras.
- **Bloqueado:** cherry-pick masivo de antiguos controladores, servicios, migraciones y snapshots; cualquier escritura a Neon producción o exportación de datos personales; promoción de PR #64 a `main`; borrar ramas/worktrees.
- **Siguiente entrada de auditoría:** adjuntar ZIP de las 23 copias verificadas; comparar contenido contra los blobs SHA de staging/consolidación y determinar qué rescatar por cambios semánticos con pruebas de integración.
