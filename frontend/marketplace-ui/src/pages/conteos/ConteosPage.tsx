/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useMemo, useState } from 'react';
import { ClipboardList, Plus, RefreshCw } from 'lucide-react';
import DataTable, { type Column } from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { conteosApi, type ConteoCiclico } from '../../api/conteos';
import { wmsApi } from '../../api/wms';
import type { StockTransferenciaWms } from '../../types';

export default function ConteosPage() {
  const [conteos, setConteos] = useState<ConteoCiclico[]>([]);
  const [stocks, setStocks] = useState<StockTransferenciaWms[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');

  const [modalPlan, setModalPlan] = useState(false);
  const [stockFisicoId, setStockFisicoId] = useState('');
  const [observaciones, setObservaciones] = useState('');

  const [modalCantidad, setModalCantidad] = useState(false);
  const [conteoSeleccionado, setConteoSeleccionado] = useState<ConteoCiclico | null>(null);
  const [modoCantidad, setModoCantidad] = useState<'primer' | 'reconteo'>('primer');
  const [cantidad, setCantidad] = useState('');
  const [saving, setSaving] = useState(false);

  const cargar = async () => {
    setLoading(true);
    setError('');
    try {
      const [c, s] = await Promise.all([conteosApi.listar(), wmsApi.listarStocks()]);
      setConteos(c);
      setStocks(s);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudieron cargar los conteos cíclicos.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { cargar(); }, []);

  const stockSeleccionado = useMemo(() => stocks.find(s => s.id === stockFisicoId), [stocks, stockFisicoId]);

  const planificar = async () => {
    if (!stockFisicoId) return setError('Selecciona un saldo de inventario.');
    setSaving(true); setError('');
    try {
      const r = await conteosApi.planificar({ stockFisicoId, observaciones: observaciones.trim() || undefined });
      setModalPlan(false); setMensaje(r.mensaje); setStockFisicoId(''); setObservaciones(''); await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo planificar el conteo.');
    } finally { setSaving(false); }
  };

  const iniciar = async (id: string) => {
    setError('');
    try {
      const r = await conteosApi.iniciar(id);
      setMensaje(r.mensaje); await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : 'No se pudo iniciar el conteo.'); }
  };

  const abrirCantidad = (conteo: ConteoCiclico, modo: 'primer' | 'reconteo') => {
    setConteoSeleccionado(conteo); setModoCantidad(modo); setCantidad(''); setModalCantidad(true); setError('');
  };

  const registrarCantidad = async () => {
    if (!conteoSeleccionado) return;
    const qty = Number(cantidad);
    if (!Number.isInteger(qty) || qty < 0) return setError('La cantidad debe ser un entero igual o mayor que cero.');

    setSaving(true); setError('');
    try {
      const r = modoCantidad === 'primer'
        ? await conteosApi.primerConteo(conteoSeleccionado.id, qty)
        : await conteosApi.reconteo(conteoSeleccionado.id, qty);
      setModalCantidad(false); setMensaje(r.mensaje); await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo registrar la cantidad contada.');
    } finally { setSaving(false); }
  };

  const cancelar = async (id: string) => {
    setError('');
    try {
      const r = await conteosApi.cancelar(id);
      setMensaje(r.mensaje); await cargar();
    } catch (e) { setError(e instanceof Error ? e.message : 'No se pudo cancelar el conteo.'); }
  };

  const estadoBadge = (r: ConteoCiclico) => {
    const cls = r.estado === 'Completado' || r.estado === 'CompletadoConAjuste'
      ? 'badge-entregada'
      : r.estado === 'Cancelado'
        ? 'badge-cancelada'
        : 'badge-pendiente';
    return <span className={`badge ${cls}`}>{r.estado}</span>;
  };

  const columns: Column<ConteoCiclico>[] = [
    { key: 'sku', header: 'SKU / Producto', render: r => <div><div className="td-mono td-primary">{r.sku}</div><div style={{fontSize:11,color:'var(--text-muted)'}}>{r.productoNombre}</div></div> },
    { key: 'ubicacionCodigo', header: 'Ubicación', className: 'td-mono' },
    { key: 'estado', header: 'Estado', render: estadoBadge },
    { key: 'cantidadSistema', header: 'Sistema', render: r => r.cantidadSistema ?? '—' },
    { key: 'primerConteo', header: '1.er conteo', render: r => r.primerConteo ?? '—' },
    { key: 'segundoConteo', header: 'Reconteo', render: r => r.segundoConteo ?? '—' },
    { key: 'ajuste', header: 'Ajuste', render: r => r.ajusteInventarioId ? <div><div className="td-mono">{r.ajusteInventarioId.slice(0, 8)}…</div><div style={{fontSize:11,color:'var(--text-muted)'}}>{r.estadoAjuste ?? 'Pendiente'}</div></div> : '—' },
    { key: 'acciones', header: 'Acciones', render: r => <div style={{display:'flex',gap:6,flexWrap:'wrap'}}>
      {r.estado === 'Planificado' && <button className="btn btn-primary" onClick={() => iniciar(r.id)}>Iniciar</button>}
      {r.estado === 'EnConteo' && <button className="btn btn-primary" onClick={() => abrirCantidad(r, 'primer')}>Registrar conteo</button>}
      {r.estado === 'RequiereReconteo' && <button className="btn btn-primary" onClick={() => abrirCantidad(r, 'reconteo')}>Registrar reconteo</button>}
      {(r.estado === 'Planificado' || r.estado === 'EnConteo' || r.estado === 'RequiereReconteo') && <button className="btn btn-ghost" onClick={() => cancelar(r.id)}>Cancelar</button>}
    </div> },
  ];

  return <div>
    <div className="page-header">
      <div><h1 className="page-title">Conteos cíclicos</h1><p className="page-subtitle">Verificación física con reconteo obligatorio antes de generar ajustes</p></div>
      <div style={{display:'flex',gap:8}}>
        <button className="btn btn-primary" onClick={() => { setModalPlan(true); setError(''); }}><Plus size={13}/> Planificar conteo</button>
        <button className="btn btn-secondary" onClick={cargar}><RefreshCw size={13}/> Actualizar</button>
      </div>
    </div>

    {error && <AlertBanner variant="danger" title="Conteos cíclicos" message={error} onClose={() => setError('')} />}
    {mensaje && <AlertBanner variant="success" title="Operación completada" message={mensaje} onClose={() => setMensaje('')} />}

    <AlertBanner
      variant="info"
      title="Regla de control"
      message="El conteo no modifica stock. Si el primer conteo difiere del sistema, se exige reconteo. Una diferencia confirmada genera un ajuste pendiente de aprobación, que solo cambiará el inventario al aplicarse desde Ajustes de inventario."
    />

    <DataTable<ConteoCiclico> title="Conteos registrados" columns={columns} data={conteos} loading={loading} rowKey={r => r.id} pageSize={10} emptyMessage="No hay conteos cíclicos registrados" />

    <Modal
      title="Planificar conteo cíclico"
      open={modalPlan}
      onClose={() => !saving && setModalPlan(false)}
      footer={<><button className="btn btn-secondary" onClick={() => setModalPlan(false)} disabled={saving}>Cancelar</button><button className="btn btn-primary" onClick={planificar} disabled={saving}>{saving ? 'Guardando...' : 'Planificar'}</button></>}
    >
      <div style={{display:'grid',gap:12}}>
        <label className="form-group"><span className="form-label">Saldo / ubicación</span><select className="form-input" value={stockFisicoId} onChange={e => setStockFisicoId(e.target.value)}><option value="">Seleccionar...</option>{stocks.map(s => <option key={s.id} value={s.id}>{s.sku} · {s.ubicacionCodigo} · físico {s.cantidadTotal}</option>)}</select></label>
        {stockSeleccionado && <div style={{fontSize:12,color:'var(--text-muted)'}}>Disponible actual: {stockSeleccionado.cantidadDisponible} · reservado: {stockSeleccionado.cantidadReservada} · bloqueado: {stockSeleccionado.cantidadBloqueada}</div>}
        <label className="form-group"><span className="form-label">Observaciones</span><textarea className="form-input" rows={3} value={observaciones} onChange={e => setObservaciones(e.target.value)} placeholder="Opcional" /></label>
      </div>
    </Modal>

    <Modal
      title={modoCantidad === 'primer' ? 'Registrar primer conteo' : 'Registrar reconteo'}
      open={modalCantidad}
      onClose={() => !saving && setModalCantidad(false)}
      footer={<><button className="btn btn-secondary" onClick={() => setModalCantidad(false)} disabled={saving}>Cancelar</button><button className="btn btn-primary" onClick={registrarCantidad} disabled={saving}>{saving ? 'Guardando...' : 'Registrar'}</button></>}
    >
      <div style={{display:'grid',gap:12}}>
        <div style={{display:'flex',gap:8,alignItems:'center'}}><ClipboardList size={18}/><div><strong>{conteoSeleccionado?.sku}</strong><div style={{fontSize:11,color:'var(--text-muted)'}}>{conteoSeleccionado?.ubicacionCodigo}</div></div></div>
        <label className="form-group"><span className="form-label">Cantidad física contada</span><input className="form-input" type="number" min="0" step="1" value={cantidad} onChange={e => setCantidad(e.target.value)} autoFocus /></label>
        <div style={{fontSize:12,color:'var(--text-muted)'}}>El operador registra la cantidad física observada. El stock del sistema no se modifica en este paso.</div>
      </div>
    </Modal>
  </div>;
}
