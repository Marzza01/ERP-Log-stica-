/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import type { Usuario } from '../types';
import { authApi } from '../api/auth';
import { setUnauthorizedHandler } from '../api/client';

interface AuthContextType {
  usuario: Usuario | null;
  token: string | null;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | null>(null);

function parseJwtPayload(token: string): any {
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
    return JSON.parse(json);
  } catch {
    return null;
  }
}

function isSessionValid(token: string | null, expiresAtStr: string | null): boolean {
  if (!token) return false;

  // Si tenemos expiresAt guardado, verificar expiración
  if (expiresAtStr) {
    const expTime = new Date(expiresAtStr).getTime();
    if (!isNaN(expTime) && Date.now() >= expTime) {
      return false;
    }
  }

  // Verificar claim exp del JWT directamente si existe
  const payload = parseJwtPayload(token);
  if (payload && typeof payload.exp === 'number') {
    if (Date.now() >= payload.exp * 1000) {
      return false;
    }
  }

  return true;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(() => {
    const t = localStorage.getItem('erp_token');
    const exp = localStorage.getItem('erp_expires_at');
    if (t && isSessionValid(t, exp)) {
      return t;
    }
    // Si la sesión no es válida o expiró, limpiar
    localStorage.removeItem('erp_token');
    localStorage.removeItem('erp_usuario');
    localStorage.removeItem('erp_expires_at');
    return null;
  });

  const [usuario, setUsuario] = useState<Usuario | null>(() => {
    const t = localStorage.getItem('erp_token');
    const u = localStorage.getItem('erp_usuario');
    const exp = localStorage.getItem('erp_expires_at');
    if (t && u && isSessionValid(t, exp)) {
      try {
        return JSON.parse(u);
      } catch {
        return null;
      }
    }
    return null;
  });

  const logout = useCallback(() => {
    localStorage.removeItem('erp_token');
    localStorage.removeItem('erp_usuario');
    localStorage.removeItem('erp_expires_at');
    setToken(null);
    setUsuario(null);
  }, []);

  // Registrar callback global para 401 desde el cliente HTTP
  useEffect(() => {
    setUnauthorizedHandler(() => {
      logout();
      if (window.location.pathname !== '/login') {
        window.location.href = '/login';
      }
    });
  }, [logout]);

  const login = useCallback(async (email: string, password: string) => {
    // Si el backend rechaza o no responde, lanza error y NO crea sesión
    const res = await authApi.login({ email, password });
    if (!res || !res.token) {
      throw new Error('Respuesta de autenticación inválida del servidor.');
    }

    const payload = parseJwtPayload(res.token);
    const roleFromJwt = payload?.role || payload?.['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
    const vendedorIdFromJwt = payload?.vendedor_id || payload?.VendedorId;

    const u: Usuario = {
      nombre: res.usuario || email.split('@')[0],
      email: email.trim().toLowerCase(),
      rol: res.rol || roleFromJwt || 'Comprador',
      vendedorId: vendedorIdFromJwt,
    };

    const expira = res.expira || (payload?.exp ? new Date(payload.exp * 1000).toISOString() : undefined);

    localStorage.setItem('erp_token', res.token);
    localStorage.setItem('erp_usuario', JSON.stringify(u));
    if (expira) {
      localStorage.setItem('erp_expires_at', expira);
    } else {
      localStorage.removeItem('erp_expires_at');
    }

    setToken(res.token);
    setUsuario(u);
  }, []);

  return (
    <AuthContext.Provider value={{ usuario, token, isAuthenticated: !!token && !!usuario, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
