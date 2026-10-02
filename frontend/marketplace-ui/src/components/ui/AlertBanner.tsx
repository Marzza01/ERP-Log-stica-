import React from 'react';
import { AlertCircle, AlertTriangle, CheckCircle2, Info, X } from 'lucide-react';

export type AlertVariant = 'danger' | 'warning' | 'success' | 'info';

interface AlertBannerProps {
  variant?: AlertVariant;
  title?: string;
  message: React.ReactNode;
  action?: {
    label: string;
    onClick: () => void;
  };
  onClose?: () => void;
  className?: string;
}

export default function AlertBanner({
  variant = 'warning',
  title,
  message,
  action,
  onClose,
  className = '',
}: AlertBannerProps) {
  const getIcon = () => {
    switch (variant) {
      case 'danger':
        return <AlertCircle size={18} className="alert-icon" />;
      case 'warning':
        return <AlertTriangle size={18} className="alert-icon" />;
      case 'success':
        return <CheckCircle2 size={18} className="alert-icon" />;
      case 'info':
      default:
        return <Info size={18} className="alert-icon" />;
    }
  };

  return (
    <div className={`alert-banner alert-${variant} ${className}`} role="alert">
      <div className="alert-icon-wrap">
        {getIcon()}
      </div>
      <div className="alert-content">
        {title && <div className="alert-title">{title}</div>}
        <div className="alert-message">{message}</div>
      </div>
      {action && (
        <button
          type="button"
          onClick={action.onClick}
          className="alert-action-btn"
        >
          {action.label}
        </button>
      )}
      {onClose && (
        <button
          type="button"
          onClick={onClose}
          className="alert-close-btn"
          aria-label="Cerrar alerta"
        >
          <X size={14} />
        </button>
      )}
    </div>
  );
}
