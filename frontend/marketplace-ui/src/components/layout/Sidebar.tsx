/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useState } from 'react';
import { NavLink, useLocation } from 'react-router-dom';
import {
  LayoutDashboard, ShoppingCart, Warehouse, Truck, CreditCard,
  RotateCcw, ShoppingBag, BookOpen, Wallet,
  Package, ChevronLeft, ChevronRight, LogOut, X, RefreshCw, Building2, ClipboardCheck, ClipboardList, BellRing, LockKeyhole,
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';

interface NavItem {
  to: string;
  icon: React.ReactNode;
  label: string;
  shortLabel?: string;
  allowedRoles: string[];
}

interface NavGroup {
  label: string;
  items: NavItem[];
}

const navGroups: NavGroup[] = [
  {
    label: 'Marketplace',
    items: [
      { to: '/checkout', icon: <CreditCard size={16} />, label: 'Comprar', shortLabel: 'Comprar', allowedRoles: ['Comprador'] },
    ],
  },
  {
    label: 'Principal',
    items: [
      { to: '/dashboard', icon: <LayoutDashboard size={16} />, label: 'Dashboard',     shortLabel: 'Dashboard', allowedRoles: ['Admin'] },
      {
        to: '/ordenes',
        icon: <ShoppingCart size={16} />,
        label: 'Órdenes',
        shortLabel: 'Órdenes',
        allowedRoles: ['Admin', 'Vendedor'],
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
        allowedRoles: ['Admin', 'Vendedor'],
      },
      {
        to: '/reabastecimiento',
        icon: <RefreshCw size={16} />,
        label: 'Reabastecimiento',
        shortLabel: 'Reabast.',
        allowedRoles: ['Admin', 'Vendedor'],
      },
      {
        to: '/proveedores',
        icon: <Building2 size={16} />,
        label: 'Proveedores',
        shortLabel: 'Proveed.',
        allowedRoles: ['Admin'],
      },
      {
        to: '/ajustes',
        icon: <ClipboardCheck size={16} />,
        label: 'Ajustes de inventario',
        shortLabel: 'Ajustes',
        allowedRoles: ['Admin'],
      },
      {
        to: '/conteos',
        icon: <ClipboardList size={16} />,
        label: 'Conteos cíclicos',
        shortLabel: 'Conteos',
        allowedRoles: ['Admin', 'Vendedor'],
      },
      {
        to: '/alertas-inventario',
        icon: <BellRing size={16} />,
        label: 'Alertas de inventario',
        shortLabel: 'Alertas',
        allowedRoles: ['Admin', 'Vendedor'],
      },
      {
        to: '/bloqueos-inventario',
        icon: <LockKeyhole size={16} />,
        label: 'Bloqueos de inventario',
        shortLabel: 'Bloqueos',
        allowedRoles: ['Admin', 'Vendedor'],
      },
      { to: '/fulfillment', icon: <Truck size={16} />,        label: 'Fulfillment',    shortLabel: 'Fulfillment', allowedRoles: ['Admin', 'Vendedor'] },
      {
        to: '/rma',
        icon: <RotateCcw size={16} />,
        label: 'Devoluciones',
        shortLabel: 'Devoluciones',
        allowedRoles: ['Admin', 'Vendedor'],
      },
      {
        to: '/compras',
        icon: <ShoppingBag size={16} />,
        label: 'Compras',
        shortLabel: 'Compras',
        allowedRoles: ['Admin'],
      },
    ],
  },
  {
    label: 'Finanzas',
    items: [
      { to: '/contabilidad',  icon: <BookOpen size={16} />, label: 'Contabilidad',   shortLabel: 'Contabilidad', allowedRoles: ['Admin'] },
      {
        to: '/liquidaciones',
        icon: <Wallet size={16} />,
        label: 'Liquidaciones',
        shortLabel: 'Pagos',
        allowedRoles: ['Admin', 'Vendedor'],
      },
      { to: '/productos',     icon: <Package size={16} />,  label: 'Productos',      shortLabel: 'Productos', allowedRoles: ['Admin', 'Vendedor'] },
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

  const userRole = usuario?.rol || '';

  const visibleGroups: NavGroup[] = navGroups
    .map(group => ({
      ...group,
      items: group.items.filter(item => item.allowedRoles.includes(userRole)),
    }))
    .filter(group => group.items.length > 0);

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
          {visibleGroups.map((group) => (
            <div key={group.label}>
              <div className="sidebar-section-label">{group.label}</div>
              {group.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}
                  title={collapsed ? item.label : undefined}
                  onClick={onClose}
                >
                  <span className="nav-icon">{item.icon}</span>
                  <span className="nav-label nav-label-full">{item.label}</span>
                  {item.shortLabel && (
                    <span className="nav-label nav-label-short">{item.shortLabel}</span>
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
