# FIN-06 — Concurrencia e integridad de compras

**Alcance:** índices de protección y transacciones para registro de facturas,
aprobación 3-Way y asiento de compra. No habilita desembolsos bancarios.

## Restricciones en PostgreSQL

La migración `20261107010000_FIN06IntegridadCompras` crea tres índices únicos:

1. `UX_FIN06_facturas_proveedor_numero`: una combinación
   `ProveedorId + upper(trim(NumeroFactura))`, incluso entre distintas PO.
2. `UX_FIN06_match_po_autorizado`: solo un match con
   `AutorizadoParaPago = TRUE` por PO. Se permiten varios matches de
   discrepancia para mantener historial de auditoría.
3. `UX_FIN06_asiento_compra_po`: solo un asiento `TipoOperacion='Compra'`
   con `ReferenciaOrigenId` no nulo por PO.

Son índices de expresión o parciales administrados **por SQL en la migración
EF**. No están en el modelo convencional EF ni deben borrarse por «limpieza»
de migraciones. El `Down` elimina los índices, lo que **quita protección**;
nunca aplicarlo de forma automática como rollback operativo.

## Consulta PREVIA obligatoria — solo lectura

Sobre cada base de destino, con credenciales de solo lectura:

```sql
-- Facturas duplicadas de un mismo proveedor, sin distinción de mayúsculas o espacios.
SELECT "ProveedorId", upper(btrim("NumeroFactura")) AS numero, count(*) AS veces
FROM finance.facturas_proveedor
GROUP BY 1,2 HAVING count(*) > 1;

-- Más de un match autorizado de la misma PO.
SELECT "OrdenCompraId", count(*) AS veces
FROM finance.matches_tridireccionales
WHERE "AutorizadoParaPago" = TRUE
GROUP BY 1 HAVING count(*) > 1;

-- Más de un asiento de compra de la misma PO.
SELECT "ReferenciaOrigenId", count(*) AS veces
FROM finance.partidas_contables
WHERE "TipoOperacion" = 'Compra' AND "ReferenciaOrigenId" IS NOT NULL
GROUP BY 1 HAVING count(*) > 1;

-- Historial actual Finance y última migración aplicada.
SELECT "MigrationId" FROM finance."__EFMigrationsHistory"
ORDER BY "MigrationId";
```

**Si alguno devuelve filas, detener despliegue.** No borrar ni modificar
facturas, matches o asientos automáticamente: conciliación manual, revisión
con responsable contable y plan de reparación auditado.

## Proceso de promoción

1. Confirmar snapshot/backup recuperable de la base destino y verificar
   identidad exacta de proyecto, rama y base de datos. No utilizar el
   `TEST_PG_CONNECTION` de CI para Neon.
2. CI del PR debe pasar sobre PostgreSQL efímero del runner (migración,
   dos matches simultáneos, dos facturas simultáneas y rollback completo).
3. Ejecutar las consultas de preflight sobre **un clon** del destino;
   verificar sus resultados, migrar el clon y repetir las pruebas HTTP.
4. Aplicar la migración mediante un procedimiento supervisado con
   credenciales de mínimo privilegio, preferiblemente durante una ventana
   de mantenimiento; estos índices bloquean escrituras durante su creación.
5. Recién entonces desplegar la versión de API que exige los índices.
   No activar pagos ni desembolsos reales.
6. Tras desplegar: confirmar que las tres restricciones aparecen en
   `pg_indexes`, comprobar el CI y verificar un solo match/asiento por PO.

Si la migración no se completa, mantener el despliegue anterior.
Si la API falla, revertir **solo la aplicación** al commit anterior y dejar
intactos los índices protectores hasta una revisión técnica.

## Diferencia entre prueba local y producción

Las pruebas FIN-06 crean una base PostgreSQL 15 temporal con nombre
`ci_fin06_<GUID>` exclusivamente en localhost:5432. Se elimina al terminar;
nunca accede a Neon. Las pruebas InMemory existentes no demuestran
concurrencia: solo la suite PostgreSQL lo hace.

**Límites conocidos:** el ERP todavía no implementa un proveedor bancario
real, y el detalle de cantidades por línea de factura sigue pendiente para
la siguiente revisión funcional del 3-Way Match.
