# P1 · Integridad histórica y semántica de liquidaciones (staging)

## Objetivo y límites

P0 aceptó el flujo de compras y recepción WMS contra Render + Neon staging.
P1 refuerza los riesgos del plan de integración: unicidad y vínculos históricos,
conciliaciones ambiguas y **no presentar asientos internos como transferencias bancarias**.

Ámbito autorizado: GitHub branch `staging/catalog-500-neon-test`, Render staging y
Neon branch `test-migraciones-erp-20261104` (proyecto `square-lake-18241058`).
No modificar GitHub `main`, Render producción, Neon `main` ni transferencias externas.

## Auditoría SQL de solo lectura

El archivo `scripts/p1-staging-integrity.sql` contiene comprobaciones que se
ejecutan seleccionando **explícitamente la rama de staging** en Neon SQL Editor.
No contiene DDL, DML ni credenciales.

- 10 comprobaciones de PO/factura, split/liquidación, asiento, y cantidades Finance/WMS.
- 12 comprobaciones de unicidad Checkout, intentos de pago, conciliación, reembolso,
  reservas multiubicación y stock físico.
- Conteos de migraciones EF y registros financieros como metadatos.

**Resultado 2026-11-07:** las 22 comprobaciones devolvieron 0 anomalías sobre
la rama de prueba. La aceptación P0 creó dos PO, tres entradas inbound y una
factura no pagada; ninguna liquidación ni asiento.

**Límite de evidencia:** las tablas de intentos, reembolsos, conciliaciones y
liquidaciones están vacías en este entorno. Un cero detectado no acredita que
una migración de datos históricos funcione sobre otra rama con filas reales.
Estas consultas tampoco verifican los movimientos sin referencia WMS `ITEM-`,
ni deducen coincidencias de transacciones huérfanas por importe.

## Semántica del pago

El flujo de liquidaciones actual solo admite `Payments:Provider=Mock` en
Development cuando se habilita expresamente, y `Disabled` en los demás
entornos. El método histórico `ProcesarPagoAsync` produce **asiento simulado
y estado interno**; no realiza ni confirma transferencia bancaria externa.

La API expone de forma aditiva:
- `registroContableSimulado=true` en la acción de registro.
- `transferenciaBancariaConfirmada=false` en generación, registro e historial.
- Un aviso explícito que aclara qué significa `Completada`.

Se conservan los campos anteriores y el nombre del endpoint para no romper
clientes existentes. La interfaz web ya advierte que los registros contables
no equivalen a pagos bancarios. La prueba automatizada
`P1LiquidacionesSemanticaHttpTests` bloquea regresiones semánticas.

## Condiciones para activar un proveedor de pagos real

No habilitar pagos hasta contar con un gateway externo aprobado, un identificador
de transferencia verificable, conciliación de cobros inciertos, reembolsos
idempotentes, y ensayos aislados en PostgreSQL con datos históricos restaurados.

Las liquidaciones y entradas contables existentes no son evidencia de fondos
enviados. Un conciliador no puede atribuir pagos huérfanos por coincidencia
parcial de monto, fecha o transacción.

## Aceptación

- Pruebas de compilación, JWT, control de accesos, y suites .NET/React en GitHub Actions.
- Regresión nueva: API no certifica transferencia bancaria para simulación.
- SQL auditoría de solo lectura con 22 comprobaciones de anomalías = 0 en staging.
- Sin ejecutar `--execute` de P0 una segunda vez.
- Sin migraciones ni cambios de datos en staging o producción desde este PR.

**P1 de integridad y semántica en staging** puede aceptarse con estos criterios;
el despliegue de un proveedor financiero real y los backfills de otras ramas
constituyen trabajos independientes y requieren diseño/aprobación específica.
