# LOGIMKT — Fase 8A: ensayo integral automatizado del Grupo A

## Objetivo
Validar que las reglas construidas entre las Fases 4–7 funcionan juntas, no solo de forma aislada.

Este bloque **no agrega nuevas funciones de negocio ni cambia la base de datos**. Añade:

1. una prueba xUnit integral de logística del Grupo A;
2. un script PowerShell reproducible para validar frontend, backend, tests, migraciones y guardas de arquitectura;
3. documentación de aceptación para el cierre.

## Ensayo automatizado nuevo

`tests/MarketplaceERP.Catalog.Tests/GrupoALogisticaE2ETests.cs`

Recorre en una sola prueba:

1. Inbound real de 30 unidades.
2. Reserva de 5 unidades y confirmación sin consumo físico.
3. Transferencia WMS parcial de 10 unidades.
4. Bloqueo de 2 unidades por incidencia.
5. Picking, packing y despacho de 5 unidades.
6. RMA ReStock de 2 unidades.
7. Desbloqueo de las 2 unidades.
8. Conteo cíclico con diferencia confirmada.
9. Generación de ajuste negativo pendiente.
10. Aprobación y aplicación del ajuste.
11. Alerta derivada por stock crítico y reconocimiento.
12. Comprobación del ledger de movimientos e invariantes finales.

La prueba usa EF Core InMemory y no modifica PostgreSQL real.

## Aplicación
Copiar el ZIP sobre la raíz de `ERP-Log-stica-` conservando las rutas.

## Validación recomendada

Con Docker Desktop activo y PostgreSQL del proyecto en el puerto 5433:

```powershell
.\scripts\ValidarFase8GrupoA.ps1 -PostgresPort 5433
```

El script ejecuta:

- `npm.cmd run build`;
- `dotnet build MarketplaceERP.sln`;
- `dotnet test MarketplaceERP.sln --no-build`;
- comprobación de migraciones Catalog sin `(Pending)`;
- comprobación de que `IStockService` sigue registrado con `StockService` real;
- búsqueda de marcadores explícitos de datos demo/fallback en pantallas principales del Grupo A.

Si Docker no está disponible y solo se desea validar código temporalmente:

```powershell
.\scripts\ValidarFase8GrupoA.ps1 -OmitirBaseDatos
```

**No usar `-OmitirBaseDatos` para el cierre definitivo.**

## Resultado esperado
Antes de este bloque había 55 pruebas Catalog. Al añadir el ensayo integral esperamos:

- Core: 10/10
- Catalog: 56/56
- Finance: 22/22
- Total: 88/88
- Failed: 0

Si en el repositorio existe alguna prueba posterior legítima, el total puede ser mayor; lo obligatorio es `Failed: 0`.

## Base de datos
- No se agrega entidad.
- No se agrega tabla.
- No generar migración.
- No ejecutar `database update` por este bloque.

## Barcode / EAN
`CodigoEAN` continúa fuera del alcance del Grupo A y no es modificado.

## Commit sugerido

```powershell
git add .
git commit -m "Fase 8A: ensayo integral automatizado del Grupo A"
git status
```
