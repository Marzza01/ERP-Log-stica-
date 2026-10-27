/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useMemo, useState } from 'react';
import { ClipboardCheck, Plus, RefreshCw } from 'lucide-react';
import DataTable, { type Column } from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { ajustesApi, type AjusteInventario, type TipoAjusteInventario } from '../../api/ajustes';
import { wmsApi } from '../../api/wms';
import type { StockTransferenciaWms } from '../../types';

export default function AjustesPage() {
  const [ajustes, setAjustes] = useState<AjusteInventario[]>([]);
  const [stocks, setStocks] = useState<StockTransferenciaWms[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');
  const [modal, setModal] = useState(false);
  const [stockFisicoId, setStockFisicoId] = useState('');
  const [tipo, setTipo] = useState<TipoAjusteInventario>('Positivo');
  const [cantidad, setCantidad] = useState('');
  const [motivo, setMotivo] = useState('');
  const [detalle, setDetalle] = useState('');
  const [referencia, setReferencia] = useState('');
  const [saving, setSaving] = useState(false);

  const cargar = async () => {
    setLoading(true);
    setError('');
    try {
      const [a, s] = await Promise.all([ajustesApi.listar(), wmsApi.listarStocks()]);
      setAjustes(a);
      setStocks(s);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudieron cargar los ajustes.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { cargar(); }, []);

  const stockSeleccionado = useMemo(() => stocks.find(s => s.id === stockFisicoId), [stocks, stockFisicoId]);

  const abrir = () => {
    setStockFisicoId(''); setTipo('Positivo'); setCantidad(''); setMotivo(''); setDetalle(''); setReferencia('');
    setModal(true); setError('');
  };

  const crear = async () => {
    const qty = Number(cantidad);
    if (!stockFisicoId) return setError('Selecciona un saldo de inventario.');
    if (!Number.isInteger(qty) || qty <= 0) return setError('La cantidad debe ser un entero mayor que cero.');
    if (!motivo.trim()) return setError('El motivo es obligatorio.');
    if (tipo === 'Negativo' && stockSeleccionado && qty > stockSeleccionado.cantidadDisponible)
      return setError(`Solo hay ${stockSeleccionado.cantidadDisponible} unidades disponibles.`);

    setSaving(true); setError('');
    try {
      const r = await ajustesApi.crear({
        stockFisicoId, tipo, cantidad: qty, motivo: motivo.trim(),
        detalle: detalle.trim() || undefined, referencia: referencia.trim() || undefined,
      });
      setModal(false); setMensaje(r.mensaje); await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo crear el ajuste.');
    } finally { setSaving(false); }
  };

  const ejecutar = async (accion: 'aprobar' | 'rechazar' | 'cancelar' | 'aplicar', id: string) => {
    setError('');
    try {
      const r = await ajustesApi[accion](id);
      setMensaje(r.mensaje); await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : `No se pudo ${accion} el ajuste.`);
    }
  };

  const columns: Column<AjusteInventario>[] = [
    { key: 'sku', header: 'SKU / Producto', render: r => <div><div className="td-mono td-primary">{r.sku}</div><div style={{fontSize:11,color:'var(--text-muted)'}}>{r.productoNombre}</div></div> },
    { key: 'ubicacionCodigo', header: 'Ubicación', className: 'td-mono' },
    { key: 'tipo', header: 'Tipo', render: r => <span className={`badge ${r.tipo === 'Positivo' ? 'badge-entregada' : 'badge-cancelada'}`}>{r.tipo}</span> },
    { key: 'cantidad', header: 'Cantidad' },
    { key: 'motivo', header: 'Motivo' },
    { key: 'estado', header: 'Estado', render: r => <span className={`badge ${r.estado === 'Aplicado' ? 'badge-entregada' : r.estado === 'Rechazado' || r.estado === 'Cancelado' ? 'badge-cancelada' : 'badge-pendiente'}`}>{r.estado}</span> },
    { key: 'stockActual', header: 'Stock / Disponible', render: r => <span>{r.stockActual} / {r.disponibleActual}</span> },
    { key: 'acciones', header: 'Acciones', render: r => (
      <div style={{display:'flex',gap:6,flexWrap:'wrap'}}>
        {r.estado === 'PendienteAprobacion' && <><button className="btn btn-secondary" onClick={() => ejecutar('aprobar', r.id)}>Aprobar</button><button className="btn btn-ghost" onClick={() => ejecutar('rechazar', r.id)}>Rechazar</button></>}
        {r.estado === 'Aprobado' && <button className="btn btn-primary" onClick={() => ejecutar('aplicar', r.id)}>Aplicar</button>}
        {(r.estado === 'PendienteAprobacion' || r.estado === 'Aprobado') && <button className="btn btn-ghost" onClick={() => ejecutar('cancelar', r.id)}>Cancelar</button>}
      </div>
    )},
  ];

  return <div>
    <div className="page-header">
      <div><h1 className="page-title">Ajustes de inventario</h1><p className="page-subtitle">Correcciones auditables: crear → aprobar → aplicar</p></div>
      <div style={{display:'flex',gap:8}}><button className="btn btn-primary" onClick={abrir}><Plus size={13}/> Nuevo ajuste</button><button className="btn btn-secondary" onClick={cargar}><RefreshCw size={13}/> Actualizar</button></div>
    </div>

    {error && <AlertBanner variant="danger" title="Ajustes de inventario" message={error} onClose={() => setError('')} />}
    {mensaje && <AlertBanner variant="success" title="Operación completada" message={mensaje} onClose={() => setMensaje('')} />}

    <AlertBanner variant="info" title="Control de stock" message="Crear o aprobar una solicitud no modifica el inventario. El cambio físico ocurre únicamente al aplicar un ajuste aprobado." />

    <DataTable<AjusteInventario> title="Solicitudes de ajuste" columns={columns} data={ajustes} loading={loading} rowKey={r => r.id} pageSize={10} emptyMessage="No hay ajustes registrados" />

    <Modal title="Nuevo ajuste de inventario" open={modal} onClose={() => !saving && setModal(false)} footer={<><button className="btn btn-secondary" onClick={() => setModal(false)} disabled={saving}>Cancelar</button><button className="btn btn-primary" onClick={crear} disabled={saving}>{saving ? 'Guardando...' : 'Crear ajuste'}</button></>}>
      <div style={{display:'grid',gap:12}}>
        <label className="form-group"><span className="form-label">Saldo / ubicación</span><select className="form-input" value={stockFisicoId} onChange={e => setStockFisicoId(e.target.value)}><option value="">Seleccionar...</option>{stocks.map(s => <option key={s.id} value={s.id}>{s.sku} · {s.ubicacionCodigo} · físico {s.cantidadTotal} · disponible {s.cantidadDisponible}</option>)}</select></label>
        <div style={{display:'grid',gridTemplateColumns:'1fr 1fr',gap:12}}>
          <label className="form-group"><span className="form-label">Tipo</span><select className="form-input" value={tipo} onChange={e => setTipo(e.target.value as TipoAjusteInventario)}><option value="Positivo">Positivo</option><option value="Negativo">Negativo</option></select></label>
          <label className="form-group"><span className="form-label">Cantidad</span><input className="form-input" type="number" min="1" step="1" value={cantidad} onChange={e => setCantidad(e.target.value)} /></label>
        </div>
        <label className="form-group"><span className="form-label">Motivo</span><input className="form-input" value={motivo} onChange={e => setMotivo(e.target.value)} placeholder="Ej. Diferencia de conteo" /></label>
        <label className="form-group"><span className="form-label">Detalle</span><textarea className="form-input" value={detalle} onChange={e => setDetalle(e.target.value)} rows={3} /></label>
        <label className="form-group"><span className="form-label">Referencia</span><input className="form-input" value={referencia} onChange={e => setReferencia(e.target.value)} placeholder="Opcional" /></label>
      </div>
    </Modal>
  </div>;
}
