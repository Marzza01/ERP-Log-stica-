# P3 · Calidad, rendimiento y reportes — LOGIMKT

**Rama de desarrollo:** `fix/p3-calidad-rendimiento-reportes-20261108`, derivada del PR documental P2 #54 (base `d217a07cf9a08d91832893e19c810e4d6799218d`). No hay cambios aplicados a `main`, a `staging/catalog-500-neon-test` ni a Neon producción. P2 #54 aún debe integrarse antes de P3 si se desea integrar el historial en staging.

## Alcance del checklist P3

### P3.1 Consultas, paginación y rendimiento
- `GET /api/productos` y `GET /api/productos/buscar`: consultas de lectura `AsNoTracking()` para reducir el coste del change tracker sin cambiar la autorización por rol ni la respuesta JSON.
- `ProductosPage.tsx` deja de truncar silenciosamente a los 100 primeros SKU. Utiliza paginación backend con páginas de **50**, `total` real de la API, búsqueda `/api/productos/buscar`, debounce de 300 ms y descarte de respuestas obsoletas. `DataTable` incorpora modo de paginación externa sin volver a filtrar/ordenar solo la página del servidor.
- Tarjetas de stock y bajo mínimo indican expresamente **por página**; `total` publicado procede del backend. El resumen del WMS por SKU agrega `stocks` de múltiples ubicaciones y descuenta reservas/bloqueos.
- Prueba E2E Chromium con API mock local: catálogo **10 000 SKU** paginado en 200 páginas, navegación a la página 2 y búsqueda de un SKU de página posterior. **No** equivale a una prueba de 10 000 filas en PostgreSQL real.
- Análisis SQL read-only con EXPLAIN ANALYZE en clon Neon `br-polished-union-bbrwd0tu`: 500 SKU totales, 488 publicados. Consulta comparable por `Sku`, página 10 de 50, ~3.902 ms en una medición puntual; búsqueda por fragmento ~0.225 ms en una medición. El planner usó secuencial y sort en 500 filas. Índices existentes: único `IX_productos_Sku`, índice de `CategoriaNombre`, `EstaPublicado` y otros. **No extrapolar estos tiempos a volúmenes mayores**, ni recomendar índices de texto sin medir primero.

### P3.2 Errores y UX
- Evita inyectar texto HTML/crudo de errores de proxy de servidor en el mensaje mostrado al usuario. Los errores HTTP 5xx no JSON exponen aviso genérico de servicio no disponible en vez de trazas internas.
- Dashboard descarta respuestas HTTP retrasadas cuando cambia de período o se desmonta el componente; no mezcla indicadores de intervalos distintos.
- Al recorrer el catálogo, las tarjetas no atribuyen las unidades de una página al conjunto completo; filtro de categoría explícito y búsqueda global backend.
- Test de error HTML 503 en `tests/p3-quality.test.cjs`; pruebas previas P1 de 401/403/login y Chromium continúan en CI.

### P3.3 KPIs, reportes y análisis
- Dashboard permite `Exportar KPIs CSV` del período seleccionado cuando existe respuesta válida del servidor, sin añadir cifras inventadas.
- CSV incluye período, sello temporal de API, resumen operativo (órdenes, canceladas, entregadas, 3-Way, RMA, comisión interna) y KPIs con unidad/meta/semaforización.
- KPI con `tieneDatos=false` se exporta como `Sin datos`, no cero; CSV escapado contra fórmulas `=+-@` y comillas.
- No se presentan modelos financieros proyectados como cobros bancarios o resultados reales; no hay pagos externos.

## Evidencia requerida para cierre
1. Build .NET y frontend en SHA final, tests de P0/P1 conservados.
2. `npm run test:p3`: stock multiubicación, paginación y metadatos, preservación de DTO, CSV sin datos, protección frente a fórmulas y HTML 503.
3. CI E2E navegador: `P3_CATALOG_BROWSER_EVIDENCE.completed=true` con 10 000 SKU simulados, y `P2_LOCAL_ADMIN_ROUTE_EVIDENCE`, `P2_LOCAL_BUYER_EVIDENCE`, `P2_LOCAL_SELLER_EVIDENCE` aprobados en la API real de CI desechable.
4. No merges ni escrituras financieras. No habilitar proveedores de pago.

## Límites y decisiones
- P3 constituye mejoras de calidad y UX, **no certificación de rendimiento bajo 10k/100k filas en PostgreSQL**, ni prueba de resistencia simultánea ni aprobación de producción.
- `DataTable` mantiene paginación cliente para las otras pantallas; solo Productos se mueve a servidor. Otras vistas con datasets crecientes requieren fases adicionales si la evaluación exige escala superior.
- Los totales visibles del catálogo corresponden a productos **publicados**, no necesariamente a los 500 maestros (en clon se observaron 488 publicados).
- `PR #54` (P2) sigue separado; hacer revisión e integración supervisada a staging antes de incorporar P3, siempre con CI por SHA y sin desplegar a producción.
