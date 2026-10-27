/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import {
  LayoutDashboard, ShoppingCart, Warehouse, Truck,
  RotateCcw, ShoppingBag, BookOpen, Wallet,
  Package, ChevronLeft, ChevronRight, LogOut, X, RefreshCw, Building2, ClipboardCheck, ClipboardList,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';

interface NavItem {
  to: string;
  icon: React.ReactNode;
  label: string;
  shortLabel?: string;
  badge?: {
    count: number | string;
    variant: 'danger' | 'warning' | 'info';
  };
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

const navGroups: NavGroup[] = [
  {
    label: 'Principal',
    items: [
      { to: '/dashboard', icon: <LayoutDashboard size={16} />, label: 'Dashboard',     shortLabel: 'Dashboard' },
      {
        to: '/ordenes',
        icon: <ShoppingCart size={16} />,
        label: 'Órdenes',
        shortLabel: 'Órdenes',
        badge: { count: '3', variant: 'warning' },
      },
    ],
  },
  {
    label: 'Operaciones',
    items: [
      {
        to: '/wms',
        icon: <Warehouse size={16} />,
        label: 'WMS / Bodega',
        shortLabel: 'Bodega',
        badge: { count: '1', variant: 'danger' },
      },
      {
        to: '/reabastecimiento',
        icon: <RefreshCw size={16} />,
        label: 'Reabastecimiento',
        shortLabel: 'Reabast.',
      },
      {
        to: '/proveedores',
        icon: <Building2 size={16} />,
        label: 'Proveedores',
        shortLabel: 'Proveed.',
      },
      {
        to: '/ajustes',
        icon: <ClipboardCheck size={16} />,
        label: 'Ajustes de inventario',
        shortLabel: 'Ajustes',
      },
      {
        to: '/conteos',
        icon: <ClipboardList size={16} />,
        label: 'Conteos cíclicos',
        shortLabel: 'Conteos',
      },
      { to: '/fulfillment', icon: <Truck size={16} />,        label: 'Fulfillment',    shortLabel: 'Fulfillment' },
      {
        to: '/rma',
        icon: <RotateCcw size={16} />,
        label: 'Devoluciones',
        shortLabel: 'Devoluciones',
        badge: { count: '2', variant: 'warning' },
      },
      {
        to: '/compras',
        icon: <ShoppingBag size={16} />,
        label: 'Compras',
        shortLabel: 'Compras',
        badge: { count: '3', variant: 'info' },
      },
    ],
  },
  {
    label: 'Finanzas',
    items: [
      { to: '/contabilidad',  icon: <BookOpen size={16} />, label: 'Contabilidad',   shortLabel: 'Contabilidad' },
      {
        to: '/liquidaciones',
        icon: <Wallet size={16} />,
        label: 'Liquidaciones',
        shortLabel: 'Pagos',
        badge: { count: '2', variant: 'warning' },
      },
      { to: '/productos',     icon: <Package size={16} />,  label: 'Productos',      shortLabel: 'Productos' },
    ],
  },
];

interface SidebarProps {
  mobileOpen: boolean;
  onClose: () => void;
}

export default function Sidebar({ mobileOpen, onClose }: SidebarProps) {
  const [collapsed, setCollapsed] = useState(false);
  const { logout, usuario } = useAuth();
  useLocation(); // trigger re-render on route change for active state

  return (
    <>
      {/* Mobile Overlay */}
      {mobileOpen && (
        <div className="sidebar-overlay" onClick={onClose} aria-hidden="true" />
      )}

      <aside className={`sidebar${collapsed ? ' collapsed' : ''}${mobileOpen ? ' mobile-open' : ''}`}>
        {/* Logo */}
        <div className="sidebar-logo">
          <div className="sidebar-logo-icon">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none">
              <path d="M3 9l9-7 9 7v11a2 2 0 01-2 2H5a2 2 0 01-2-2z" stroke="white" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
              <polyline points="9 22 9 12 15 12 15 22" stroke="white" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
            </svg>
          </div>
          <div className="sidebar-logo-text">
            <div style={{ fontSize: 13, fontWeight: 700, letterSpacing: '-0.02em', color: 'var(--text-primary)', lineHeight: 1.2 }}>LOGIMKT</div>
            <div style={{ fontSize: 10, color: 'var(--text-muted)', fontWeight: 500 }}>ERP Marketplace</div>
          </div>
          {/* Close button on mobile */}
          <button className="sidebar-close-btn" onClick={onClose} title="Cerrar menú" aria-label="Cerrar menú">
            <X size={16} />
          </button>
        </div>

        {/* Nav */}
        <nav className="sidebar-nav">
          {navGroups.map((group) => (
            <div key={group.label}>
              <div className="sidebar-section-label">{group.label}</div>
              {group.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}
                  title={collapsed ? item.label : undefined}
                >
                  <span className="nav-icon">{item.icon}</span>
                  <span className="nav-label nav-label-full">{item.label}</span>
                  {item.shortLabel && (
                    <span className="nav-label nav-label-short">{item.shortLabel}</span>
                  )}
                  {item.badge && !collapsed && (
                    <span className={`nav-badge-pill nav-badge-${item.badge.variant}`}>
                      {item.badge.count}
                    </span>
                  )}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        {/* Footer */}
        <div className="sidebar-footer">
          {!collapsed && usuario && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '8px 8px 10px', borderBottom: '1px solid var(--border)', marginBottom: 8 }}>
              <div className="avatar" style={{ width: 28, height: 28, fontSize: 11 }}>
                {usuario.nombre.charAt(0).toUpperCase()}
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{usuario.nombre}</div>
                <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{usuario.rol}</div>
              </div>
            </div>
          )}
          {/* Collapse toggle — hidden on mobile */}
          <button className="sidebar-toggle sidebar-toggle-desktop" onClick={() => setCollapsed(!collapsed)} title={collapsed ? 'Expandir' : 'Colapsar'}>
            {collapsed ? <ChevronRight size={15} /> : <ChevronLeft size={15} />}
          </button>
          <button className="nav-item btn-ghost" onClick={logout} title="Cerrar sesión" style={{ width: '100%', marginTop: 2, cursor: 'pointer' }}>
            <LogOut size={16} className="nav-icon" />
            <span className="nav-label">Cerrar sesión</span>
          </button>
        </div>
      </aside>
    </>
  );
}
