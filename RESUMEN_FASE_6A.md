# LOGIMKT — Resumen Fase 6A

La Fase 6A incorpora ajustes auditables de inventario al proyecto general.

## Backend

- Nueva entidad `AjusteInventario`.
- Nuevo servicio `IAjusteInventarioService` / `AjusteInventarioService`.
- Nuevo controlador `AjustesInventarioController`.
- Nuevo `DbSet<AjusteInventario>` y configuración EF.
- Aplicación de ajuste genera `MovimientoInventario`.

## Frontend

- Nueva ruta `/ajustes`.
- Nueva opción **Ajustes de inventario** en Operaciones.
- Alta de solicitud por saldo WMS.
- Acciones Aprobar, Rechazar, Cancelar y Aplicar.

## Seguridad funcional

- Crear y aprobar no cambian stock.
- Ajuste negativo no consume reservado/bloqueado.
- Aplicación es idempotente.
- Solo Admin puede aprobar/rechazar/aplicar en API.

## Base de datos

Requiere una migración nueva para `catalog.ajustes_inventario`. La migración no se incluye prefabricada: debe generarse con EF Core sobre la copia integrada y revisarse antes de `database update`.

## Fuera de alcance

- Conteos cíclicos (Fase 6B).
- Alertas operativas finales (Fase 6C).
- Código de barras/EAN (otro integrante).
