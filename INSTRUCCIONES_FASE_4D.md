# LOGIMKT — Fase 4D: Fulfillment / Outbound

## Objetivo

Conectar el despacho físico de Fulfillment con las reservas reales implementadas en Fase 4B.

Regla principal:

```text
Checkout
→ reserva y confirma

Picking
→ recoge exactamente las ubicaciones reservadas

Packing
→ prepara paquete, sin modificar inventario

Despacho
→ consume reserva
→ reduce stock físico
→ reduce stock reservado
→ registra MovimientoInventario.DespachoOutbound
→ Paquete = Despachado
```

## Cambios principales

### 1. IStockService

Se agrega:

```csharp
Task ConsumirReservasOrdenAsync(Guid ordenId);
```

### 2. StockService

El consumo Outbound:

- acepta únicamente reservas confirmadas;
- procesa todos los detalles de ubicación;
- disminuye `CantidadTotal`;
- disminuye `CantidadReservada`;
- incrementa `CantidadConsumida`;
- actualiza el estado de reserva;
- registra movimientos `DespachoOutbound`;
- es idempotente si la orden ya fue consumida.

En PostgreSQL se bloquean las filas necesarias durante el consumo.

### 3. Fulfillment

`GenerarPickListAsync` deja de escoger cualquier stock del producto.
Ahora toma las ubicaciones de `ReservaStockDetalle`.

`EmpacarYRotularAsync` exige una PickList completada.

`DespacharPaqueteAsync`:
- exige estado `ListoParaDespacho`;
- exige picking completado;
- consume las reservas;
- marca `EstadoPacking.Despachado`;
- guarda `FechaDespacho`;
- evita doble descuento ante un segundo intento.

### 4. Frontend

Se agrega `Despachado` a `EstadoPaquete`.

La pantalla ya no muestra un falso “Despachado (Local)” si la API rechaza el despacho.
Si el backend falla, muestra error y conserva el paquete pendiente.

### 5. API

Un despacho inválido responde HTTP 409 con:

```json
{
  "codigo": "DESPACHO_INVALIDO"
}
```

## Base de datos

Fase 4D NO cambia el esquema.

No ejecutar:

```powershell
dotnet ef migrations add ...
dotnet ef database update
```

## Aplicación

1. Extraer el ZIP.
2. Copiar el contenido sobre la raíz de `ERP-Log-stica-`.
3. Aceptar reemplazos.

## Validación

Frontend:

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..
```

Backend:

```powershell
dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

Resultado esperado de pruebas:

```text
Core:      6
Catalog:  25
Finance:  22

TOTAL:    53
```

Las tres pruebas adicionales cubren:
- consumo real de reserva/stock;
- rechazo de reserva no confirmada;
- despacho idempotente desde Fulfillment.

## Git

Si todo pasa:

```powershell
git add .
git commit -m "Fase 4D: Fulfillment outbound consume reservas y stock"
git status
```

## Código de barras

Sin cambios.

`Producto.CodigoEAN` continúa pendiente para otro integrante.
