/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { createContext, useContext, useState, useCallback } from 'react';
import { CheckCircle2, AlertTriangle, AlertCircle, Info, X } from 'lucide-react';

export type ToastType = 'success' | 'error' | 'warning' | 'info';

export interface ToastItem {
  id: string;
  type: ToastType;
  title: string;
  message?: string;
  duration?: number;
}

interface ToastContextValue {
  showToast: (type: ToastType, title: string, message?: string, duration?: number) => void;
  success: (title: string, message?: string, duration?: number) => void;
  error: (title: string, message?: string, duration?: number) => void;
  warning: (title: string, message?: string, duration?: number) => void;
  info: (title: string, message?: string, duration?: number) => void;
  removeToast: (id: string) => void;
}

const ToastContext = createContext<ToastContextValue | undefined>(undefined);

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);

  const removeToast = useCallback((id: string) => {
    setToasts(prev => prev.filter(t => t.id !== id));
  }, []);

  const showToast = useCallback((type: ToastType, title: string, message?: string, duration = 4000) => {
    const id = Math.random().toString(36).substring(2, 9);
    setToasts(prev => [...prev.slice(-4), { id, type, title, message, duration }]); // Max 5 toasts
    if (duration > 0) {
      setTimeout(() => {
        removeToast(id);
      }, duration);
    }
  }, [removeToast]);

  const success = useCallback((title: string, message?: string, duration?: number) => showToast('success', title, message, duration), [showToast]);
  const error = useCallback((title: string, message?: string, duration?: number) => showToast('error', title, message, duration), [showToast]);
  const warning = useCallback((title: string, message?: string, duration?: number) => showToast('warning', title, message, duration), [showToast]);
  const info = useCallback((title: string, message?: string, duration?: number) => showToast('info', title, message, duration), [showToast]);

  return (
    <ToastContext.Provider value={{ showToast, success, error, warning, info, removeToast }}>
      {children}
      {/* Toast Container */}
      <div className="toast-container" role="region" aria-label="Notificaciones" aria-live="polite">
        {toasts.map(toast => (
          <div key={toast.id} className={`toast-card toast-${toast.type} animate-toast-in`} role="alert">
            <div className="toast-icon">
              {toast.type === 'success' && <CheckCircle2 size={18} />}
              {toast.type === 'error' && <AlertCircle size={18} />}
              {toast.type === 'warning' && <AlertTriangle size={18} />}
              {toast.type === 'info' && <Info size={18} />}
            </div>
            <div className="toast-content">
              <div className="toast-title">{toast.title}</div>
              {toast.message && <div className="toast-message">{toast.message}</div>}
            </div>
            <button
              onClick={() => removeToast(toast.id)}
              className="toast-close"
              aria-label="Cerrar notificación"
            >
              <X size={14} />
            </button>
            {toast.duration && toast.duration > 0 && (
              <div
                className="toast-progress"
                style={{ animationDuration: `${toast.duration}ms` }}
              />
            )}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast debe ser utilizado dentro de un ToastProvider');
  }
  return context;
}
