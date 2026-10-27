# Corrección frontend previa al cierre de Fase 6D

Corrige los 3 errores TypeScript detectados después de aplicar 6D.

## Archivos incluidos

- `frontend/marketplace-ui/src/api/compras.ts`
- `frontend/marketplace-ui/src/types/index.ts`

## Correcciones

1. Restaura/exporta los contratos de recepción Inbound de Compras:
   - `RecibirMercaderiaItemRequest`
   - `RecibirMercaderiaRequest`
2. Amplía `EstadoPaquete` para reflejar el enum real del backend:
   - `Pendiente`
   - `Empacando`
   - `ListoParaDespacho`
   - `Despachado`

El backend `EstadoPacking` ya contiene `Despachado`, por lo que esta corrección alinea el contrato TypeScript con la API real.

## Aplicación

Copiar el contenido del ZIP sobre la raíz de `ERP-Log-stica-`, aceptando reemplazo de los dos archivos.

## Validación obligatoria

```powershell
cd frontend\marketplace-ui
npm.cmd run build
cd ..\..

dotnet build MarketplaceERP.sln
dotnet test MarketplaceERP.sln
```

No cerrar Fase 6D hasta que `npm.cmd run build` finalice sin errores TypeScript y los tests .NET queden con `Failed: 0`.

## Base de datos

No requiere migración ni `database update`.

## Fuera de alcance

`CodigoEAN` no se modifica.
