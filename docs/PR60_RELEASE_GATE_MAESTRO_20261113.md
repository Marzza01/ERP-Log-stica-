# PR #60 | Puerta de release y alineación a main
Fecha del corte: 13-nov-2026 (El Salvador). Documento operativo, no aprobación de merge.
Repositorio: Marzza01/ERP-Log-stica-. No crear ramas Git nuevas.

## 1. Situación comprobada (solo lectura)
- Candidato: staging/catalog-500-neon-test@86f5eb8dcbfbcad19d41eda180561403fa70b355.
- Base de main: e04c90696e59d86390216a8b51bf00e876cfebc5.
- PR #60 OPEN/DRAFT, mergeable, 305 commits por delante, 0 detrás; 280 archivos.
- GitHub Actions run 43543711578: 4/4 SUCCESS. Logs: Core 10/10, Catalog 92/92, Finance 664/664; total 766/766 .NET, 0 fallidas/omitidas.
- Chromium CI: autenticación Admin/Vendedor/Comprador, restricciones 401/403, escenarios con API real sobre PostgreSQL local descartable y 2 vendedores/3 órdenes aisladas; NO sustituyen aceptación de Preview real.
- STAGING Render marketplaceerp-api-staging: dep-dbrs1cuk1f9s73fh9n0g LIVE, SHA 86f5eb8, AutoDeploy OFF.
- Vercel Preview: dpl_9uVG3SVHkEdZ5tHAHX5AaUQML2gU READY, SHA 86f5eb8.
- F267 STAGING: Admin/Comprador/Vendedor JWT 200, acceso Admin ajeno 403, comprador POST Checkout 503 PagosDeshabilitados, anónimo 401, QA recovery disabled. Cuentas F270 con claves DPAPI privadas Windows.
- Neon STAGING: tiny-sound-11266101 / br-aged-morning-bbordmzw / marketplaceerp_test; 39 tablas Core2, Catalog7, Finance10.
- Índices únicos catalog WMS verificados en Neon STAGING: UX_WMS_PickList_OrdenId, UX_WMS_Paquete_OrdenId, UX_WMS_PickList_NumeroPickList, UX_WMS_Paquete_NumeroGuia. Cero pares duplicados por OrdenId en datos actuales.
- Datos STAGING al corte antes de F270: 2 órdenes y 2 reservas registradas (no confundir con transacciones de producción).
- Producción Vercel usa main@e04c906 y middleware /api apunta a https://marketplaceerp-api.onrender.com.
- Render Production marketplaceerp-api usa rama integracion/main-reconciliacion-20261102, SHA LIVE 77e9a16ea2b9d7d7cdd3c83c65bf9bb1b4e81d1, AutoDeploy ON.
- **La identidad exacta de Neon Production que consume Render Production todavía NO está certificada.**
- El proyecto Neon square-lake-18241058 tiene varias ramas; ninguna puede asumirse producción solo por llamarse main.

## 2. Bloqueadores de seguridad ANTES del merge
1. F268 BACKUP & RESTORE del STAGING ACTUAL: el snapshot F171 fue pre-migraciones y crear otro devolvió 422 limite de snapshots. La copia P5 tiene 37 tablas (Core2/Catalog6/Finance6), no las 39 actuales. Ejecutar scripts/f268-backup-neon-staging-local-restore.ps1 -Ejecutar con credenciales SOLO en PowerShell local, generar SHA256/huellas y restaurar a base efímera local. Exigir salida F268_RESULTADO=BACKUP_Y_RESTORE_VERIFICADOS, inventario y huellas 39/39, no solo preflight.
2. Confirmar por configuración de Render Production (sin revelar URI/secreto) el Neon Production ID, rama y base exacta; comparar historiales EF, esquema y datos necesarios. NO confundir STAGING con Production.
3. Preparar respaldo **reciente de Neon Production** y ensayar restauración a destino totalmente aislado. Snapshot viejo, copia de otra rama o backup de STAGING no cumplen esta condición.
4. Verificar plan de rollback **versionado** de Render, Vercel y Neon: capturar configuración de despliegue (nombres, destinos, referencias a secretos; guardar valores cifrados fuera de Git) y sus SHAs. Probar restore en STAGING; documentar por qué un rollback de código no necesariamente revierte migraciones irreversibles.
5. Completar pruebas de reservas 1:N, compensación, recuperación de resultado incierto y protección contra doble cobro, falla entre persistencias, con pruebas de corte de proceso/red en PostgreSQL aislado. No activar proveedor de pagos real para demostrarlo.
6. Verificar acceso frontend real Vercel Preview con Admin/Vendedor/Comprador, acceso correcto y 403 ajenos, y recorridos OMS B2B/B2C disponibles sin transacciones externas. CI en navegador localhost no equivale al despliegue real.
7. Revisar de forma explícita los defectos bloqueantes todavía consignados en Issue #65, y separar mejoras comerciales de fin de semana sin dar por completados requisitos faltantes.

