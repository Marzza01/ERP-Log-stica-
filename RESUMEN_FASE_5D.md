# Resumen — Fase 5D Proveedores logísticos

La Fase 5D completa la integración logística del maestro `core.proveedores` con Reabastecimiento y Compras.

## Flujo

```text
Producto.ProveedorNombre
        ↓
normalización / vinculación
        ↓
core.proveedores
        ├─ Id real
        ├─ Nombre comercial / razón social
        ├─ OTIF histórico
        ├─ Plazo de crédito
        └─ contacto
        ↓
Reabastecimiento / Compras
```

## Resultado

- No existe un segundo maestro de proveedores.
- Reabastecimiento distingue proveedor vinculado vs. nombre no vinculado.
- Compras requiere `ProveedorId` activo del maestro.
- PO conserva el proveedor corporativo canónico.
- Se visualizan métricas logísticas por proveedor: SKU vinculados, SKU bajo mínimo, PO abiertas y unidades pendientes de recepción.
- Los códigos EAN permanecen fuera de alcance.
