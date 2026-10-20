/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

// ─────────────────────────────────────────────────────────────────────────────
// API Client — Base HTTP client con JWT interceptor y manejo centralizado de 401/403
// ─────────────────────────────────────────────────────────────────────────────

export const API_BASE = import.meta.env.VITE_API_URL ?? '';

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

export function setUnauthorizedHandler(handler: UnauthorizedHandler) {
  onUnauthorized = handler;
}

export function setForbiddenHandler(handler: ForbiddenHandler) {
  onForbidden = handler;
}

async function request<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const token = localStorage.getItem('erp_token');

  const headers: HeadersInit = {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...options.headers,
  };

  let res: Response;
  try {
    res = await fetch(`${API_BASE}${path}`, { ...options, headers });
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
        errorMsg = parsedBody.mensaje || parsedBody.message || parsedBody.title || text;
      } catch {
        errorMsg = text || res.statusText;
      }
    } catch {
      // Ignorar error al leer body
    }

    // Interceptor 401: Unauthorized
    if (res.status === 401) {
      // Si la petición falló en el login endpoint, no disparamos logout ni redirección global
      if (!path.includes('/api/auth/login')) {
        if (onUnauthorized) {
          onUnauthorized();
        } else {
          localStorage.removeItem('erp_token');
          localStorage.removeItem('erp_usuario');
          localStorage.removeItem('erp_expires_at');
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

  // 204 No Content
  if (res.status === 204) return undefined as unknown as T;

  return res.json() as Promise<T>;
}

export const apiClient = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body !== undefined ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body !== undefined ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' }),
};
