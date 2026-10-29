/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Eye, EyeOff, Loader2 } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { ApiError } from '../../api/client';

export default function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPw, setShowPw] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      await login(email, password);
      // Redirigir según el rol o al inicio
      const stored = localStorage.getItem('erp_usuario');
      if (stored) {
        try {
          const u = JSON.parse(stored);
          if (u.rol === 'Comprador') {
            navigate('/checkout');
            return;
          }
        } catch { /* ignore */ }
      }
      navigate('/dashboard');
    } catch (err: any) {
      if (err instanceof ApiError) {
        if (err.status === 401) {
          setError('Correo electrónico o contraseña incorrectos.');
        } else if (err.status === 429) {
          setError('Demasiados intentos fallidos. Has alcanzado el límite de seguridad. Espera un minuto antes de intentar de nuevo.');
        } else if (err.status === 0) {
          setError('No es posible conectar con el servidor de autenticación. Verifica tu conexión de red.');
        } else {
          setError(err.message || 'Error en la autenticación.');
        }
      } else if (err instanceof Error) {
        if (err.message.includes('Failed to fetch') || err.message.includes('NetworkError')) {
          setError('No es posible conectar con el servidor de autenticación. Verifica tu conexión de red.');
        } else {
          setError(err.message);
        }
      } else {
        setError('Error inesperado durante el inicio de sesión.');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="auth-shell">
      <div className="auth-card">
        {/* Logo */}
        <div className="auth-logo">
          <div className="sidebar-logo-icon" style={{ width: 36, height: 36 }}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none">
              <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z" stroke="white" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <polyline points="9 22 9 12 15 12 15 22" stroke="white" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
          </div>
          <div>
            <div style={{ fontSize: 16, fontWeight: 800, color: 'var(--text-primary)', letterSpacing: '-0.03em', lineHeight: 1.1 }}>LOGIMKT ERP</div>
            <div style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 500 }}>Marketplace Logistics System</div>
          </div>
        </div>

        <h1 className="auth-title">Iniciar sesión</h1>
        <p className="auth-subtitle">Accede al panel de control logístico</p>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div className="form-group">
            <label className="form-label">Correo electrónico</label>
            <input
              className="form-input"
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder="nombre@ejemplo.com"
              required
              autoComplete="email"
            />
          </div>

          <div className="form-group">
            <label className="form-label">Contraseña</label>
            <div style={{ position: 'relative' }}>
              <input
                className="form-input"
                type={showPw ? 'text' : 'password'}
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="••••••••"
                required
                autoComplete="current-password"
                style={{ paddingRight: 40 }}
              />
              <button
                type="button"
                onClick={() => setShowPw(!showPw)}
                style={{ position: 'absolute', right: 10, top: '50%', transform: 'translateY(-50%)', background: 'none', border: 'none', color: 'var(--text-muted)', cursor: 'pointer', display: 'flex' }}
              >
                {showPw ? <EyeOff size={15} /> : <Eye size={15} />}
              </button>
            </div>
          </div>

          {error && (
            <div style={{ background: 'var(--red-bg)', border: '1px solid var(--red)', borderRadius: 'var(--radius-md)', padding: '10px 14px', fontSize: 12, color: 'var(--red)' }}>
              {error}
            </div>
          )}

          <button className="btn btn-primary btn-lg" type="submit" disabled={loading} style={{ width: '100%', justifyContent: 'center', marginTop: 4 }}>
            {loading ? <><Loader2 size={15} style={{ animation: 'spin 1s linear infinite' }} /> Ingresando...</> : 'Ingresar al sistema'}
          </button>
        </form>
      </div>

      <style>{`@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }`}</style>
    </div>
  );
}
