# Despliegue de LOGIMKT Marketplace · Desarrollo

Esta guía corresponde únicamente al entorno de Desarrollo. No usar credenciales ni endpoints de Producción.

## Recursos

| Recurso | Configuración |
| --- | --- |
| GitHub | Marzza01/ERP-Log-stica- |
| Rama | development |
| Vercel | logimkt-marketplace-development |
| Root Directory | frontend/marketplace-store |
| Neon | Proyecto marketplaceerp-development, base logimkt_marketplace_dev |
| Render API | https://marketplaceerp-api-staging.onrender.com |

La base de la tienda es independiente de marketplaceerp_test. La cuenta de PostgreSQL de la tienda solo debe tener acceso a logimkt_marketplace_dev.

## Variables Vercel · Preview / development

| Variable | Tipo | Descripción |
| --- | --- | --- |
| DATABASE_URL | Encrypted | Cadena PostgreSQL de logimkt_marketplace_app |
| ADMIN_PASSWORD | Encrypted | Contraseña del panel de la tienda |
| ERP_API_URL | Plain | URL fija de Render Staging |
| ERP_EMAIL | Plain | Cuenta técnica ERP Comprador |
| ERP_PASSWORD | Encrypted | Contraseña de cuenta técnica Comprador |
| ERP_ADMIN_EMAIL | Plain | Cuenta técnica ERP Admin |
| ERP_ADMIN_PASSWORD | Encrypted | Contraseña de cuenta técnica Admin |

No copiar secretos a este documento, al repositorio ni a logs. Las credenciales ERP se usan solo en el servidor de Next.js.

## Activación inicial

1. Desplegar la rama development en Vercel.
2. Abrir /admin con la contraseña ADMIN_PASSWORD.
3. Entrar en Conexión ERP y probar autenticación.
4. Ejecutar Sincronizar catálogo y stock. La acción requiere la cuenta ERP Admin.
5. Confirmar que el número de SKU vinculados coincide con el catálogo publicado que devuelve el ERP; revisar los SKU sin precio B2B y los productos sin stock.
6. Ejecutar compras de prueba B2C y B2B, validar reservas, cambios de estado, pagos manuales, cancelaciones y RMA.
7. Repetir pruebas cuando haya correcciones. No conectar Producción durante esta fase.

## Criterios de seguridad

- No habilitar pasarela ni cargos reales.
- No usar la base de datos del ERP desde la tienda.
- No sincronizar catálogo mientras haya pedidos Pendiente ERP.
- Un pedido no se muestra como confirmado hasta que el ERP acepte la orden y reserve inventario.
- Cancelar en el ERP antes de liberar el inventario local.
- Las devoluciones permanecen pendientes de inspección; no marcar reembolso ni reingreso automático.
- Mantener Vercel Preview protegido por autenticación del equipo.
