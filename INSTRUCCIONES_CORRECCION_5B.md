# Corrección Fase 5B — Ocupación WMS

## Problema corregido

La Fase 5B calculaba una ubicación ocupada únicamente si tenía un `StockFisico` con `CantidadTotal > 0`.

El proyecto ya mantiene también el indicador persistente `UbicacionAlmacen.EstaOcupada`, y las pruebas heredadas esperan que este indicador siga siendo válido.

## Regla corregida

Una ubicación se considera ocupada cuando:

```text
EstaOcupada == true
O
existe StockFisico con CantidadTotal > 0
```

Una ubicación se considera disponible únicamente cuando ambas condiciones son falsas.

## Archivo reemplazado

`src/MarketplaceERP.Catalog/Services/WmsService.cs`

## No cambia

- modelo EF / PostgreSQL
- transferencias 5B
- InventoryService
- frontend
- CodigoEAN

## Validación

Después de copiar el parche ejecutar:

```powershell
dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

La prueba que fallaba debe volver a pasar:

`WmsService_ObtenerMapaBodega_CalculaCapacidadYOcupacion`
