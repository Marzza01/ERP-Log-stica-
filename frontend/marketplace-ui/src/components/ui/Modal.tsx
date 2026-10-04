/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect } from 'react';
import { X } from 'lucide-react';

interface ModalProps {
  title: string;
  open?: boolean;
  isOpen?: boolean;
  onClose: () => void;
  children: React.ReactNode;
  footer?: React.ReactNode;
  size?: 'sm' | 'md' | 'lg';
}

export default function Modal({ title, open, isOpen, onClose, children, footer, size = 'md' }: ModalProps) {
  const isModalOpen = open ?? isOpen ?? false;

  useEffect(() => {
    if (!isModalOpen) return;
    const handler = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [isModalOpen, onClose]);

  if (!isModalOpen) return null;

  const maxW = { sm: 380, md: 520, lg: 720 }[size];

  return (
    <div className="modal-overlay" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="modal" style={{ maxWidth: maxW }}>
        <div className="modal-header">
          <span className="modal-title">{title}</span>
          <button className="icon-btn" onClick={onClose} style={{ border: 'none' }}>
            <X size={14} />
          </button>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-footer">{footer}</div>}
      </div>
    </div>
  );
}
