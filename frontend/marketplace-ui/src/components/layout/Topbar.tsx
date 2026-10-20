/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState, useRef } from 'react';
import { useLocation, Link } from 'react-router-dom';
import { Bell, Search, Menu, Package, AlertTriangle, AlertCircle, CheckCircle2, ChevronRight } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';

const routeTitles: Record<string, { label: string; sub: string }> = {
  '/dashboard':    { label: 'Dashboard KPIs', sub: 'Indicadores de desempeño logístico' },
  '/ordenes':      { label: 'Órdenes (OMS)',   sub: 'Gestión de ciclo de vida de órdenes' },
  '/wms':          { label: 'WMS — Bodega',    sub: 'Warehouse Management System' },
  '/fulfillment':  { label: 'Fulfillment',     sub: 'Picking, Packing y Despacho' },
  '/rma':          { label: 'RMA / Devoluciones', sub: 'Gestión de devoluciones y reembolsos' },
  '/compras':      { label: 'Compras / 3-Way Match', sub: 'Procurement y cotejo tridireccional' },
  '/contabilidad': { label: 'Contabilidad',    sub: 'Libro diario y balance general' },
  '/liquidaciones':{ label: 'Liquidaciones',   sub: 'Pagos y splits de comisión' },
  '/productos':    { label: 'Productos',        sub: 'Catálogo de productos y stock' },
  '/checkout':     { label: 'Catálogo de Compra', sub: 'Selección y checkout de productos' },
  '/sin-acceso':   { label: 'Acceso Restringido', sub: 'Permisos insuficientes' },
};

interface NotificationItem {
  id: string;
  type: 'danger' | 'warning' | 'info' | 'success';
  title: string;
  subtitle: string;
  time: string;
  link: string;
}

const DEMO_NOTIFICATIONS: NotificationItem[] = [
  {
    id: 'n1',
    type: 'danger',
    title: 'Alerta Stock Crítico: 12 SKUs bajo mínimo',
    subtitle: 'El inventario físico ha cruzado el umbral de seguridad.',
    time: 'Hace 5 min',
    link: '/productos',
  },
  {
    id: 'n2',
    type: 'warning',
    title: 'Pasillo B WMS al 92% de capacidad',
    subtitle: 'Considere redistribución de carga hacia Pasillo D.',
    time: 'Hace 24 min',
    link: '/wms',
  },
  {
    id: 'n3',
    type: 'warning',
    title: '3 Órdenes de Compra pendientes de 3-Way Match',
    subtitle: 'Facturas recibidas requieren conciliación tridireccional.',
    time: 'Hace 1 hora',
    link: '/compras',
  },
  {
    id: 'n4',
    type: 'info',
    title: 'Liquidación periódica programada',
    subtitle: 'Próxima dispersión de saldos de vendedores externos en 24h.',
    time: 'Hace 2 horas',
    link: '/liquidaciones',
  },
];

interface TopbarProps {
  onMenuToggle: () => void;
}

export default function Topbar({ onMenuToggle }: TopbarProps) {
  const location = useLocation();
  const { usuario } = useAuth();
  const [apiOnline, setApiOnline] = useState<boolean | null>(null);
  const [showNotifications, setShowNotifications] = useState(false);
  const notifRef = useRef<HTMLDivElement>(null);

  const route = routeTitles[location.pathname] ?? { label: 'ERP', sub: 'MarketplaceERP' };

  // Close notifications on outside click
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (notifRef.current && !notifRef.current.contains(event.target as Node)) {
        setShowNotifications(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Ping API health
  useEffect(() => {
    const apiBase = import.meta.env.VITE_API_URL ?? '';
    const healthUrl = apiBase ? `${apiBase}/health` : '/health';
    fetch(healthUrl, { signal: AbortSignal.timeout(2500) })
      .then(res => {
        setApiOnline(res.ok);
      })
      .catch(() => {
        setApiOnline(false);
      });
  }, [location.pathname]);

  return (
    <header className="topbar">
      {/* Hamburger — visible only on mobile/tablet */}
      <button
        className="icon-btn topbar-menu-btn"
        onClick={onMenuToggle}
        title="Abrir menú"
        aria-label="Abrir menú de navegación"
      >
        <Menu size={16} />
      </button>

      {/* Breadcrumb */}
      <div className="topbar-breadcrumb">
        <span className="topbar-brand">LOGIMKT ERP</span>
        <span style={{ color: 'var(--border)' }}>/</span>
        <span className="current">{route.label}</span>
      </div>

      <div className="topbar-spacer" />

      {/* Actions */}
      <div className="topbar-actions">
        {/* API Status */}
        {apiOnline !== null && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 5, fontSize: 11, color: 'var(--text-muted)' }} className="api-status-label">
            <span className={`api-dot ${apiOnline ? 'online' : 'offline'}`} />
            <span>{apiOnline ? 'API online' : 'API offline'}</span>
          </div>
        )}

        <button className="icon-btn" title="Buscar">
          <Search size={14} />
        </button>

        {/* Notifications Button & Dropdown */}
        <div style={{ position: 'relative' }} ref={notifRef}>
          <button
            className={`icon-btn ${showNotifications ? 'active' : ''}`}
            onClick={() => setShowNotifications(prev => !prev)}
            title="Centro de Notificaciones"
            aria-label="Notificaciones del sistema"
            style={{ position: 'relative' }}
          >
            <Bell size={14} />
            <span className="notif-badge-dot" />
          </button>

          {showNotifications && (
            <div className="notifications-dropdown">
              <div className="notif-header">
                <div className="notif-title">
                  <span>Notificaciones</span>
                  <span className="notif-badge-pill">{DEMO_NOTIFICATIONS.length}</span>
                </div>
                <span style={{ fontSize: 11, color: 'var(--accent)', fontWeight: 600, cursor: 'pointer' }}>
                  Marcar leídas
                </span>
              </div>

              <div className="notif-list">
                {DEMO_NOTIFICATIONS.map(n => (
                  <Link
                    key={n.id}
                    to={n.link}
                    className="notif-item"
                    onClick={() => setShowNotifications(false)}
                  >
                    <div
                      className="notif-item-icon"
                      style={{
                        background:
                          n.type === 'danger'
                            ? 'var(--red-bg)'
                            : n.type === 'warning'
                            ? 'var(--yellow-bg)'
                            : 'var(--blue-bg)',
                        color:
                          n.type === 'danger'
                            ? 'var(--red)'
                            : n.type === 'warning'
                            ? 'var(--yellow)'
                            : 'var(--blue)',
                      }}
                    >
                      {n.type === 'danger' && <AlertCircle size={16} />}
                      {n.type === 'warning' && <AlertTriangle size={16} />}
                      {n.type === 'info' && <Package size={16} />}
                      {n.type === 'success' && <CheckCircle2 size={16} />}
                    </div>
                    <div className="notif-item-content">
                      <div className="notif-item-title">{n.title}</div>
                      <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 2 }}>
                        {n.subtitle}
                      </div>
                      <div className="notif-item-time">{n.time}</div>
                    </div>
                    <ChevronRight size={14} style={{ color: 'var(--text-muted)', flexShrink: 0, marginTop: 4 }} />
                  </Link>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Avatar */}
        <div className="avatar" title={usuario?.nombre}>
          {usuario?.nombre?.charAt(0)?.toUpperCase() ?? 'U'}
        </div>
      </div>
    </header>
  );
}
