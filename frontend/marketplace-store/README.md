# LOGIMKT Marketplace · Módulo C

Tienda web B2B/B2C de LOGIMKT S.A. de C.V. integrada con MarketplaceERP. Se despliega de forma independiente en Vercel, mantiene su propia base PostgreSQL y consume el ERP exclusivamente mediante su API HTTP.

## Arquitectura

- Tienda: Next.js 16, React 19, TypeScript y Tailwind.
- Base de datos de tienda: PostgreSQL propio (DATABASE_URL). No reutilizar marketplaceerp_test ni escribir directamente en la base del ERP.
- ERP/OMS: ASP.NET Core 8 en Render. El ERP es la fuente de verdad para catálogo publicado, precios, disponibilidad, reservas, estado formal de pedidos y devoluciones.
- Identidad: cuenta técnica Comprador para crear pedidos y cuenta técnica Admin para sincronizar catálogo, validar pagos y operar estados.
- Desarrollo: Vercel logimkt-marketplace-development, rama development, API https://marketplaceerp-api-staging.onrender.com, base Neon logimkt_marketplace_dev.

## Funciones

| Ruta | Función |
| --- | --- |
| / | Portada y categorías |
| /catalogo | Catálogo, búsqueda y filtros |
| /producto/[sku] | Ficha, precio por canal y disponibilidad |
| /carrito, /checkout | Carrito y checkout B2B/B2C |
| /pedido/[code] | Seguimiento con código aleatorio no secuencial |
| /politicas | Términos, pagos, envíos, cancelaciones y devoluciones |
| /admin | Dashboard de operación |
| /admin/pedidos | Pedidos, estados, pagos, cancelaciones y RMA |
| /admin/inventario | Inventario local y carga masiva |
| /admin/erp | Conexión, sincronización de catálogo y reintentos |

## Catálogo maestro

El archivo data/Catalogo_500_Productos_ERP.csv es el conjunto semilla de 500 SKU del ERP. data/catalogo.json se genera desde ese CSV con:

    npm run catalog:from-erp

En el primer arranque, la tienda importa el catálogo semilla a su propia base. Después, Sincronizar catálogo y stock consulta el endpoint de productos del ERP con credenciales Admin y hace upsert por SKU. La sincronización:

- Actualiza nombre, categoría, subcategoría, EAN, proveedor, dimensiones, unidad, precios B2B/B2C y disponibilidad.
- Inserta productos nuevos publicados que aún no existan en la tienda.
- Marca como inactivos los productos que ya no aparecen publicados en el ERP.
- Conserva la imagen local cuando el ERP no entrega una URL de imagen.
- Se bloquea si existen pedidos Pendiente ERP, para no sobrescribir reservas locales pendientes.

No se generan correspondencias aproximadas por nombre ni se reemplazan SKU oficiales. El SKU del ERP es la llave.

## Flujo de pedidos

1. La tienda valida el carrito y crea un código de seguimiento aleatorio.
2. Si ERP está configurado, el pedido local queda Pendiente ERP; todavía no se presenta como confirmado.
3. Next.js envía el pedido al endpoint de integración del ERP. El ERP recalcula precios e impuestos y reserva el inventario.
4. Solo después de la respuesta satisfactoria del ERP, la tienda marca el pedido como Confirmado y guarda el identificador y número ERP.
5. Si la comunicación falla, el pedido permanece pendiente y se puede reintentar desde el panel. El código externo y la idempotencia evitan crear órdenes duplicadas.
6. Los cambios de estado, cancelaciones y solicitudes RMA se envían al ERP antes de reflejar el resultado local.

El carrito se limita a productos del mismo vendedor, como exige el contrato actual del ERP.

## Pagos

Los pagos reales, las pasarelas y los cargos con tarjeta permanecen deshabilitados. La tienda solo permite:

- B2C: transferencia bancaria y pago contra entrega.
- B2B: transferencia bancaria y crédito a 30 días.

Transferencias y crédito requieren validación administrativa en el ERP. El cobro contra entrega se confirma después de la entrega. Una devolución crea una solicitud RMA pendiente de inspección; no reingresa inventario ni emite reembolsos automáticamente.

## Variables de entorno

Configurar como secretos en Vercel; nunca guardar valores reales en Git:

| Variable | Uso |
| --- | --- |
| DATABASE_URL | PostgreSQL exclusivo de la tienda |
| ADMIN_PASSWORD | Acceso al panel Admin de la tienda |
| ERP_API_URL | API del ERP; en Desarrollo usar Render Staging |
| ERP_EMAIL / ERP_PASSWORD | Cuenta técnica ERP con rol Comprador |
| ERP_ADMIN_EMAIL / ERP_ADMIN_PASSWORD | Cuenta técnica ERP con rol Admin; necesaria para sincronización completa y acciones operativas |

Sin variables ERP_*, la tienda puede ejecutarse en modo local de demostración. Ese modo no confirma ni reserva pedidos en el ERP.

## Desarrollo y validación

Desde esta carpeta:

    npm ci
    npm run catalog:from-erp
    npm run lint
    npm run build

CI ejecuta estas validaciones en Node 24. La integración backend se valida con dotnet build MarketplaceERP.sln y dotnet test MarketplaceERP.sln.

## Despliegue

- Proyecto Vercel: logimkt-marketplace-development.
- Root Directory: frontend/marketplace-store.
- Rama de pruebas: development (Preview).
- Base de datos: logimkt_marketplace_dev en el proyecto Neon de Desarrollo.
- API: https://marketplaceerp-api-staging.onrender.com.

No apuntar esta configuración a Producción. La configuración productiva se preparará únicamente después de aceptar las pruebas manuales en Desarrollo y aprobar la liberación a main.
