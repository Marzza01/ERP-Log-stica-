# P1 · Decisiones de seguridad para aceptación de staging

**Alcance:** staging de LOGIMKT; este documento no autoriza producción ni pagos reales.
**Fecha:** 2026-11-07.

## 1. JWT en el navegador: situación actual y decisión

Hoy el frontend guarda `erp_token`, `erp_usuario` y `erp_expires_at`
en `localStorage`. Esto permite recargas y recuperación tras reiniciar el
navegador, pero **un XSS ejecutado en el mismo origen puede leer el JWT**.
La verificación de `exp` desde JavaScript es solo una protección de interfaz;
el servidor ASP.NET debe validar firma, audiencia, emisor y expiración en
**cada petición**. Nunca considerar el rol persistido en navegador como autoridad.

**Mitigaciones implantadas P1:**
- Invalidar sesión al alcanzar la primera de `JWT.exp` o `expiresAt`,
  sin esperar la siguiente petición al servidor.
- Aplicar política Content-Security-Policy desde Vercel:
  `script-src 'self'`, `connect-src 'self'`, sin `unsafe-eval`,
  `object-src 'none'` y `frame-ancestors 'none'`.
- Prohibir suplantación de roles desde respuestas de login inconsistentes.
- API solo por el mismo origen `/api`; nunca introducir JWT ni credenciales en
  variables Vite públicas, logs, issues o repositorios.
- Aceptación en Chromium con red local interceptada, más pruebas HTTP
  de autorización separadas.

**Riesgo residual / decisión para producción:** migrar a sesión gestionada
por backend mediante cookies `HttpOnly; Secure; SameSite` y defensa CSRF
apropiada, revisar rotación/revocación y diseñar BFF o endpoints de sesión.
No cambiar a cookie sin protección CSRF. CSP reduce superficie, **no elimina
XSS** ni el riesgo del `localStorage`. Mantener JWT de corta vida en staging,
sin tokens de uso real ni datos sensibles hasta cerrar esta deuda.

## 2. Login y rate limit

El API limita intentos de login a 5/minuto por IP con
`Microsoft.AspNetCore.RateLimiting`. En un único proceso de staging es una
barrera básica. **En varias réplicas no es un límite distribuido**, y
NAT/proxies pueden agrupar usuarios detrás de una IP.

**Decisión:** no escalar a varias réplicas ni declarar protección
antibruteforce de producción hasta implementar contador centralizado
(p. ej. Redis o WAF del proveedor), política por cuenta/IP, bloqueo temporal
seguro y métricas. Conservar mensajes genéricos, no exponer si existe una cuenta.
No confiar en `X-Forwarded-For` arbitrario.

## 3. Pagos, liquidaciones y conciliación

En staging `Payments:Provider=Disabled`. Las liquidaciones y asientos
existentes son registros internos simulados, **no transferencias bancarias**.
No habilitar pagos ni operar sobre proveedores reales sin gateway autorizado,
clave idempotente, resultado durable, conciliación, reembolso idempotente y
pruebas sobre PostgreSQL desechable con datos históricos restaurados.

## 4. Puertas de aceptación

P1 de staging puede registrar como completados: contrato UI de sesión,
rutas por rol, pruebas de navegador con API mock, migraciones financieras con
filas antiguas sintéticas, CI y despliegue de Render/Vercel staging.

**No aceptar como comprobado sin evidencia específica:**
- navegador real contra Preview de Vercel + API Render con un JWT legítimo de staging;
- migración de datos reales de un cliente si esa copia no está disponible;
- efectividad del límite de login entre múltiples instancias;
- pago bancario confirmado, preparado para producción.

Los requisitos anteriores permanecen marcados claramente como pendientes
en el Issue #45 y deben ser verificados o reclasificados explícitamente.
