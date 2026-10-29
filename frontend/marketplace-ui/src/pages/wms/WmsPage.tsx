/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useMemo, useState } from 'react';
import { Boxes, MapPin, MoveRight, RefreshCw } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { wmsApi } from '../../api/wms';
import type { ResumenBodega, StockTransferenciaWms, UbicacionAlmacen } from '../../types';

export default function WmsPage() {
  const [resumen, setResumen] = useState<ResumenBodega | null>(null);
  const [ubicaciones, setUbicaciones] = useState<UbicacionAlmacen[]>([]);
  const [stocks, setStocks] = useState<StockTransferenciaWms[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorCarga, setErrorCarga] = useState('');
  const [filterPasillo, setFilterPasillo] = useState('');
  const [soloDisp, setSoloDisp] = useState(false);

  const [transferModal, setTransferModal] = useState(false);
  const [stockFisicoId, setStockFisicoId] = useState('');
  const [nuevaUbicacionId, setNuevaUbicacionId] = useState('');
  const [cantidadTransferir, setCantidadTransferir] = useState('');
  const [observaciones, setObservaciones] = useState('');
  const [operacionId, setOperacionId] = useState('');
  const [transfiriendo, setTransfiriendo] = useState(false);
  const [errorTransferencia, setErrorTransferencia] = useState('');
  const [mensajeTransferencia, setMensajeTransferencia] = useState('');

  const load = async () => {
    setLoading(true);
    setErrorCarga('');
    try {
      const [r, u, s] = await Promise.all([
        wmsApi.getMapaBodega(),
        wmsApi.listarUbicaciones(),
        wmsApi.listarStocks(),
      ]);
      setResumen(r);
      setUbicaciones(u);
      setStocks(s);
    } catch (error) {
      setResumen(null);
      setUbicaciones([]);
      setStocks([]);
      setErrorCarga(error instanceof Error ? error.message : 'No se pudo cargar el WMS desde la API.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, []);

  const ubicacionesVisibles = useMemo(() => {
    return ubicaciones.filter(u => {
      if (filterPasillo && u.pasillo !== filterPasillo) return false;
      if (soloDisp && u.estaOcupada) return false;
      return true;
    });
  }, [ubicaciones, filterPasillo, soloDisp]);

  const stockSeleccionado = useMemo(
    () => stocks.find(s => s.id === stockFisicoId) ?? null,
    [stocks, stockFisicoId]
  );

  const abrirTransferencia = () => {
    setStockFisicoId('');
    setNuevaUbicacionId('');
    setCantidadTransferir('');
    setObservaciones('');
    setOperacionId(crypto.randomUUID());
    setErrorTransferencia('');
    setTransferModal(true);
  };

  const cerrarTransferencia = () => {
    if (transfiriendo) return;
    setTransferModal(false);
    setErrorTransferencia('');
  };

  const handleTransferir = async () => {
    const cantidad = Number(cantidadTransferir);

    if (!stockSeleccionado) {
      setErrorTransferencia('Debes seleccionar un saldo de stock de origen.');
      return;
    }
    if (!nuevaUbicacionId) {
      setErrorTransferencia('Debes seleccionar la ubicación de destino.');
      return;
    }
    if (!Number.isInteger(cantidad) || cantidad <= 0) {
      setErrorTransferencia('La cantidad debe ser un entero mayor que cero.');
      return;
    }
    if (cantidad > stockSeleccionado.cantidadDisponible) {
      setErrorTransferencia(`Solo hay ${stockSeleccionado.cantidadDisponible} unidades disponibles para transferir.`);
      return;
    }
    if (stockSeleccionado.ubicacionAlmacenId === nuevaUbicacionId) {
      setErrorTransferencia('La ubicación de destino debe ser distinta a la de origen.');
      return;
    }

    setTransfiriendo(true);
    setErrorTransferencia('');
    try {
      const resultado = await wmsApi.transferir({
        stockFisicoId: stockSeleccionado.id,
        nuevaUbicacionId,
        cantidad,
        operacionId,
        observaciones: observaciones.trim() || undefined,
      });

      setMensajeTransferencia(
        resultado.yaProcesada
          ? 'La transferencia ya había sido procesada; no se duplicó inventario.'
          : `Transferencia completada: ${resultado.cantidadTransferida} unidades.`
      );
      setTransferModal(false);
      await load();
    } catch (error) {
      setErrorTransferencia(error instanceof Error ? error.message : 'No se pudo transferir el stock.');
    } finally {
      setTransfiriendo(false);
    }
  };

  const columns = [
    { key: 'codigo', header: 'Código', className: 'td-mono td-primary' },
    { key: 'pasillo', header: 'Pasillo', render: (r: UbicacionAlmacen) => <span className="badge badge-default">{r.pasillo}</span> },
    { key: 'rack', header: 'Rack' },
    { key: 'nivel', header: 'Nivel' },
    { key: 'zona', header: 'Zona' },
    { key: 'capacidadMaxima', header: 'Capacidad' },
    { key: 'estaOcupada', header: 'Estado', render: (r: UbicacionAlmacen) => (
      <span className={`badge ${!r.estaOcupada ? 'badge-entregada' : 'badge-cancelada'}`}>
        {!r.estaOcupada ? 'Disponible' : 'Ocupado'}
      </span>
    )},
  ];

  const destinosDisponibles = ubicaciones.filter(u => u.id !== stockSeleccionado?.ubicacionAlmacenId);
  const stocksTransferibles = stocks.filter(s => s.cantidadDisponible > 0 && s.ubicacionAlmacenId);

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">WMS — Bodega LOGIMKT</h1>
          <p className="page-subtitle">Mapa de almacén y transferencias reales entre ubicaciones</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-secondary" onClick={abrirTransferencia}>
            <MoveRight size={13} /> Transferir Stock
          </button>
          <button className="btn btn-secondary" onClick={load}>
            <RefreshCw size={13} /> Actualizar
          </button>
        </div>
      </div>

      {errorCarga && (
        <AlertBanner variant="danger" title="WMS no disponible" message={errorCarga} action={{ label: 'Reintentar', onClick: load }} />
      )}

      {mensajeTransferencia && (
        <AlertBanner
          variant="success"
          title="Transferencia WMS completada"
          message={mensajeTransferencia}
          onClose={() => setMensajeTransferencia('')}
        />
      )}

      {resumen && (
        <>
          <div className="stat-grid" style={{ marginBottom: 16 }}>
            {[
              { label: 'Total Ubicaciones', value: resumen.totalUbicaciones, icon: <MapPin size={18} /> },
              { label: 'Ocupadas', value: resumen.ocupadas, icon: <Boxes size={18} /> },
              { label: 'Disponibles', value: resumen.disponibles, icon: <MapPin size={18} /> },
              {
                label: '% Ocupación',
                value: `${resumen.porcentajeOcupacion}%`,
                icon: <MapPin size={18} />,
                delta: resumen.porcentajeOcupacion >= 90 ? 'Crítico' : resumen.porcentajeOcupacion >= 70 ? 'Moderado' : 'Óptimo',
                deltaType: (resumen.porcentajeOcupacion >= 90 ? 'down' : resumen.porcentajeOcupacion >= 70 ? 'neutral' : 'up') as 'down' | 'neutral' | 'up',
              },
            ].map(s => (
              <div className="stat-card" key={s.label}>
                <div className="stat-icon-wrap">{s.icon}</div>
                <div className="stat-info">
                  <div className="stat-header">
                    <span className="stat-label">{s.label}</span>
                    {s.delta && <span className={`stat-delta ${s.deltaType}`}>{s.delta}</span>}
                  </div>
                  <div className="stat-value">{s.value}</div>
                </div>
              </div>
            ))}
          </div>

          {resumen.porcentajeOcupacion >= 90 && (
            <AlertBanner
              variant="warning"
              title="Capacidad de Almacenamiento en Riesgo"
              message={`La bodega registra ${resumen.porcentajeOcupacion}% de ocupación.`}
            />
          )}
        </>
      )}

      <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
        <select className="form-input" value={filterPasillo} onChange={e => setFilterPasillo(e.target.value)} style={{ width: 'auto', fontSize: 12 }}>
          <option value="">Todos los pasillos</option>
          {(resumen?.pasillos ?? []).map(p => <option key={p.pasillo} value={p.pasillo}>Pasillo {p.pasillo}</option>)}
        </select>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--text-secondary)', cursor: 'pointer' }}>
          <input type="checkbox" checked={soloDisp} onChange={e => setSoloDisp(e.target.checked)} />
          Solo posiciones vacías
        </label>
      </div>

      <DataTable<UbicacionAlmacen>
        title="Ubicaciones de Almacén"
        columns={columns}
        data={ubicacionesVisibles}
        loading={loading}
        rowKey={r => r.id}
        pageSize={15}
        emptyMessage="No hay ubicaciones con estos filtros"
      />

      <Modal
        title="Transferir Stock entre Ubicaciones"
        open={transferModal}
        onClose={cerrarTransferencia}
        footer={
          <>
            <button className="btn btn-secondary" onClick={cerrarTransferencia} disabled={transfiriendo}>Cancelar</button>
            <button className="btn btn-primary" onClick={handleTransferir} disabled={transfiriendo}>
              {transfiriendo ? 'Transfiriendo...' : 'Confirmar transferencia'}
            </button>
          </>
        }
      >
        <div className="form-group">
          <label className="form-label">Stock de origen</label>
          <select
            className="form-input"
            value={stockFisicoId}
            onChange={e => {
              setStockFisicoId(e.target.value);
              setCantidadTransferir('');
              setNuevaUbicacionId('');
              setErrorTransferencia('');
            }}
          >
            <option value="">Seleccionar saldo...</option>
            {stocksTransferibles.map(stock => (
              <option key={stock.id} value={stock.id}>
                {stock.sku} — {stock.ubicacionCodigo} — Disponible: {stock.cantidadDisponible}
              </option>
            ))}
          </select>
        </div>

        {stockSeleccionado && (
          <div style={{ marginBottom: 12, padding: 10, border: '1px solid var(--border)', borderRadius: 8, fontSize: 12 }}>
            <strong>{stockSeleccionado.productoNombre}</strong>
            <div>SKU: {stockSeleccionado.sku}</div>
            <div>Origen: {stockSeleccionado.ubicacionCodigo}</div>
            <div>
              Físico: {stockSeleccionado.cantidadTotal} · Reservado: {stockSeleccionado.cantidadReservada} · Bloqueado: {stockSeleccionado.cantidadBloqueada} · Disponible: {stockSeleccionado.cantidadDisponible}
            </div>
          </div>
        )}

        <div className="form-group">
          <label className="form-label">Cantidad a transferir</label>
          <input
            type="number"
            min={1}
            max={stockSeleccionado?.cantidadDisponible ?? undefined}
            className="form-input"
            value={cantidadTransferir}
            onChange={e => setCantidadTransferir(e.target.value)}
            placeholder={stockSeleccionado ? `Máximo ${stockSeleccionado.cantidadDisponible}` : 'Selecciona primero el stock'}
            disabled={!stockSeleccionado}
          />
        </div>

        <div className="form-group">
          <label className="form-label">Ubicación de destino</label>
          <select
            className="form-input"
            value={nuevaUbicacionId}
            onChange={e => setNuevaUbicacionId(e.target.value)}
            disabled={!stockSeleccionado}
          >
            <option value="">Seleccionar ubicación...</option>
            {destinosDisponibles.map(u => (
              <option key={u.id} value={u.id}>
                {u.codigo} — {u.zona} — Capacidad {u.capacidadMaxima}
              </option>
            ))}
          </select>
        </div>

        <div className="form-group">
          <label className="form-label">Observaciones (opcional)</label>
          <textarea
            className="form-input"
            value={observaciones}
            onChange={e => setObservaciones(e.target.value)}
            rows={3}
            placeholder="Motivo operativo de la transferencia..."
          />
        </div>

        {errorTransferencia && (
          <AlertBanner variant="danger" title="No se pudo transferir" message={errorTransferencia} />
        )}
      </Modal>
    </div>
  );
}
