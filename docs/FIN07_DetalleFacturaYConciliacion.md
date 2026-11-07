# FIN-07 — Detalle de factura y conciliación por producto

## Problema corregido

Antes, `CantidadFacturada` del 3-Way Match se rellenaba copiando
`CantidadRecibidaBodega`. No existía evidencia independiente por producto y
una orden podía aprobarse aunque la factura tuviera cantidades o precios
distintos en cada SKU, siempre que los totales agregados coincidieran.

## Nuevo contrato (compatible con registro heredado)

`POST /api/compras/facturas` acepta opcionalmente `lineas`:

```json
{
  "ordenCompraId": "GUID_DE_PO",
  "numeroFactura": "CCF-001",
  "subTotal": 40.00,
  "montoTotal": 45.20,
  "fechaEmision": null,
  "fechaVencimiento": null,
  "lineas": [
    {
      "itemOrdenCompraId": "GUID_ITEM_A",
      "cantidadFacturada": 2,
      "precioUnitarioFacturado": 10.00
    },
    {
      "itemOrdenCompraId": "GUID_ITEM_B",
      "cantidadFacturada": 2,
      "precioUnitarioFacturado": 10.00
    }
  ]
}
```

La solicitud sin `lineas` aún registra el documento con
`detalleRegistrado=false` para mantener compatibilidad con el contrato
anterior. **No se puede aprobar en 3-Way Match sin completar el detalle
documental**: nunca se reconstruye desde WMS ni desde la PO.

Para una factura antigua aún no aprobada ni pagada:
`POST /api/compras/facturas/{id}/lineas`, con un arreglo JSON de líneas.
Esta operación solo permite añadir el detalle una vez. No modifica montos
existentes, no permite precios/cantidades inválidos, líneas de otras PO o
subtotales que no concuerden. Solo accesible a Admin.

Las líneas de factura se guardan en `finance.items_factura_proveedor`
mediante migración `20261107022000_FIN07DetalleFacturaProveedor`.
Índice único `(FacturaProveedorId, ItemOrdenCompraId)`, llaves foráneas
y restricciones CHECK para valores positivos. Es una migración **aditiva**:
no borra facturas ni inserta líneas inventadas.

## Reglas de autorización

El 3-Way compara por **ItemOrdenCompraId**:

- Cantidad solicitada en PO contra recibida WMS y facturada independiente;
- Precio unitario acordado contra precio que aparece en la factura;
- Suma real de líneas contra subtotal facturado;
- Importe total PO contra importe total factura.

Que los totales de unidades coincidan no basta si las unidades pertenecen
a SKU diferentes. Que los importes totales coincidan tampoco basta cuando
los precios por SKU difieren. Ambos casos quedan sin autorización.

Las facturas históricas sin líneas quedan intactas y no son aprobables
hasta cargar evidencia real. Los registros con líneas no se pueden
sobrescribir desde el endpoint de completar; las correcciones documentales
posteriores requerirán un flujo de notas de crédito/ajustes auditados.

## Estado de despliegue

FIN-06 introdujo `20261107010000_FIN06IntegridadCompras`.
**Aplicar y validar FIN-06 ANTES de FIN-07**, únicamente tras snapshot y
pruebas en clon. Esta PR no ejecuta `MigrateAsync` sobre Neon y no debe
desplegarse automáticamente a Render. Ver `docs/FIN06_MigracionSegura.md`.

Tras migrar el clon, ejecutar pruebas de facturas multi-SKU, documentos
antiguos, duplicados, concurrencia y reversión contable, y comprobar el
historial de migraciones. Hasta entonces, mantener la API desplegada
anterior, sin habilitar pasarela ni transferencias reales.

## Limitaciones

- FIN-07 no convierte facturas externas a líneas ni verifica la autenticidad
  de un comprobante: el administrador debe transcribir datos del documento.
- No implementa pagos parciales por línea, crédito fiscal especial ni
  numeración tributaria oficial.
- Una factura parcialmente recibida se registra y muestra discrepancia; no
  se autoautoriza.
