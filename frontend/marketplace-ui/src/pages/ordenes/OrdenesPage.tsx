import React, { useEffect, useState, useCallback } from 'react';
import { Plus, ChevronRight } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import { ordenesApi } from '../../api/ordenes';
import type { Orden, EstadoOrden, TipoCliente } from '../../types';

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
  const [isCompact, setIsCompact] = useState(false);

  // Modal Nueva Orden
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [creatingOrder, setCreatingOrder] = useState(false);
  const [orderForm, setOrderForm] = useState({
    tipoCliente: 'ConsumidorFinal' as TipoCliente,
    nombreProducto: 'Kit Logístico Starter (Seed SKU)',
    sku: 'SKU-LOGI-8801',
    cantidad: 2,
    precioUnitario: 45.00,
    transportista: 'LOGIMKT Express',
    zonaEnvio: 'San Salvador y AMSS',
    direccionEntrega: 'Av. Las Palmas #404, San Salvador',
    costoEnvio: 5.00,
    condicionPago: 'Inmediato',
    tipoComprobante: 'FacturaConsumidorFinal',
  });

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
        transportista: ['LOGIMKT Flota Propia', 'C807 Express El Salvador', 'Urbano Express El Salvador', 'Cargo Expreso SV'][i % 4],
        guiaEnvio: i > 3 ? `C807-${100000 + i}` : undefined,
        direccionEntrega: `Colonia #${i + 1}, San Salvador`,
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
    const targetId = selectedOrden.id;
    const targetEstado = nuevoEstado;

    try {
      await ordenesApi.transicionarEstado(targetId, targetEstado);
      await load();
    } catch {
      // Optimistic / fallback: actualizar estado local inmediatamente
      setOrdenes(prev =>
        prev.map(o => (o.id === targetId ? { ...o, estado: targetEstado } : o))
      );
    } finally {
      // Siempre reflejar el cambio en la orden seleccionada y cerrar el modal
      setSelectedOrden(prev => (prev && prev.id === targetId ? { ...prev, estado: targetEstado } : null));
      setTransModal(false);
      setSaving(false);
    }
  };

  const handleCrearOrden = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreatingOrder(true);
    try {
      await ordenesApi.crear({
        compradorId: crypto.randomUUID(),
        vendedorId: crypto.randomUUID(),
        tipoCliente: orderForm.tipoCliente,
        tipoComprobante: orderForm.tipoComprobante,
        condicionPago: orderForm.condicionPago,
        transportista: orderForm.transportista,
        zonaEnvio: orderForm.zonaEnvio,
        direccionEntrega: orderForm.direccionEntrega,
        costoEnvio: Number(orderForm.costoEnvio),
        items: [
          {
            productoId: crypto.randomUUID(),
            sku: orderForm.sku,
            nombre: orderForm.nombreProducto,
            cantidad: Number(orderForm.cantidad),
            precioUnitario: Number(orderForm.precioUnitario),
          },
        ],
      });
      await load();
      setCreateModalOpen(false);
    } catch {
      // Optimistic demo fallback
      const sub = Number(orderForm.cantidad) * Number(orderForm.precioUnitario);
      const iva = Math.round(sub * 0.13 * 100) / 100;
      const envio = Number(orderForm.costoEnvio);
      const nueva: Orden = {
        id: crypto.randomUUID(),
        numeroOrden: `ORD-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}-${Math.floor(10000 + Math.random() * 90000)}`,
        estado: 'Pendiente',
        tipoCliente: orderForm.tipoCliente,
        montoTotal: sub + iva + envio,
        subTotal: sub,
        impuestoIva: iva,
        costoEnvio: envio,
        moneda: 'USD',
        tipoComprobante: orderForm.tipoComprobante,
        transportista: orderForm.transportista,
        direccionEntrega: orderForm.direccionEntrega,
        zonaEnvio: orderForm.zonaEnvio,
        fechaCreacion: new Date().toISOString(),
        totalItems: 1,
        minutosEnEstado: 0,
      };
      setOrdenes(prev => [nueva, ...prev]);
      setCreateModalOpen(false);
    } finally {
      setCreatingOrder(false);
    }
  };

  // Métricas del canal (Efferd #2 pattern: Channel breakdown & gross total)
  const totalFacturado = ordenes.reduce((acc, o) => acc + (o.montoTotal || 0), 0);
  const ordenesB2B = ordenes.filter(o => o.tipoCliente === 'Empresa');
  const ordenesB2C = ordenes.filter(o => o.tipoCliente !== 'Empresa');

  const columns = [
    { key: 'numeroOrden', header: 'Nº Orden', className: 'td-mono td-primary' },
    { key: 'estado', header: 'Estado', render: (r: Orden) => <StatusBadge status={r.estado} /> },
    {
      key: 'tipoCliente',
      header: 'Canal',
      render: (r: Orden) => (
        <span style={{
          fontSize: 11,
          padding: '2px 6px',
          borderRadius: 4,
          background: r.tipoCliente === 'Empresa' ? 'rgba(99,102,241,0.12)' : 'var(--bg-surface)',
          color: r.tipoCliente === 'Empresa' ? 'var(--accent-hover)' : 'var(--text-secondary)',
          fontWeight: 600
        }}>
          {r.tipoCliente === 'Empresa' ? 'B2B Empresa' : 'B2C Retail'}
        </span>
      )
    },
    { key: 'montoTotal', header: 'Total', className: 'td-right', render: (r: Orden) => <span className="td-primary td-num">{fmtMoney(r.montoTotal)}</span> },
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
      <div className="page-header" style={{ alignItems: 'flex-start' }}>
        <div>
          <h1 className="page-title">Órdenes (OMS)</h1>
          <p className="page-subtitle">Gestión operativa multicanal — Flujo B2B y B2C con máquina de estados</p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <button
            className={`btn btn-secondary btn-sm${isCompact ? ' active' : ''}`}
            onClick={() => setIsCompact(!isCompact)}
            title="Alternar densidad visual de la tabla"
          >
            {isCompact ? 'Vista Normal' : 'Vista Compacta'}
          </button>
          <select className="form-input" value={filterEstado} onChange={e => setFilterEstado(e.target.value)} style={{ width: 'auto', fontSize: 12 }}>
            <option value="">Todos los estados</option>
            {ESTADOS.map(e => <option key={e} value={e}>{e}</option>)}
          </select>
          <button className="btn btn-primary" onClick={() => setCreateModalOpen(true)}><Plus size={13} /> Nueva Orden</button>
        </div>
      </div>

      {/* Resumen Denso Efferd #2: Canal B2B vs B2C y Volumen Facturado */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(3, 1fr)',
        gap: 12,
        marginBottom: 16,
        padding: '12px 16px',
        background: 'var(--bg-card)',
        border: '1px solid var(--border)',
        borderRadius: 'var(--radius-lg)'
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <div style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--accent)' }} />
          <div>
            <div style={{ fontSize: 10, textTransform: 'uppercase', color: 'var(--text-muted)', fontWeight: 600 }}>Volumen B2B (Empresas)</div>
            <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)' }} className="td-num">
              {ordenesB2B.length} órdenes ({fmtMoney(ordenesB2B.reduce((a, b) => a + b.montoTotal, 0))})
            </div>
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, borderLeft: '1px solid var(--border)', paddingLeft: 12 }}>
          <div style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--green)' }} />
          <div>
            <div style={{ fontSize: 10, textTransform: 'uppercase', color: 'var(--text-muted)', fontWeight: 600 }}>Volumen B2C (Consumidor)</div>
            <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)' }} className="td-num">
              {ordenesB2C.length} órdenes ({fmtMoney(ordenesB2C.reduce((a, b) => a + b.montoTotal, 0))})
            </div>
          </div>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, borderLeft: '1px solid var(--border)', paddingLeft: 12 }}>
          <div style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--yellow)' }} />
          <div>
            <div style={{ fontSize: 10, textTransform: 'uppercase', color: 'var(--text-muted)', fontWeight: 600 }}>Total Facturado Vista</div>
            <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)' }} className="td-num">
              {fmtMoney(totalFacturado)}
            </div>
          </div>
        </div>
      </div>

      {/* Selector de estados rápido con contador */}
      <div className="stat-grid" style={{ gridTemplateColumns: 'repeat(6,1fr)', marginBottom: 16 }}>
        {ESTADOS.map(e => {
          const count = ordenes.filter(o => o.estado === e).length;
          return (
            <div
              key={e}
              className="stat-card"
              style={{
                padding: '10px 14px',
                cursor: 'pointer',
                borderColor: filterEstado === e ? 'var(--accent)' : 'var(--border)',
                background: filterEstado === e ? 'rgba(99,102,241,0.06)' : 'var(--bg-card)'
              }}
              onClick={() => setFilterEstado(filterEstado === e ? '' : e)}
            >
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
        compact={isCompact}
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
            <button
              className="btn btn-primary"
              onClick={handleTransicion}
              disabled={saving || !nuevoEstado}
            >
              {saving ? 'Guardando...' : `Confirmar cambio a ${nuevoEstado || '...'}`}
            </button>
          </>
        }
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <p style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
            Orden: <strong style={{ color: 'var(--text-primary)' }}>{selectedOrden?.numeroOrden}</strong>
          </p>
          <p style={{ fontSize: 12, color: 'var(--text-muted)' }}>
            Estado actual: <StatusBadge status={selectedOrden?.estado ?? ''} />
          </p>
          <div className="form-group">
            <label className="form-label">Nuevo estado a aplicar</label>
            <select
              className="form-input"
              value={nuevoEstado}
              onChange={e => setNuevoEstado(e.target.value as EstadoOrden)}
            >
              {(selectedOrden ? TRANSICIONES[selectedOrden.estado] ?? [] : []).map(e => (
                <option key={e} value={e}>{e}</option>
              ))}
            </select>
          </div>
        </div>
      </Modal>

      {/* Modal Crear Nueva Orden */}
      <Modal
        title="Crear Nueva Orden (OMS)"
        open={createModalOpen}
        onClose={() => setCreateModalOpen(false)}
        size="lg"
        footer={
          <>
            <button className="btn btn-secondary" onClick={() => setCreateModalOpen(false)}>Cancelar</button>
            <button
              className="btn btn-primary"
              onClick={handleCrearOrden}
              disabled={creatingOrder || !orderForm.nombreProducto || orderForm.cantidad <= 0 || orderForm.precioUnitario <= 0}
            >
              {creatingOrder ? 'Registrando...' : 'Crear y Registrar Orden'}
            </button>
          </>
        }
      >
        <form onSubmit={handleCrearOrden} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Canal / Tipo de Cliente</label>
              <select
                className="form-input"
                value={orderForm.tipoCliente}
                onChange={e => {
                  const val = e.target.value as TipoCliente;
                  setOrderForm({
                    ...orderForm,
                    tipoCliente: val,
                    tipoComprobante: val === 'Empresa' ? 'CreditoFiscalCCF' : 'FacturaConsumidorFinal',
                    condicionPago: val === 'Empresa' ? 'Credito30' : 'Inmediato',
                  });
                }}
              >
                <option value="ConsumidorFinal">B2C Retail (Consumidor Final)</option>
                <option value="Empresa">B2B Empresa (Distribuidor / Mayoreo)</option>
                <option value="GobiernoPublico">B2G Gobierno / Entidad Pública</option>
              </select>
            </div>

            <div className="form-group">
              <label className="form-label">Comprobante Fiscal</label>
              <select
                className="form-input"
                value={orderForm.tipoComprobante}
                onChange={e => setOrderForm({ ...orderForm, tipoComprobante: e.target.value })}
              >
                <option value="FacturaConsumidorFinal">Factura Consumidor Final</option>
                <option value="CreditoFiscalCCF">Crédito Fiscal (CCF)</option>
                <option value="FacturaExportacion">Factura de Exportación</option>
              </select>
            </div>
          </div>

          <div style={{ padding: '10px 14px', borderRadius: 6, background: 'var(--bg-surface)', border: '1px solid var(--border)' }}>
            <span style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--text-muted)', letterSpacing: '0.05em' }}>
              Detalle del Producto / Ítem
            </span>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 10, marginTop: 8 }}>
              <div className="form-group">
                <label className="form-label">Nombre del Producto</label>
                <input
                  type="text"
                  className="form-input"
                  value={orderForm.nombreProducto}
                  onChange={e => setOrderForm({ ...orderForm, nombreProducto: e.target.value })}
                  placeholder="Ej: Smartphone Galaxy A54"
                  required
                />
              </div>
              <div className="form-group">
                <label className="form-label">SKU</label>
                <input
                  type="text"
                  className="form-input"
                  value={orderForm.sku}
                  onChange={e => setOrderForm({ ...orderForm, sku: e.target.value })}
                  placeholder="SKU-LOGI-XXXX"
                  required
                />
              </div>
              <div className="form-group">
                <label className="form-label">Cantidad</label>
                <input
                  type="number"
                  min="1"
                  className="form-input"
                  value={orderForm.cantidad}
                  onChange={e => setOrderForm({ ...orderForm, cantidad: parseInt(e.target.value) || 1 })}
                  required
                />
              </div>
              <div className="form-group">
                <label className="form-label">Precio Unitario ($ USD)</label>
                <input
                  type="number"
                  step="0.01"
                  min="0.01"
                  className="form-input"
                  value={orderForm.precioUnitario}
                  onChange={e => setOrderForm({ ...orderForm, precioUnitario: parseFloat(e.target.value) || 0 })}
                  required
                />
              </div>
            </div>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 12 }}>
            <div className="form-group">
              <label className="form-label">Operador Logístico / Transportista</label>
              <select
                className="form-input"
                value={orderForm.transportista}
                onChange={e => setOrderForm({ ...orderForm, transportista: e.target.value })}
              >
                <option value="C807 Express El Salvador">C807 Express El Salvador (Operador Local 3PL)</option>
                <option value="LOGIMKT Flota Propia">LOGIMKT Express (Flota Local Propia AMSS)</option>
                <option value="Urbano Express El Salvador">Urbano Express El Salvador</option>
                <option value="Cargo Expreso SV">Cargo Expreso El Salvador</option>
                <option value="DHL Express El Salvador">DHL Express (Nacional / Internacional)</option>
                <option value="RetiroEnBodega">Retiro en Bodega Central (Pick-Up)</option>
              </select>
            </div>
            <div className="form-group">
              <label className="form-label">Zona de Envío</label>
              <input
                type="text"
                className="form-input"
                value={orderForm.zonaEnvio}
                onChange={e => setOrderForm({ ...orderForm, zonaEnvio: e.target.value })}
              />
            </div>
            <div className="form-group">
              <label className="form-label">Costo Envío ($ USD)</label>
              <input
                type="number"
                step="0.5"
                min="0"
                className="form-input"
                value={orderForm.costoEnvio}
                onChange={e => setOrderForm({ ...orderForm, costoEnvio: parseFloat(e.target.value) || 0 })}
              />
            </div>
          </div>

          <div className="form-group">
            <label className="form-label">Dirección de Entrega</label>
            <input
              type="text"
              className="form-input"
              value={orderForm.direccionEntrega}
              onChange={e => setOrderForm({ ...orderForm, direccionEntrega: e.target.value })}
              placeholder="Dirección completa de entrega"
              required
            />
          </div>

          {/* Resumen de Liquidación */}
          <div style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            padding: '10px 14px',
            background: 'rgba(99,102,241,0.08)',
            border: '1px solid rgba(99,102,241,0.2)',
            borderRadius: 6,
            fontSize: 12
          }}>
            <div>
              <span style={{ color: 'var(--text-muted)' }}>Subtotal: </span>
              <strong>{fmtMoney(orderForm.cantidad * orderForm.precioUnitario)}</strong>
              <span style={{ color: 'var(--text-muted)', marginLeft: 10 }}>IVA (13%): </span>
              <strong>{fmtMoney(Math.round(orderForm.cantidad * orderForm.precioUnitario * 0.13 * 100) / 100)}</strong>
              <span style={{ color: 'var(--text-muted)', marginLeft: 10 }}>Envío: </span>
              <strong>{fmtMoney(orderForm.costoEnvio)}</strong>
            </div>
            <div>
              <span style={{ color: 'var(--text-muted)' }}>Total a Facturar: </span>
              <strong style={{ fontSize: 14, color: 'var(--accent-hover)' }}>
                {fmtMoney(
                  orderForm.cantidad * orderForm.precioUnitario +
                  Math.round(orderForm.cantidad * orderForm.precioUnitario * 0.13 * 100) / 100 +
                  orderForm.costoEnvio
                )}
              </strong>
            </div>
          </div>
        </form>
      </Modal>
    </div>
  );
}
