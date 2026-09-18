import React from 'react';
import { Truck, Package, AlertCircle } from 'lucide-react';

const comingSoon = (title: string, sub: string, icon: React.ReactNode) => () => (
  <div>
    <div className="page-header">
      <div><h1 className="page-title">{title}</h1><p className="page-subtitle">{sub}</p></div>
    </div>
    <div className="empty-state" style={{ padding: 80 }}>
      <div className="empty-state-icon" style={{ width: 56, height: 56 }}>{icon}</div>
      <p className="empty-state-text">Módulo en desarrollo</p>
      <p className="empty-state-sub">Este módulo será implementado próximamente por el equipo</p>
    </div>
  </div>
);

export const FulfillmentPage = comingSoon('Fulfillment', 'Picking, Packing y Despacho', <Truck size={24} />);
export const RmaPage = comingSoon('RMA / Devoluciones', 'Gestión de devoluciones y reembolsos', <AlertCircle size={24} />);
export const ComprasPage = comingSoon('Compras / 3-Way Match', 'Procurement y cotejo tridireccional', <Package size={24} />);
export const ContabilidadPage = comingSoon('Contabilidad', 'Libro diario y balance general', <Package size={24} />);
export const LiquidacionesPage = comingSoon('Liquidaciones', 'Pagos y splits de comisión', <Package size={24} />);
export const ProductosPage = comingSoon('Productos', 'Catálogo de 500 productos y stock físico', <Package size={24} />);
