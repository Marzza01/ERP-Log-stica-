import type { AuthResponse, Usuario } from '../types';

export interface AuthSession {
  token: string;
  usuario: Usuario;
  expiresAt: string;
}

type SessionStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

function parseJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const json = decodeURIComponent(
      atob(base64)
        .split('')
        .map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    const parsed: unknown = JSON.parse(json);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : null;
  } catch {
    return null;
  }
}

function tokenRole(payload: Record<string, unknown> | null): unknown {
  return payload?.role ?? payload?.['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
}

export function isSessionValid(token: string | null, expiresAt: string | null): boolean {
  if (!token || !expiresAt) return false;
  const responseExpiry = Date.parse(expiresAt);
  const payload = parseJwtPayload(token);
  return Number.isFinite(responseExpiry) && responseExpiry > Date.now() &&
    typeof payload?.exp === 'number' && Number.isFinite(payload.exp) &&
    payload.exp * 1000 > Date.now();
}

export function sessionFromLogin(response: AuthResponse, email: string): AuthSession {
  const payload = parseJwtPayload(response?.token);
  if (!response?.token || !response.usuario || !response.rol || !response.role ||
      response.rol !== response.role || response.rol !== tokenRole(payload) ||
      !['Admin', 'Vendedor', 'Comprador'].includes(response.rol) ||
      !isSessionValid(response.token, response.expiresAt)) {
    throw new Error('Respuesta de autenticación inválida del servidor.');
  }

  const vendedorId = payload?.vendedor_id ?? payload?.VendedorId;
  return {
    token: response.token,
    expiresAt: response.expiresAt,
    usuario: {
      nombre: response.usuario,
      email: email.trim().toLowerCase(),
      rol: response.rol,
      vendedorId: typeof vendedorId === 'string' ? vendedorId : undefined,
    },
  };
}

export function clearStoredSession(storage: SessionStorage): void {
  storage.removeItem('erp_token');
  storage.removeItem('erp_usuario');
  storage.removeItem('erp_expires_at');
}

export function readStoredSession(storage: SessionStorage): AuthSession | null {
  const token = storage.getItem('erp_token');
  const expiresAt = storage.getItem('erp_expires_at');
  const rawUser = storage.getItem('erp_usuario');
  if (token && expiresAt && rawUser && isSessionValid(token, expiresAt)) {
    try {
      const usuario: Usuario = JSON.parse(rawUser);
      const payload = parseJwtPayload(token);
      if (usuario && typeof usuario.nombre === 'string' &&
          typeof usuario.email === 'string' &&
          typeof usuario.rol === 'string' && usuario.rol === tokenRole(payload)) {
        return { token, usuario, expiresAt };
      }
    } catch { /* Invalid local state is discarded. */ }
  }
  clearStoredSession(storage);
  return null;
}

export function persistSession(storage: SessionStorage, session: AuthSession): void {
  try {
    storage.setItem('erp_token', session.token);
    storage.setItem('erp_usuario', JSON.stringify(session.usuario));
    storage.setItem('erp_expires_at', session.expiresAt);
  } catch (error) {
    clearStoredSession(storage);
    throw error;
  }
}
