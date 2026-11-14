# P1 · Aceptación autenticada read-only contra Render/Neon staging

El script `scripts/p1-staging-auth-readonly.mjs` ejecuta HTTP real y
**no escribe** órdenes, facturas, inventario, asientos ni liquidaciones.

Valida salud/readiness, acceso anónimo 401, login Admin real 200, consultas
autorizadas de compras/catálogo/órdenes y rechazo 401 de JWT adulterado.
Imprime exclusivamente `P1_AUTH_EVIDENCE={...}`; nunca imprime JWT ni
contraseñas. Requiere conexión y credenciales legítimas del operador.

## Preparación local (Windows PowerShell 7)

Usar worktree independiente actualizado con
`origin/staging/catalog-500-neon-test`. **No resetear el repositorio original**
ni modificar `MarketplaceERP-P0` mientras tenga cambios locales.

En una carpeta que contenga los scripts de la última rama de staging:

```powershell
$env:P1_ADMIN_EMAIL = "admin.evaluacion@erp.example"
$segura = Read-Host "Password Admin de staging (no se mostrará)" -AsSecureString
$env:P1_ADMIN_PASSWORD = [System.Net.NetworkCredential]::new("", $segura).Password

try {
    node .\scripts\p1-staging-auth-readonly.mjs
}
finally {
    Remove-Item Env:P1_ADMIN_EMAIL,Env:P1_ADMIN_PASSWORD,Env:P1_ADMIN_TOKEN -ErrorAction SilentlyContinue
    Remove-Variable segura -ErrorAction SilentlyContinue
}
```

Si las credenciales no están disponibles, **no** guardarlas en GitHub Secrets,
issues, prompts, archivos, logs ni mensajes; entrar manualmente en la terminal
local con `Read-Host -AsSecureString`. No adivinar contraseñas ni forzar login
con múltiples intentos por el rate limiter.

El resultado `completed:true` demuestra la aceptación HTTP autenticada de
los endpoints muestreados, **no** E2E visual de la Preview protegida de Vercel.
La E2E visual real requiere el navegador del operador con autorización local
de Vercel o un token de acceso efímero permitido por Deployment Protection.
No deshabilitar la protección del deployment para completar esa prueba.

**Criterio de cierre del Issue #45:** adjuntar únicamente el JSON de evidencia
(sin tokens ni secretos), el resultado de Chromium y las pruebas de migración.
No cerrar si no se verificó el flujo Preview→Render con una cuenta legítima
o si faltan requisitos históricos explícitos del issue.


## E2E visual real en Vercel Preview (protegido)

El archivo `frontend/marketplace-ui/tests/e2e-staging-preview.mjs`
ejecuta un Chromium real contra **Vercel Preview de la rama staging**,
atraviesa su proxy hacia Render staging y valida una sesión Admin legítima
con consultas de solo lectura. Produce exclusivamente
`P1_PREVIEW_EVIDENCE={...}` y ningún secreto.

**Requisitos en el equipo del operador**:
- PowerShell 7, Node.js 24, Vercel CLI ya autenticado como propietario del proyecto,
  Vercel Preview y permisos Trusted Sources apropiados para desarrollo→Preview.
- El proyecto local de frontend vinculado al proyecto Vercel
  `erp-log-stica` (equipo `2-cm2`). Evitar vínculos con otro proyecto.
- `npm ci`, luego `npm install --no-save --no-package-lock playwright@1.55.0`,
  y `npx playwright install chromium` en la carpeta del frontend.
- No habilitar Deployment Protection bypass, ni utilizar Production.

Ejemplo desde `frontend/marketplace-ui` de un **worktree P1 aislado**
sin cambiar el checkout original:

```powershell
$env:P1_STAGING_PREVIEW_URL = "https://<preview-exacto-de-staging>.vercel.app"
$env:P1_ADMIN_EMAIL = "admin.evaluacion@erp.example"
$segura = Read-Host "Password de Admin staging" -AsSecureString
$env:P1_ADMIN_PASSWORD = [System.Net.NetworkCredential]::new("", $segura).Password
try {
    # Ejecutar en un proyecto Vercel vinculado, para obtener la credencial
    # temporal de Trusted Sources sin imprimirla.
    vc env run -- node tests/e2e-staging-preview.mjs
}
finally {
    Remove-Item Env:P1_ADMIN_EMAIL,Env:P1_ADMIN_PASSWORD,Env:P1_STAGING_PREVIEW_URL -ErrorAction SilentlyContinue
    Remove-Variable segura -ErrorAction SilentlyContinue
}
```

**Sustituir el host de ejemplo por el dominio exacto del deployment Preview
de staging**. No sirve la URL de Production ni un alias externo. El script
rechaza otros hostnames y solo inicia sesión, realiza GET y cierra sesión.
Nunca compra, siembra catálogo, crea órdenes, registra pagos ni modifica DB.

Si `vc env run` no proporciona `VERCEL_OIDC_TOKEN`, revisar que el
directorio esté vinculado al proyecto correcto y los permisos de Trusted
Sources en Vercel; no pegar secretos en ChatGPT, no hacer público el Preview
ni saltarse Deployment Protection.

La aceptación E2E real está completa solo con
`P1_PREVIEW_EVIDENCE.completed=true` y todos los estados esperados. Las
pruebas de CI con API simulada/local son complementarias pero no sustituyen
esa ejecución. El navegador tampoco puede demostrar las rutas reales de
Vendedor/Comprador en staging si no existen cuentas legítimas de esos roles:
esos casos se cubren con la suite aislada y deben identificarse como tales.
