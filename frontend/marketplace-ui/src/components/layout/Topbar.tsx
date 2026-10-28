/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useCallback, useEffect, useState, useRef } from 'react';
import { useLocation, Link } from 'react-router-dom';
import { Bell, Menu, AlertTriangle, AlertCircle, CheckCircle2, ChevronRight } from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { alertasApi, type AlertaInventario } from '../../api/alertas';

const routeTitles: Record<string, { label: string; sub: string }> = {
  '/dashboard': { label: 'Dashboard KPIs', sub: 'Indicadores de desempeño logístico' },
  '/ordenes': { label: 'Órdenes (OMS)', sub: 'Gestión de ciclo de vida de órdenes' },
  '/wms': { label: 'WMS — Bodega', sub: 'Warehouse Management System' },
  '/reabastecimiento': { label: 'Reabastecimiento', sub: 'Necesidades operativas de inventario' },
  '/proveedores': { label: 'Proveedores logísticos', sub: 'Maestro corporativo, OTIF y cobertura de compras' },
  '/ajustes': { label: 'Ajustes de inventario', sub: 'Correcciones controladas de existencias' },
  '/conteos': { label: 'Conteos cíclicos', sub: 'Verificación física y reconciliación de inventario' },
  '/alertas-inventario': { label: 'Alertas de inventario', sub: 'Riesgos operativos derivados del stock real' },
  '/bloqueos-inventario': { label: 'Bloqueos de inventario', sub: 'Stock retenido por daño o incidencia' },
  '/fulfillment': { label: 'Fulfillment', sub: 'Picking, Packing y Despacho' },
  '/rma': { label: 'RMA / Devoluciones', sub: 'Gestión de devoluciones y reembolsos' },
  '/compras': { label: 'Compras / 3-Way Match', sub: 'Procurement y cotejo tridireccional' },
  '/contabilidad': { label: 'Contabilidad', sub: 'Libro diario y balance general' },
  '/liquidaciones': { label: 'Liquidaciones', sub: 'Pagos y splits de comisión' },
  '/productos': { label: 'Productos', sub: 'Catálogo de productos y stock' },
  '/sin-acceso': { label: 'Acceso restringido', sub: 'Permisos insuficientes para esta sección' },
};

interface TopbarProps {
  onMenuToggle: () => void;
}

