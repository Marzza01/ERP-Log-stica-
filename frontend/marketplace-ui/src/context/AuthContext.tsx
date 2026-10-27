/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { createContext, useContext, useState, useCallback, type ReactNode } from 'react';
import type { Usuario } from '../types';
import { authApi } from '../api/auth';

interface AuthContextType {
  usuario: Usuario | null;
  token: string | null;
  isAuthenticated: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('erp_token'));

  const [usuario, setUsuario] = useState<Usuario | null>(() => {
    const stored = localStorage.getItem('erp_usuario');
    if (!stored) return null;
    try {
      return JSON.parse(stored) as Usuario;
    } catch {
      localStorage.removeItem('erp_usuario');
      localStorage.removeItem('erp_token');
      return null;
    }
  });

  const login = useCallback(async (email: string, password: string) => {
    const res = await authApi.login({ email, password });
    localStorage.setItem('erp_token', res.token);
    const u: Usuario = { nombre: res.usuario, email, rol: res.rol };
    localStorage.setItem('erp_usuario', JSON.stringify(u));
    setToken(res.token);
    setUsuario(u);
  }, []);

  const logout = useCallback(() => {
    localStorage.removeItem('erp_token');
    localStorage.removeItem('erp_usuario');
    setToken(null);
    setUsuario(null);
  }, []);

  return (
    <AuthContext.Provider value={{ usuario, token, isAuthenticated: !!token, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
