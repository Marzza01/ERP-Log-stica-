/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react';
import type { Usuario } from '../types';
import { authApi } from '../api/auth';
import { setUnauthorizedHandler } from '../api/client';
import { clearStoredSession, persistSession, readStoredSession, sessionFromLogin, sessionRemainingMs, type AuthSession } from './authSession';

interface AuthContextType {
  usuario: Usuario | null;
  token: string | null;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthSession | null>(() => readStoredSession(localStorage));
  const token = session?.token ?? null;
  const usuario = session?.usuario ?? null;

  const logout = useCallback(() => {
    clearStoredSession(localStorage);
    setSession(null);
  }, []);

  // Invalidate an idle browser session when its token expires, without waiting
  // for a 401 response from the next HTTP request.
  useEffect(() => {
    if (!session) return;
    const remainingMs = sessionRemainingMs(session);
    if (remainingMs <= 0) {
      logout();
      return;
    }
    const timeout = window.setTimeout(logout, Math.min(remainingMs, 2_147_483_647));
    return () => window.clearTimeout(timeout);
  }, [session, logout]);

  // Registrar callback global para 401 desde el cliente HTTP
  useEffect(() => {
    setUnauthorizedHandler(() => {
      // ProtectedRoute performs the redirect after logout; a second imperative
      // window.location navigation races with React Router and aborts tests.
      logout();
    });
  }, [logout]);

  const login = useCallback(async (email: string, password: string) => {
    // Si el backend rechaza o no responde, lanza error y NO crea sesión
    const res = await authApi.login({ email, password });
    const nextSession = sessionFromLogin(res, email);
    persistSession(localStorage, nextSession);
    setSession(nextSession);
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
