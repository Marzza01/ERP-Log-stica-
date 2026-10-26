/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState } from 'react';
import { MapPin, RefreshCw, MoveRight } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { wmsApi } from '../../api/wms';
import type { UbicacionAlmacen, ResumenBodega } from '../../types';

export default function WmsPage() {
  const [resumen, setResumen] = useState<ResumenBodega | null>(null);
  const [ubicaciones, setUbicaciones] = useState<UbicacionAlmacen[]>([]);
  const [loading, setLoading] = useState(true);
  const [filterPasillo, setFilterPasillo] = useState('');
  const [soloDisp, setSoloDisp] = useState(false);
  const [reubicarModal, setReubicarModal] = useState(false);
  const [stockFisicoId, setStockFisicoId] = useState('');
  const [nuevaUbicacionId, setNuevaUbicacionId] = useState('');
  const [reubicando, setReubicando] = useState(false);
  const [errorReubicacion, setErrorReubicacion] = useState('');

  const load = async () => {
    setLoading(true);
    try {
      const [r, u] = await Promise.all([
        wmsApi.getMapaBodega(),
        wmsApi.listarUbicaciones({ pasillo: filterPasillo || undefined, soloDisponibles: soloDisp || undefined }),
      ]);
      setResumen(r);
      setUbicaciones(u);
    } catch {
      // Demo con el mismo contrato del backend.
      const pasillos = ['A', 'B', 'C', 'D'];
      const demo: UbicacionAlmacen[] = Array.from({ length: 40 }, (_, i) => {
        const pasillo = pasillos[i % 4];
        const nivel = (i % 3) + 1;
        const estaOcupada = i % 3 !== 0;
        return {
          id: crypto.randomUUID(),
          codigo: `PAS-${pasillo}-RCK-${String((i % 5) + 1).padStart(2, '0')}-NIV-${String(nivel).padStart(2, '0')}-POS-${String((i % 5) + 1).padStart(2, '0')}`,
          pasillo,
          rack: `Rack ${String((i % 5) + 1).padStart(2, '0')}`,
          nivel,
          posicion: `Posición ${String((i % 5) + 1).padStart(2, '0')}`,
          capacidadMaxima: 100,
          estaOcupada,
          zona: 'AlmacenamientoGeneral',
          fechaCreacion: new Date().toISOString(),
        };
      });
      const filtradas = filterPasillo ? demo.filter(u => u.pasillo === filterPasillo) : demo;
      const ocupadas = demo.filter(u => u.estaOcupada).length;
      setUbicaciones(soloDisp ? filtradas.filter(u => !u.estaOcupada) : filtradas);
      setResumen({
        totalUbicaciones: demo.length,
        ocupadas,
        disponibles: demo.length - ocupadas,
        porcentajeOcupacion: Math.round((ocupadas / demo.length) * 100),
        pasillos: pasillos.map(pasillo => {
          const ubicacionesPasillo = demo.filter(u => u.pasillo === pasillo);
          return {
            pasillo,
            totalRacks: new Set(ubicacionesPasillo.map(u => u.rack)).size,
            totalPosiciones: ubicacionesPasillo.length,
            posicionesOcupadas: ubicacionesPasillo.filter(u => u.estaOcupada).length,
            ubicaciones: ubicacionesPasillo,
          };
        }),
      });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [filterPasillo, soloDisp]);

  const handleReubicar = async () => {
    if (!stockFisicoId || !nuevaUbicacionId) {
      setErrorReubicacion('Debes indicar el ID del stock y la nueva ubicación.');
      return;
    }

    setReubicando(true);
    setErrorReubicacion('');
    try {
      await wmsApi.reubicar(stockFisicoId, nuevaUbicacionId);
      setReubicarModal(false);
      setStockFisicoId('');
      setNuevaUbicacionId('');
      await load();
    } catch (error) {
      setErrorReubicacion(error instanceof Error ? error.message : 'No se pudo reubicar el stock.');
    } finally {
      setReubicando(false);
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

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">WMS — Bodega LOGIMKT</h1>
          <p className="page-subtitle">Mapa dinámico de almacén con métricas de ocupación</p>
        </div>
        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn btn-secondary" onClick={() => setReubicarModal(true)}>
            <MoveRight size={13} /> Reubicar Stock
          </button>
          <button className="btn btn-secondary" onClick={load}>
            <RefreshCw size={13} /> Actualizar
          </button>
        </div>
      </div>

      {/* Métricas Homogéneas y Neutrales */}
      {resumen && (
        <>
          <div className="stat-grid" style={{ marginBottom: 16 }}>
            {[
              { label: 'Total Ubicaciones', value: resumen.totalUbicaciones, icon: <MapPin size={18} /> },
              { label: 'Ocupadas', value: resumen.ocupadas, icon: <MapPin size={18} /> },
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

      {/* Filtros */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
        <select className="form-input" value={filterPasillo} onChange={e => setFilterPasillo(e.target.value)} style={{ width: 'auto', fontSize: 12 }}>
          <option value="">Todos los pasillos</option>
          {(resumen?.pasillos ?? []).map(p => <option key={p.pasillo} value={p.pasillo}>Pasillo {p.pasillo}</option>)}
        </select>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--text-secondary)', cursor: 'pointer' }}>
          <input type="checkbox" checked={soloDisp} onChange={e => setSoloDisp(e.target.checked)} />
          Solo disponibles
        </label>
      </div>

      <DataTable<UbicacionAlmacen>
        title="Ubicaciones de Almacén"
        columns={columns}
        data={ubicaciones}
        loading={loading}
        rowKey={r => r.id}
        pageSize={15}
        emptyMessage="No hay ubicaciones con estos filtros"
      />

      {/* Modal reubicar */}
      <Modal
        title="Reubicar Stock"
        open={reubicarModal}
        onClose={() => setReubicarModal(false)}
        footer={
          <>
            <button className="btn btn-secondary" onClick={() => setReubicarModal(false)}>Cancelar</button>
            <button className="btn btn-primary" onClick={handleReubicar} disabled={reubicando}>{reubicando ? 'Reubicando...' : 'Confirmar reubicación'}</button>
          </>
        }
      >
        <div className="form-group">
          <label className="form-label">ID Stock Físico</label>
          <input className="form-input" value={stockFisicoId} onChange={e => setStockFisicoId(e.target.value)} placeholder="UUID del stock a reubicar..." />
        </div>
        <div className="form-group">
          <label className="form-label">Nueva Ubicación</label>
          <select className="form-input" value={nuevaUbicacionId} onChange={e => setNuevaUbicacionId(e.target.value)}>
            <option value="">Seleccionar ubicación...</option>
            {ubicaciones.filter(u => !u.estaOcupada).map(u => (
              <option key={u.id} value={u.id}>{u.codigo} — {u.zona}</option>
            ))}
          </select>
        </div>
        {errorReubicacion && <AlertBanner variant="danger" title="No se pudo reubicar" message={errorReubicacion} />}
      </Modal>
    </div>
  );
}
