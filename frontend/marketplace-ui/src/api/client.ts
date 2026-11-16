/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

// ─────────────────────────────────────────────────────────────────────────────
// API Client — Base HTTP client con JWT interceptor y manejo centralizado de 401/403
// ─────────────────────────────────────────────────────────────────────────────

// Vercel y el proxy de Vite reciben /api desde el mismo origen del navegador.
export const API_BASE = '';

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public data?: any
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

type UnauthorizedHandler = () => void;
type ForbiddenHandler = (message: string) => void;

let onUnauthorized: UnauthorizedHandler | null = null;
let onForbidden: ForbiddenHandler | null = null;

// F354: explicit BFF mode is enabled only by AuthProvider in a separately
// configured Preview build. Never retain a JWT in browser-readable storage.
let cookieBffMode = false;
let cookieCsrfToken: string | null = null;

export function configureCookieBffMode(enabled: boolean): void {
  cookieBffMode = enabled;
  if (!enabled) cookieCsrfToken = null;
}

export function setCookieBffCsrfToken(token: string | null): void {
  cookieCsrfToken = token;
}

export function setUnauthorizedHandler(handler: UnauthorizedHandler) {
  onUnauthorized = handler;
}

export function setForbiddenHandler(handler: ForbiddenHandler) {
  onForbidden = handler;
}

async function request<T>(
  path: string,
  options: RequestInit = {},
  ignoreSuccessBody = false
): Promise<T> {
  const token = cookieBffMode ? null : localStorage.getItem('erp_token');
  const method = (options.method ?? 'GET').toUpperCase();
  const stateChange = !['GET', 'HEAD'].includes(method);

  const headers: HeadersInit = {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(cookieBffMode && stateChange && cookieCsrfToken
      ? { 'X-ERP-CSRF': cookieCsrfToken } : {}),
    ...options.headers,
  };

  let res: Response;
  try {
    res = await fetch(`${API_BASE}${path}`, {
      ...options, headers,
      credentials: cookieBffMode ? 'same-origin' : options.credentials,
    });
  } catch (netErr: any) {
    throw new ApiError(0, 'No se pudo conectar con el servidor. Verifica tu conexión a la red o el estado del backend.');
  }

  if (!res.ok) {
    let parsedBody: any = null;
    let errorMsg = res.statusText;

    try {
      const text = await res.text();
      try {
        parsedBody = JSON.parse(text);
        const serverMessage = parsedBody && typeof parsedBody === 'object'
          ? (parsedBody.mensaje || parsedBody.message || parsedBody.title) : null;
        errorMsg = typeof serverMessage === 'string' && serverMessage.trim()
          ? serverMessage.slice(0, 300) : `Error HTTP ${res.status}`;
      } catch {
        // No presentar páginas HTML de proxy (ni errores internos con trazas) como texto de usuario.
        errorMsg = res.status >= 500
          ? 'El servicio está temporalmente no disponible. Inténtalo de nuevo.'
          : `No se pudo completar la solicitud (HTTP ${res.status}).`;
      }
    } catch {
      // Ignorar error al leer body
    }

    // Interceptor 401: Unauthorized
    if (res.status === 401) {
      // Si la petición falló en el login endpoint, no disparamos logout ni redirección global
      if (path !== '/api/auth/login') {
        if (onUnauthorized) {
          onUnauthorized();
        } else {
          if (cookieBffMode) {
            cookieCsrfToken = null;
          } else {
            localStorage.removeItem('erp_token');
            localStorage.removeItem('erp_usuario');
            localStorage.removeItem('erp_expires_at');
          }
          if (window.location.pathname !== '/login') {
            window.location.href = '/login';
          }
        }
      }
      throw new ApiError(401, errorMsg || 'Credenciales inválidas o sesión expirada.', parsedBody);
    }

    // Interceptor 403: Forbidden
    if (res.status === 403) {
      const msg = 'Acceso denegado: No cuentas con los permisos requeridos para realizar esta acción.';
      if (onForbidden) {
        onForbidden(msg);
      }
      throw new ApiError(403, errorMsg || msg, parsedBody);
    }

    // Interceptor 429: Rate Limit
    if (res.status === 429) {
      throw new ApiError(429, 'Demasiados intentos. Has alcanzado el límite de peticiones. Por favor, espera un minuto antes de reintentar.', parsedBody);
    }

    throw new ApiError(res.status, errorMsg || `Error en la solicitud (${res.status})`, parsedBody);
  }

  // La operación puede haber sido persistida aunque la respuesta 2xx no tenga JSON.
  // Para comandos cuyo resultado no se utiliza, validar el HTTP 2xx sin leer el cuerpo.
  if (ignoreSuccessBody || res.status === 204 || res.status === 205 || method === 'HEAD')
    return undefined as T;

  const body = await res.text();
  if (!body.trim()) {
    if (stateChange) return undefined as T; // POST/PUT/DELETE 2xx sin contenido válido.
    throw new ApiError(res.status, 'El servidor respondió sin datos para esta consulta.');
  }
  try {
    return JSON.parse(body) as T;
  } catch {
    throw new ApiError(res.status, 'El servidor devolvió una respuesta JSON inválida.');
  }
}

/** Authenticated binary download that uses the same BFF/legacy session mode as JSON APIs. */
export async function getAuthenticatedBlob(path: string): Promise<Blob> {
  const token = cookieBffMode ? null : localStorage.getItem('erp_token');
  const response = await fetch(`${API_BASE}${path}`, {
    credentials: 'same-origin',
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (response.status === 401) {
    onUnauthorized?.();
    throw new ApiError(401, 'Inicia sesión nuevamente para acceder al PDF.');
  }
  if (response.status === 403) {
    onForbidden?.('No tienes permisos para acceder a este PDF.');
    throw new ApiError(403, 'No tienes permisos para acceder al PDF.');
  }
  if (!response.ok) throw new ApiError(response.status, 'No fue posible recuperar el PDF de la orden.');
  if (!response.headers.get('content-type')?.toLowerCase().includes('application/pdf'))
    throw new ApiError(502, 'La respuesta del servidor no es un documento PDF.');
  return response.blob();
}

export const apiClient = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown, headers?: HeadersInit) =>
    request<T>(path, { method: 'POST', body: body !== undefined ? JSON.stringify(body) : undefined, headers }),
  // Comandos que solo requieren saber si el servidor aceptó la operación.
  postVoid: (path: string, body?: unknown, headers?: HeadersInit) =>
    request<void>(path, { method: 'POST', body: body !== undefined ? JSON.stringify(body) : undefined, headers }, true),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body !== undefined ? JSON.stringify(body) : undefined }),
  putVoid: (path: string, body?: unknown) =>
    request<void>(path, { method: 'PUT', body: body !== undefined ? JSON.stringify(body) : undefined }, true),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
};
