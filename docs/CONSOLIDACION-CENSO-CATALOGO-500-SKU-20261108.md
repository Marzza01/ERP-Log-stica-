# Censo de datos de catálogo en ramas remotas (solo lectura) — 8 nov 2026

## Fuente y alcance

Se leyó por GitHub el archivo `docs/data/Catalogo_500_Productos.csv` de tres ramas:

- `staging/catalog-500-neon-test`
- `staging/vercel-render-neon-test`
- `consolidacion/erp-segura-20261108`

En las tres ramas, **el blob es idéntico**: `0ef5f4728059b6263289091f6b2d464df16424fc`.

Estos resultados corresponden al **CSV versionado en GitHub**, no a las tres variantes locales que aún deben inspeccionarse, ni prueban el estado actual en Neon.

## Resultados de lectura de las 500 filas

| Comprobación | Resultado |
|---|---:|
| Filas de productos | 500 |
| Columnas de encabezado | 16 |
| Productos marcados `Activo` | **488** |
| Productos marcados `Inactivo` | **12** |
| Categorías distintas | 6 |
| Proveedores *nombrados en CSV* | 14 |
| SKU vacío / repetido | 0 / 0 |
| EAN vacío / repetido | 0 / 0 |
| Filas con columnas distintas de 16 | 0 |
| Precios B2C/B2B no numéricos o no positivos | 0 |
| Precio B2C menor a USD 5 por unidad | 12 |
| Precio B2B mayor al B2C en la misma fila | 0 |

### Distribución por categoría

| Categoría | Filas |
|---|---:|
| Electrónica y Tecnología | 100 |
| Ropa y Accesorios | 90 |
| Alimentos y Bebidas | 80 |
| Ferretería e Industria | 80 |
| Hogar y Decoración | 80 |
| Salud y Cuidado Personal | 70 |

**Interpretación restringida:**

1. La exigencia de tener **500 productos activos/publicados** no se demuestra con este CSV tal como está: solo 488 indican `Activo`. Los 12 productos inactivos no deben activarse automáticamente sin comprobar la rúbrica y si la situación operativa permite su publicación.
2. Los **14 nombres de proveedores en el CSV** no equivalen a 14 registros válidos en la tabla `core.proveedores`. Una verificación anterior en la BD aislada encontró cuatro registros reales, no cinco; debe comprobarse con el contexto más reciente sin insertar proveedores en producción.
3. Un precio unitario B2C de menos de USD 5 **no demuestra** por sí mismo incumplir un mínimo de **pedido** B2C de USD 5. Es un control distinto que depende de checkout/OMS.
4. Que los tres blobs remotos sean idénticos no permite elegir entre las tres variantes locales del usuario ni autoriza sobreescribirlas.

## Gate recomendado

- Preservar el CSV real del usuario y las tres variantes locales; el test de 500 SKUs ahora genera una **copia sintética en carpeta temporal** (rama candidata), en vez de sobreescribir el archivo versionado.
- La CI candidata tiene un control posterior a las pruebas `dotnet test` para fallar si el archivo versionado `docs/data/Catalogo_500_Productos.csv` fue modificado por alguna prueba.
- Una vez analizado el ZIP privado, decidir cuál CSV y qué estados de productos usar sin alterar datos históricos ni movimientos de stock.
- Para aceptación de la rúbrica, auditar por separado: 500 productos, 500 publicaciones activas, 5 proveedores en Core y mínimos por **pedido** en B2B/B2C.

**No se ha modificado ninguna fila de catálogo o esquema de PostgreSQL.**
