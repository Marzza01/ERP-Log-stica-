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
