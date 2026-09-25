import React, { useEffect, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { Bell, Search } from 'lucide-react';
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
};

export default function Topbar() {
  const location = useLocation();
  const { usuario } = useAuth();
  const [apiOnline, setApiOnline] = useState<boolean | null>(null);

  const route = routeTitles[location.pathname] ?? { label: 'ERP', sub: 'MarketplaceERP' };

  // Ping API health
  useEffect(() => {
    fetch('/health', { signal: AbortSignal.timeout(2500) })
      .then(res => {
        if (res.ok) setApiOnline(true);
        else throw new Error('Not ok');
      })
      .catch(() => {
        fetch('http://localhost:8080/health', { signal: AbortSignal.timeout(2500) })
          .then(res => setApiOnline(res.ok))
          .catch(() => setApiOnline(false));
      });
  }, [location.pathname]);

  return (
    <header className="topbar">
      {/* Breadcrumb */}
      <div className="topbar-breadcrumb">
        <span>LOGIMKT ERP</span>
        <span style={{ color: 'var(--border)' }}>/</span>
        <span className="current">{route.label}</span>
      </div>

      <div className="topbar-spacer" />

      {/* Actions */}
      <div className="topbar-actions">
        {/* API Status */}
        {apiOnline !== null && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 5, fontSize: 11, color: 'var(--text-muted)' }}>
            <span className={`api-dot ${apiOnline ? 'online' : 'offline'}`} />
            <span>{apiOnline ? 'API online' : 'API offline'}</span>
          </div>
        )}

        <button className="icon-btn" title="Buscar">
          <Search size={14} />
        </button>

        <button className="icon-btn" title="Notificaciones">
          <Bell size={14} />
        </button>

        {/* Avatar */}
        <div className="avatar" title={usuario?.nombre}>
          {usuario?.nombre?.charAt(0)?.toUpperCase() ?? 'U'}
        </div>
      </div>
    </header>
  );
}
