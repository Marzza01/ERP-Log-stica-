# Funcionalidades históricas recuperables — análisis de equivalencia (2026-11-08)

**Regla fundamental:** el hecho de que una rama tenga commits exclusivos no significa que sea más moderna ni segura. No fusionar `fix/auditoria` ni `integracion-main-auditoria` directamente sobre staging.

## Evidencias comparadas

| Funcionalidad | Fuente histórica | Comparación con staging | Plan de recuperación |
|---|---|---|---|
| Carrito multiproducto | `integracion-main-auditoria:frontend/marketplace-ui/src/pages/checkout/CheckoutPage.tsx`; `CheckoutNuevoRequest(List<ItemCheckoutRequest>)` | Staging `CheckoutCompradorPage.tsx` declara “un producto por orden”; `CheckoutController.cs` acepta `ProductoId` y `Cantidad` de un SKU; su propio mecanismo de idempotencia y pago incierto fue reforzado recientemente | **Recuperar experiencia del carrito, NO el controlador antiguo.** Diseñar contrato nuevo multi-SKU con validación de un vendedor o subórdenes, precio del servidor, reservas atómicas, firma de payload, reconciliación del pago, anulación y restitución. Probar reintentos idénticos/contradictorios y dos compradores concurrentes |
| Secuencias y unicidad logística | `integracion-main-auditoria:src/MarketplaceERP.Catalog/Migrations/20261028174715_NumeracionYConcurrenciaCatalog.cs` | Crea `catalog.NumeroGuiaSeq`, `catalog.NumeroPickListSeq`, indices únicos para `NumeroGuia`/`NumeroPickList` y agrega `xmin`; staging genera valores aleatorios y su esquema carece de índices únicos en esos números | Recuperar el **principio** de secuencias/índices mediante NUEVA migración basada en snapshot vigente y después de consultas por duplicados. No reaplicar migración de 2026-10-28 ni asumir que una orden solo puede tener un paquete legítimamente |
| Idempotencia asiento de venta | `integracion-main-auditoria:src/MarketplaceERP.Finance/Migrations/20261022184358_ContabilidadSecuenciaEIdempotencia.cs` | Staging protege parcialmente ventas con chequeo aplicativo, pero Neon no tiene `UNIQUE(TipoOperacion='Venta', ReferenciaOrigenId)` | Migración nueva, previa conciliación de duplicados históricos; serializar generación y exigir unicidad para carrera real; test PostgreSQL de dos solicitudes concurrentes. No importar snapshot histórico |
| Liquidaciones uno a muchos | `integracion-main-auditoria:src/MarketplaceERP.Finance/Migrations/20261020205544_LiquidacionUnoAMuchosSplits.cs` | Staging conserva esquema y controles más actuales, incluso la semántica de pago simulado. Migración antigua cambia cardinalidades | **No recuperar sin un caso de negocio y una migración de datos reversible.** Evitar perder integridad de pagos y splits |
| Hardening roles/JWT | `integracion/fase1-seguridad` | Diez commits divergentes pero **0 rutas de archivo distintas** frente a staging | Sin port de código. Conservar defensa existente, pruebas de regresión y revisión de historia |
| Bloqueos/recuperación de compras | `fix/compras-serializar-recepcion-20261107`, `fix/compras-verificar-reintentos-inbound-20261107` | Diferencias en controladores y tests antiguos. Staging incorpora correcciones más nuevas de recepción y reservas | Comparar escenarios y agregar solo pruebas faltantes; no reemplazar servicio de inventario completo |
| Seguridad y expiración de login en navegador | `fix/p1-browser-auth-lifecycle-20261107` | Diez rutas distintas en rama anterior; staging mantiene pruebas browser y funciones nuevas de autenticación | Conservar staging; tests del ciclo de renovación y logout ante expiración |
| Varios respaldos `backup/fase1-*` | Ocho ramas históricamente divergentes | **0 archivos distintos** en comparación actual con staging | No son fuentes de mejoras funcionales nuevas. Conservar referencias SHA en censo externo hasta confirmar bundle/worktrees; candidatas para poda |
| Tests aceptaciones PR antiguos | `fix/p0-integracion-postgres-20261107`, ramas `p1` | Hay árboles de test separados y cambios sin integrar textualmente | Verificar si cada escenario está representado en CI actual antes de descartar. Priorizar cobertura, no identidad textual |

## Condiciones mínimas para rescatar el carrito

- No convertir compra de 1 SKU a N mediante simples bucles de `POST /api/checkout`: varios pagos y reservas independientes pueden producir estados financieros diferentes y cobros parciales.
- Definir explícitamente vendedores múltiples y B2B vs B2C; documentar impuestos y método de pago.
- Preservar `Idempotency-Key` del comprador, hash canónico del carrito, protección `PagoResultadoInciertoException`, límites y stock consultado por servidor.
- Probar disponibilidad de stock concurrente y compensación ante fallos.
- Reglas de seguridad: validar permisos, nunca recuperar productos demo ficticios que el frontend antiguo mostraba como fallback si el API fallaba.

## Condiciones mínimas para rescatar secuencias

1. Preflight `scripts/consolidacion-integridad-readonly.sql` en entorno de ensayo con datos no vacíos.
2. Confirmar colisiones de claves numéricas y registros de la base productiva mediante SOLO lectura.
3. Adaptar `CatalogDbContext`/`FinanceDbContext` con snapshot vigente.
4. Crear migración ascendente y estrategia de rollback **sin perder datos**; migración sobre base clonada.
5. Tests PostgreSQL de solicitudes simultáneas desde DbContexts independientes.
6. CI completa del SHA exacto y revisión humana antes de ejecutar en staging.

## Status

**Decisiones de recuperación documentadas; implementación pendiente.** Este documento no afirma que la funcionalidad nueva exista. Los cambios críticos siguen aislados en PR #64 y no se han fusionado.
