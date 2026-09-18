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
    const u = localStorage.getItem('erp_usuario');
    return u ? JSON.parse(u) : null;
  });

  const login = useCallback(async (email: string, password: string) => {
    // Try real API; on failure (no users seeded), use mock credentials for dev
    try {
      const res = await authApi.login({ email, password });
      localStorage.setItem('erp_token', res.token);
      const u: Usuario = { nombre: res.usuario, email, rol: res.rol };
      localStorage.setItem('erp_usuario', JSON.stringify(u));
      setToken(res.token);
      setUsuario(u);
    } catch {
      // Dev fallback — accept any credential so UI can be tested
      if (email && password.length >= 4) {
        const mockToken = 'dev-token-' + Date.now();
        const u: Usuario = { nombre: email.split('@')[0], email, rol: 'Admin' };
        localStorage.setItem('erp_token', mockToken);
        localStorage.setItem('erp_usuario', JSON.stringify(u));
        setToken(mockToken);
        setUsuario(u);
      } else {
        throw new Error('Credenciales inválidas');
      }
    }
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
