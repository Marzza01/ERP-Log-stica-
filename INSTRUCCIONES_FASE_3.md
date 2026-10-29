# LOGIMKT — Fase 3: Corrección de contratos Frontend ↔ Backend

## Alcance
Este bloque corrige incompatibilidades contractuales del Grupo A sin implementar todavía el nuevo núcleo de stock/reservas multiubicación.

## Cambios principales

### Productos
- Frontend alineado con los campos reales de `Producto`: `precioB2C`, `precioB2B`, `categoriaNombre`, `proveedorNombre`, `stockMinimo`, etc.
- Parámetros de búsqueda corregidos a `precioMin` / `precioMax`.
- Respuesta paginada de `/api/productos/buscar` corregida.
- Se agregan `PUT /api/productos/{id}` y `DELETE /api/productos/{id}`.
- DELETE realiza baja lógica (`EstaPublicado=false`, `EstadoProducto=Inactivo`).
- El `CodigoEAN` NO se modifica en edición. Queda reservado al integrante responsable de códigos de barras.

### WMS
- Tipos TypeScript alineados con `ResumenBodegaDto` y `UbicacionAlmacen`.
- Se usan `ocupadas`, `disponibles`, `porcentajeOcupacion` y objetos de pasillo reales.
- El modal “Reubicar Stock” ahora llama realmente a `POST /api/wms/reubicar`.

### Fulfillment
- Contrato de picking usa `cantidadRecogida`, igual al backend.
- Packing incorpora los campos obligatorios que faltaban: `ordenId`, `direccionEntrega`, `contactoReceptor`, `telefonoReceptor`.
- Estados frontend alineados con `EstadoPicking` y `EstadoPacking` actuales.
- Los enums de Catalog se serializan como texto para que React y .NET compartan los mismos valores.

### RMA
- Solicitud alineada a `OrdenId`, `ProductoId`, `Cantidad`, `MotivoCliente`.
- Dictamen alineado a `Clasificacion`, `Inspector`, `DictamenTecnico`, `DescuentoRemate`.
- Estados y clasificaciones alineados con el backend.
- `ReStock`, `MermaScrap` y `Remate` se transmiten como strings válidos.

### Compras / Inbound
- `recibirMercancia` cambia de POST a PUT, coincidiendo con el backend.
- Se agrega `POST /api/compras/po/{id}/enviar`, que faltaba aunque el frontend ya lo utilizaba.
- Se crea una capa adaptadora en `api/compras.ts` para representar los enums/DTO de Finance en el modelo visual existente sin rediseñar el módulo de Compras.
- La recepción física a inventario todavía NO se implementa aquí; corresponde a una fase posterior.

## Códigos de barras
NO se implementan en este bloque.

Se preserva `Producto.CodigoEAN` y la edición de Productos no lo sobrescribe.

## Aplicación
Copiar el contenido del ZIP sobre la raíz del repositorio general y aceptar reemplazar los archivos indicados.

No borrar la base PostgreSQL ni ejecutar nuevas migraciones: esta fase no contiene cambios de esquema.

## Validación
Se realizó validación de estructura/sintaxis de los archivos modificados.

No fue posible completar `npm ci` en el entorno de generación antes del límite de ejecución, por lo que el build completo de Vite debe verificarse localmente después de copiar el bloque:

```powershell
cd frontend\marketplace-ui
npm install
npm run build
```

Para backend:

```powershell
dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Si alguno devuelve un error, no avanzar a la Fase 4: corregir primero el error y volver a ejecutar.
