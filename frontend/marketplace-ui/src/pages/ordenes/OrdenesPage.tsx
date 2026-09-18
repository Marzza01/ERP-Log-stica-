import React, { useEffect, useState, useCallback } from 'react';
import { Plus, ChevronRight } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import { ordenesApi } from '../../api/ordenes';
import type { Orden, EstadoOrden } from '../../types';

const ESTADOS: EstadoOrden[] = ['Pendiente', 'Aprobada', 'EnProceso', 'Enviada', 'Entregada', 'Cancelada'];
const TRANSICIONES: Partial<Record<EstadoOrden, EstadoOrden[]>> = {
  Pendiente: ['Aprobada', 'Cancelada'],
  Aprobada: ['EnProceso', 'Cancelada'],
  EnProceso: ['Enviada', 'Cancelada'],
  Enviada: ['Entregada'],
};

const fmt = (d: string) => new Date(d).toLocaleDateString('es-SV', { day: '2-digit', month: 'short', year: '2-digit' });
const fmtMoney = (n: number) => `$${n.toLocaleString('es-SV', { minimumFractionDigits: 2 })}`;

export default function OrdenesPage() {
  const [ordenes, setOrdenes] = useState<Orden[]>([]);
  const [loading, setLoading] = useState(true);
  const [filterEstado, setFilterEstado] = useState<string>('');
  const [selectedOrden, setSelectedOrden] = useState<Orden | null>(null);
  const [transModal, setTransModal] = useState(false);
  const [nuevoEstado, setNuevoEstado] = useState<EstadoOrden | ''>('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await ordenesApi.listar({ estado: filterEstado || undefined, tamano: 100 });
      setOrdenes(res.ordenes);
    } catch {
      // Demo data
      setOrdenes(Array.from({ length: 20 }, (_, i) => ({
        id: crypto.randomUUID(),
        numeroOrden: `ORD-2026091${i}-${10000 + i}`,
        estado: ESTADOS[i % ESTADOS.length],
        tipoCliente: i % 3 === 0 ? 'Empresa' : 'ConsumidorFinal',
        montoTotal: 85 + i * 47.5,
        subTotal: 75 + i * 42,
        impuestoIva: 9.75 + i * 5.46,
        costoEnvio: 5,
        moneda: 'USD',
        tipoComprobante: 'FacturaConsumidorFinal',
        transportista: i % 2 === 0 ? 'LOGIMKT Express' : 'DHL',
        guiaEnvio: i > 3 ? `GU-${100000 + i}` : undefined,
        direccionEntrega: `Colonia #{i + 1}, San Salvador`,
        zonaEnvio: 'San Salvador y AMSS',
        fechaCreacion: new Date(Date.now() - i * 86400000).toISOString(),
        totalItems: 1 + (i % 4),
        minutosEnEstado: i * 120,
      })));
    } finally {
      setLoading(false);
    }
  }, [filterEstado]);

  useEffect(() => { load(); }, [load]);

  const handleTransicion = async () => {
    if (!selectedOrden || !nuevoEstado) return;
    setSaving(true);
    try {
      await ordenesApi.transicionarEstado(selectedOrden.id, nuevoEstado);
      await load();
      setTransModal(false);
      setSelectedOrden(null);
    } catch { /* demo */ }
    setSaving(false);
  };

  const columns = [
    { key: 'numeroOrden', header: 'Nº Orden', className: 'td-mono td-primary' },
    { key: 'estado', header: 'Estado', render: (r: Orden) => <StatusBadge status={r.estado} /> },
    { key: 'tipoCliente', header: 'Tipo Cliente' },
    { key: 'montoTotal', header: 'Total', render: (r: Orden) => <span className="td-primary">{fmtMoney(r.montoTotal)}</span> },
    { key: 'transportista', header: 'Transportista' },
    { key: 'guiaEnvio', header: 'Guía', render: (r: Orden) => r.guiaEnvio ? <span className="td-mono">{r.guiaEnvio}</span> : <span style={{ color: 'var(--text-disabled)' }}>—</span> },
    { key: 'fechaCreacion', header: 'Fecha', render: (r: Orden) => fmt(r.fechaCreacion) },
    { key: 'acciones', header: '', sortable: false, render: (r: Orden) => {
      const sig = TRANSICIONES[r.estado];
      if (!sig?.length) return null;
      return (
        <button className="btn btn-secondary btn-sm" onClick={e => { e.stopPropagation(); setSelectedOrden(r); setNuevoEstado(sig[0]); setTransModal(true); }}>
          <ChevronRight size={11} /> Avanzar
        </button>
      );
    }},
  ];

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Órdenes (OMS)</h1>
          <p className="page-subtitle">Ciclo de vida de órdenes — máquina de 6 estados</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <select className="form-input" value={filterEstado} onChange={e => setFilterEstado(e.target.value)} style={{ width: 'auto', fontSize: 12 }}>
            <option value="">Todos los estados</option>
            {ESTADOS.map(e => <option key={e} value={e}>{e}</option>)}
          </select>
          <button className="btn btn-primary" onClick={load}><Plus size={13} /> Nueva Orden</button>
        </div>
      </div>

      {/* KPIs rápidos */}
      <div className="stat-grid" style={{ gridTemplateColumns: 'repeat(6,1fr)', marginBottom: 16 }}>
        {ESTADOS.map(e => {
          const count = ordenes.filter(o => o.estado === e).length;
          return (
            <div key={e} className="stat-card" style={{ padding: '10px 14px', cursor: 'pointer', opacity: filterEstado === e ? 1 : 0.75 }} onClick={() => setFilterEstado(filterEstado === e ? '' : e)}>
              <div className="stat-info">
                <div className="stat-label">{e}</div>
                <div className="stat-value" style={{ fontSize: 18 }}>{count}</div>
              </div>
              <StatusBadge status={e} dot={false} />
            </div>
          );
        })}
      </div>

      <DataTable<Orden>
        title="Listado de Órdenes"
        columns={columns}
        data={ordenes}
        loading={loading}
        rowKey={r => r.id}
        pageSize={15}
        emptyMessage="No hay órdenes en este estado"
        onRowClick={r => setSelectedOrden(r)}
      />

      {/* Modal detalle */}
      <Modal title={`Orden ${selectedOrden?.numeroOrden}`} open={!!selectedOrden && !transModal} onClose={() => setSelectedOrden(null)} size="md">
        {selectedOrden && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div className="grid-2">
              {[
                ['Estado', <StatusBadge status={selectedOrden.estado} />],
                ['Tipo Cliente', selectedOrden.tipoCliente],
                ['Subtotal', fmtMoney(selectedOrden.subTotal)],
                ['IVA 13%', fmtMoney(selectedOrden.impuestoIva)],
                ['Costo envío', fmtMoney(selectedOrden.costoEnvio)],
                ['Total', <strong>{fmtMoney(selectedOrden.montoTotal)}</strong>],
                ['Transportista', selectedOrden.transportista],
                ['Guía', selectedOrden.guiaEnvio ?? '—'],
                ['Zona', selectedOrden.zonaEnvio],
                ['Fecha', fmt(selectedOrden.fechaCreacion)],
              ].map(([lbl, val]) => (
                <div key={String(lbl)} style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.06em', fontWeight: 600 }}>{String(lbl)}</span>
                  <span style={{ fontSize: 13, color: 'var(--text-primary)' }}>{val as React.ReactNode}</span>
                </div>
              ))}
            </div>
            {TRANSICIONES[selectedOrden.estado]?.length && (
              <div style={{ borderTop: '1px solid var(--border)', paddingTop: 12 }}>
                <button className="btn btn-primary" onClick={() => { setNuevoEstado(TRANSICIONES[selectedOrden.estado]![0]); setTransModal(true); }}>
                  <ChevronRight size={13} /> Avanzar estado
                </button>
              </div>
            )}
          </div>
        )}
      </Modal>

      {/* Modal transición */}
      <Modal
        title="Cambiar estado de orden"
        open={transModal}
        onClose={() => setTransModal(false)}
        footer={
          <>
            <button className="btn btn-secondary" onClick={() => setTransModal(false)}>Cancelar</button>
            <button className="btn btn-primary" onClick={handleTransicion} disabled={saving || !nuevoEstado}>
              {saving ? 'Guardando...' : 'Confirmar cambio'}
            </button>
          </>
        }
      >
        <p style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
          Orden: <strong style={{ color: 'var(--text-primary)' }}>{selectedOrden?.numeroOrden}</strong>
        </p>
        <p style={{ fontSize: 12, color: 'var(--text-muted)' }}>Estado actual: <StatusBadge status={selectedOrden?.estado ?? ''} /></p>
        <div className="form-group">
          <label className="form-label">Nuevo estado</label>
          <select className="form-input" value={nuevoEstado} onChange={e => setNuevoEstado(e.target.value as EstadoOrden)}>
            {(TRANSICIONES[selectedOrden?.estado ?? 'Pendiente'] ?? []).map(e => (
              <option key={e} value={e}>{e}</option>
            ))}
          </select>
        </div>
      </Modal>
    </div>
  );
}