## 3. Riesgo de promoción automática
- NO fusionar mientras Vercel Production apunte a Render Production de código antiguo.
- El push a main puede activar Vercel Production de forma automática, pero Render Production sigue una rama diferente. La API vieja podría no soportar el frontend nuevo.
- Antes de fusionar hay que establecer una **barrera de despliegue comprobada** en Vercel/Render: congela asignación de dominios de Production hasta que API, schema y frontend estén compatibles, o usar estrategia de despliegue versionada validada por el operador. Requiere autorización específica y prueba de reversión.
- Comparar el hotfix 77e9a16 de Render Production con main y candidato. No reemplazarlo sin confirmar su funcionalidad.
- Las migraciones deben aplicarse en orden explícito, con backup actual, SQL revisado, preflight de duplicados, plan de abortar y verificación de compatibilidad hacia atrás.
- Mantener deshabilitados los pagos reales salvo autorización y pasarela certificada.

## 4. Secuencia de release D/E, una vez superadas las puertas
A. Congelar/capturar baseline de prod: deployment ids, API/frontend SHA, targets y referencias cifradas de config, Neon branch+DB y hashes de respaldo.
B. Ensayar migraciones y restore en copia aislada, ejecutar la CI en candidato idéntico, guardar evidencia.
C. Coordinar backend+frontend para evitar versiones cruzadas. Preparar Render Production para seguir main mediante promoción controlada, sin perder hotfixes. Asegurar Vercel no promoverá frontend prematuramente.
D. Revalidar SHA HEAD y base de PR #60. Solicitar y registrar autorización explícita del MERGE **después** de gates C aprobados; entonces marcar PR Ready y ejecutar merge mediante GitHub.
E. Verificar main, Render, Vercel en los SHAs esperados, y Neon migrado sin inconsistencias; monitorizar HTTP health/ready, 401/403, roles y OMS. Si falla, contener tráfico, revertir deploy seguro y recuperar DB solo con plan y punto de recuperación verificado.
F. Conservar staging aislado y respaldos hasta certificado E, luego evaluar recursos obsoletos con listado de dependencias antes de eliminar.

## 5. Funcionalidad comercial pospuesta
- Portal B2B cliente, precios especiales en UI, checkout invitado, banners, reseñas y rediseño del marketplace quedan planificados para después de consolidar main.
- Esto NO habilita marcar como completado el Módulo C académico ni quitar guardas de reserva, conciliación, autenticación y pagos.
- Cerrar C técnico significa **GO seguro para release**, no finalizar todas las mejoras comerciales.

## 6. Dictamen de este corte
NO-GO actual para merge y promoción Production: F268/restauración de 39 tablas sin evidencia ejecutada, identidad+backup Neon Production no certificada, falta rollback configurado y coordinación de despliegues. La buena CI (766/766) y los índices WMS son necesarias, pero no suficientes.
Registro: Issue #65; PR #60 permanece DRAFT.