export default function Topbar({ onMenuToggle }: TopbarProps) {
  const location = useLocation();
  const { usuario } = useAuth();
  const puedeVerAlertasInventario = usuario?.rol === 'Admin' || usuario?.rol === 'Vendedor';
  const [apiOnline, setApiOnline] = useState<boolean | null>(null);
  const [showNotifications, setShowNotifications] = useState(false);
  const [notifications, setNotifications] = useState<AlertaInventario[]>([]);
  const [notificationCount, setNotificationCount] = useState(0);
  const [notificationsLoading, setNotificationsLoading] = useState(false);
  const [notificationsError, setNotificationsError] = useState(false);
  const notifRef = useRef<HTMLDivElement>(null);

  const route = routeTitles[location.pathname] ?? { label: 'ERP', sub: 'MarketplaceERP' };

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (notifRef.current && !notifRef.current.contains(event.target as Node)) {
        setShowNotifications(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetch('/health', { signal: controller.signal })
      .then(res => setApiOnline(res.ok))
      .catch(() => setApiOnline(false));
    return () => controller.abort();
  }, [location.pathname]);

  const cargarAlertas = useCallback(async () => {
    if (!puedeVerAlertasInventario) {
      setNotifications([]);
      setNotificationCount(0);
      setNotificationsError(false);
      setNotificationsLoading(false);
      setShowNotifications(false);
      return;
    }
    setNotificationsLoading(true);
    setNotificationsError(false);
    try {
      const data = await alertasApi.listar();
      const openItems = data.items.filter(item => item.estado === 'OPEN');
      setNotificationCount(openItems.length);
      setNotifications(openItems.slice(0, 5));
    } catch {
      setNotifications([]);
      setNotificationCount(0);
      setNotificationsError(true);
    } finally {
      setNotificationsLoading(false);
    }
  }, [puedeVerAlertasInventario]);

  useEffect(() => {
    void cargarAlertas();
  }, [location.pathname, cargarAlertas]);

  return (
    <header className="topbar">
      <button
        className="icon-btn topbar-menu-btn"
        onClick={onMenuToggle}
        title="Abrir menú"
        aria-label="Abrir menú de navegación"
      >
        <Menu size={16} />
      </button>

      <div className="topbar-breadcrumb">
        <span className="topbar-brand">LOGIMKT ERP</span>
        <span style={{ color: 'var(--border)' }}>/</span>
        <span className="current">{route.label}</span>
      </div>

      <div className="topbar-spacer" />

      <div className="topbar-actions">
        {apiOnline !== null && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 5, fontSize: 11, color: 'var(--text-muted)' }} className="api-status-label">
            <span className={`api-dot ${apiOnline ? 'online' : 'offline'}`} />
            <span>{apiOnline ? 'API online' : 'API offline'}</span>
          </div>
        )}

        <div style={{ position: 'relative' }} ref={notifRef}>
          <button
            className={`icon-btn ${showNotifications ? 'active' : ''}`}
            onClick={() => setShowNotifications(prev => !prev)}
            title="Alertas operativas"
            aria-label="Alertas operativas de inventario"
            style={{ position: 'relative' }}
          >
            <Bell size={14} />
            {notificationCount > 0 && <span className="notif-badge-dot" />}
          </button>

          {showNotifications && (
            <div className="notifications-dropdown">
              <div className="notif-header">
                <div className="notif-title">
                  <span>Alertas operativas</span>
                  <span className="notif-badge-pill">{notificationCount}</span>
                </div>
                <Link
                  to="/alertas-inventario"
                  onClick={() => setShowNotifications(false)}
                  style={{ fontSize: 11, color: 'var(--accent)', fontWeight: 600, textDecoration: 'none' }}
                >
                  Ver todas
                </Link>
              </div>

              <div className="notif-list">
                {notificationsLoading ? (
                  <div style={{ padding: '18px 16px', fontSize: 12, color: 'var(--text-muted)' }}>Cargando alertas...</div>
                ) : notificationsError ? (
                  <div style={{ padding: '22px 16px', textAlign: 'center' }}>
                    <AlertCircle size={20} style={{ color: 'var(--red)', marginBottom: 6 }} />
                    <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-primary)' }}>No se pudieron cargar las alertas</div>
                    <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 3 }}>Revisa la conexión con la API.</div>
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      onClick={() => void cargarAlertas()}
                      disabled={notificationsLoading}
                      style={{ marginTop: 10 }}
                    >
                      Reintentar
                    </button>
                  </div>
                ) : notifications.length === 0 ? (
                  <div style={{ padding: '22px 16px', textAlign: 'center' }}>
                    <CheckCircle2 size={20} style={{ color: 'var(--green)', marginBottom: 6 }} />
                    <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-primary)' }}>Sin alertas pendientes</div>
                    <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 3 }}>El inventario no reporta incidencias activas.</div>
                  </div>
                ) : notifications.map(n => (
                  <Link
                    key={n.alertaId}
                    to="/alertas-inventario"
                    className="notif-item"
                    onClick={() => setShowNotifications(false)}
                  >
                    <div
                      className="notif-item-icon"
                      style={{
                        background: n.prioridad === 'CRITICAL' ? 'var(--red-bg)' : 'var(--yellow-bg)',
                        color: n.prioridad === 'CRITICAL' ? 'var(--red)' : 'var(--yellow)',
                      }}
                    >
                      {n.prioridad === 'CRITICAL' ? <AlertCircle size={16} /> : <AlertTriangle size={16} />}
                    </div>
                    <div className="notif-item-content">
                      <div className="notif-item-title">{n.sku} · {n.producto}</div>
                      <div style={{ fontSize: 11.5, color: 'var(--text-secondary)', marginTop: 2 }}>{n.mensaje}</div>
                      <div className="notif-item-time">Disponible: {n.cantidadDisponible} · Reorden: {n.puntoReorden}</div>
                    </div>
                    <ChevronRight size={14} style={{ color: 'var(--text-muted)', flexShrink: 0, marginTop: 4 }} />
                  </Link>
                ))}
              </div>
            </div>
          )}
        </div>

        <div className="avatar" title={usuario?.nombre}>
          {usuario?.nombre?.charAt(0)?.toUpperCase() ?? 'U'}
        </div>
      </div>
    </header>
  );
}
