/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useMemo, useState } from 'react';
import { LockKeyhole, RefreshCw, UnlockKeyhole } from 'lucide-react';
import DataTable, { type Column } from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { bloqueosApi } from '../../api/bloqueos';
import { wmsApi } from '../../api/wms';
import type { StockTransferenciaWms } from '../../types';

type AccionBloqueo = 'bloquear' | 'desbloquear';

export default function BloqueosInventarioPage() {
  const [stocks, setStocks] = useState<StockTransferenciaWms[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [mensaje, setMensaje] = useState('');
  const [modal, setModal] = useState(false);
  const [accion, setAccion] = useState<AccionBloqueo>('bloquear');
  const [stockId, setStockId] = useState('');
  const [cantidad, setCantidad] = useState('');
  const [motivo, setMotivo] = useState('');
  const [saving, setSaving] = useState(false);

  const cargar = async () => {
    setLoading(true);
    setError('');
    try {
      setStocks(await wmsApi.listarStocks());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudieron cargar los saldos WMS.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { cargar(); }, []);

  const stockSeleccionado = useMemo(
    () => stocks.find(s => s.id === stockId),
    [stocks, stockId],
  );

  const totalFisico = useMemo(() => stocks.reduce((sum, s) => sum + s.cantidadTotal, 0), [stocks]);
  const totalBloqueado = useMemo(() => stocks.reduce((sum, s) => sum + s.cantidadBloqueada, 0), [stocks]);
  const totalDisponible = useMemo(() => stocks.reduce((sum, s) => sum + s.cantidadDisponible, 0), [stocks]);

  const abrir = (stock: StockTransferenciaWms, nuevaAccion: AccionBloqueo) => {
    setStockId(stock.id);
    setAccion(nuevaAccion);
    setCantidad('');
    setMotivo('');
    setError('');
    setModal(true);
  };

  const guardar = async () => {
    const qty = Number(cantidad);
    if (!stockSeleccionado) return setError('Selecciona un saldo de inventario válido.');
    if (!Number.isInteger(qty) || qty <= 0) return setError('La cantidad debe ser un entero mayor que cero.');
    if (!motivo.trim()) return setError('El motivo es obligatorio para mantener trazabilidad.');

    const limite = accion === 'bloquear'
      ? stockSeleccionado.cantidadDisponible
      : stockSeleccionado.cantidadBloqueada;

    if (qty > limite) {
      return setError(
        accion === 'bloquear'
          ? `Solo hay ${limite} unidades disponibles para bloquear.`
          : `Solo hay ${limite} unidades bloqueadas para liberar.`,
      );
    }

    setSaving(true);
    setError('');
    try {
      const input = {
        stockFisicoId: stockSeleccionado.id,
        cantidad: qty,
        operacionId: crypto.randomUUID(),
        motivo: motivo.trim(),
      };
      const result = accion === 'bloquear'
        ? await bloqueosApi.bloquear(input)
        : await bloqueosApi.desbloquear(input);

      setMensaje(result.mensaje);
      setModal(false);
      await cargar();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'No se pudo actualizar el bloqueo de inventario.');
    } finally {
      setSaving(false);
    }
  };

  const columns: Column<StockTransferenciaWms>[] = [
    {
      key: 'sku',
      header: 'SKU / Producto',
      render: r => <div><div className="td-mono td-primary">{r.sku}</div><div style={{fontSize:11,color:'var(--text-muted)'}}>{r.productoNombre}</div></div>,
    },
    { key: 'ubicacionCodigo', header: 'Ubicación', className: 'td-mono' },
    { key: 'cantidadTotal', header: 'Físico' },
    { key: 'cantidadReservada', header: 'Reservado' },
    {
      key: 'cantidadBloqueada',
      header: 'Bloqueado',
      render: r => <span className={r.cantidadBloqueada > 0 ? 'badge badge-cancelada' : ''}>{r.cantidadBloqueada}</span>,
    },
    { key: 'cantidadDisponible', header: 'Disponible' },
    {
      key: 'acciones',
      header: 'Acciones',
      render: r => (
        <div style={{display:'flex',gap:6,flexWrap:'wrap'}}>
          <button className="btn btn-secondary" disabled={r.cantidadDisponible <= 0} onClick={() => abrir(r, 'bloquear')}>
            <LockKeyhole size={12}/> Bloquear
          </button>
          <button className="btn btn-ghost" disabled={r.cantidadBloqueada <= 0} onClick={() => abrir(r, 'desbloquear')}>
            <UnlockKeyhole size={12}/> Desbloquear
          </button>
        </div>
      ),
    },
  ];

  return <div>
    <div className="page-header">
      <div>
        <h1 className="page-title">Bloqueos de inventario</h1>
        <p className="page-subtitle">Daño, cuarentena e incidencias sin alterar el stock físico</p>
      </div>
      <button className="btn btn-secondary" onClick={cargar}><RefreshCw size={13}/> Actualizar</button>
    </div>

    {error && <AlertBanner variant="danger" title="Bloqueos de inventario" message={error} onClose={() => setError('')} />}
    {mensaje && <AlertBanner variant="success" title="Operación completada" message={mensaje} onClose={() => setMensaje('')} />}

    <AlertBanner
      variant="info"
      title="Regla de disponibilidad"
      message="Bloquear no reduce el stock físico: aumenta CantidadBloqueada y reduce Disponible. Desbloquear devuelve esas unidades a disponibilidad. Las unidades reservadas no pueden bloquearse."
    />

    <div style={{display:'grid',gridTemplateColumns:'repeat(3,minmax(0,1fr))',gap:12,marginBottom:16}}>
      <div className="card" style={{padding:14}}><div style={{fontSize:11,color:'var(--text-muted)'}}>Físico total</div><div style={{fontSize:22,fontWeight:700}}>{totalFisico}</div></div>
      <div className="card" style={{padding:14}}><div style={{fontSize:11,color:'var(--text-muted)'}}>Bloqueado</div><div style={{fontSize:22,fontWeight:700}}>{totalBloqueado}</div></div>
      <div className="card" style={{padding:14}}><div style={{fontSize:11,color:'var(--text-muted)'}}>Disponible</div><div style={{fontSize:22,fontWeight:700}}>{totalDisponible}</div></div>
    </div>

    <DataTable<StockTransferenciaWms>
      title="Saldos por ubicación"
      columns={columns}
      data={stocks}
      loading={loading}
      rowKey={r => r.id}
      pageSize={10}
      emptyMessage="No hay saldos físicos registrados"
    />

    <Modal
      title={accion === 'bloquear' ? 'Bloquear unidades' : 'Desbloquear unidades'}
      open={modal}
      onClose={() => !saving && setModal(false)}
      footer={<><button className="btn btn-secondary" onClick={() => setModal(false)} disabled={saving}>Cancelar</button><button className="btn btn-primary" onClick={guardar} disabled={saving}>{saving ? 'Guardando...' : accion === 'bloquear' ? 'Bloquear' : 'Desbloquear'}</button></>}
    >
      <div style={{display:'grid',gap:12}}>
        <div className="form-group">
          <span className="form-label">Saldo seleccionado</span>
          <div className="form-input" style={{background:'var(--bg-secondary)'}}>
            {stockSeleccionado ? `${stockSeleccionado.sku} · ${stockSeleccionado.ubicacionCodigo}` : '—'}
          </div>
        </div>
        {stockSeleccionado && <div style={{fontSize:12,color:'var(--text-muted)'}}>
          Físico {stockSeleccionado.cantidadTotal} · Reservado {stockSeleccionado.cantidadReservada} · Bloqueado {stockSeleccionado.cantidadBloqueada} · Disponible {stockSeleccionado.cantidadDisponible}
        </div>}
        <label className="form-group">
          <span className="form-label">Cantidad</span>
          <input className="form-input" type="number" min="1" step="1" value={cantidad} onChange={e => setCantidad(e.target.value)} />
        </label>
        <label className="form-group">
          <span className="form-label">Motivo / incidencia</span>
          <textarea className="form-input" rows={3} value={motivo} onChange={e => setMotivo(e.target.value)} placeholder={accion === 'bloquear' ? 'Ej. Daño de empaque / cuarentena / inspección' : 'Ej. Inspección aprobada; unidades liberadas'} />
        </label>
      </div>
    </Modal>
  </div>;
}
