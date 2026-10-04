/**
 * ERP desarrollado por Marcelo Zepeda
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
      // Demo
      const pasillos = ['A', 'B', 'C', 'D'];
      const demo: UbicacionAlmacen[] = Array.from({ length: 40 }, (_, i) => {
        const pasillo = pasillos[i % 4];
        const rack = String.fromCharCode(65 + Math.floor(i / 4) % 5);
        const nivel = (i % 3) + 1;
        const ocup = Math.floor(Math.random() * 100);
        return {
          id: crypto.randomUUID(),
          codigo: `${pasillo}-${rack}${nivel.toString().padStart(2,'0')}`,
          pasillo,
          rack,
          nivel: String(nivel),
          posicion: String(i % 5 + 1),
          capacidadMaxima: 100,
          ocupacionActual: ocup,
          disponible: ocup < 80,
          porcentajeOcupacion: ocup,
        };
      });
      setUbicaciones(filterPasillo ? demo.filter(u => u.pasillo === filterPasillo) : demo);
      setResumen({
        totalUbicaciones: 40,
        ubicacionesOcupadas: 28,
        ubicacionesDisponibles: 12,
        porcentajeOcupacionTotal: 70,
        pasillos: ['A', 'B', 'C', 'D'],
        alertasCapacidad: ['Pasillo B al 92% de capacidad'],
      });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [filterPasillo, soloDisp]);

  const columns = [
    { key: 'codigo', header: 'Código', className: 'td-mono td-primary' },
    { key: 'pasillo', header: 'Pasillo', render: (r: UbicacionAlmacen) => <span className="badge badge-default">{r.pasillo}</span> },
    { key: 'rack', header: 'Rack' },
    { key: 'nivel', header: 'Nivel' },
    { key: 'ocupacionActual', header: '% Ocup.', render: (r: UbicacionAlmacen) => {
      const color = r.porcentajeOcupacion >= 90 ? 'var(--red)' : r.porcentajeOcupacion >= 70 ? 'var(--yellow)' : 'var(--green)';
      return (
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <div style={{ width: 60, height: 4, background: 'var(--bg-muted)', borderRadius: 2, overflow: 'hidden' }}>
            <div style={{ width: `${r.porcentajeOcupacion}%`, height: '100%', background: color, borderRadius: 2 }} />
          </div>
          <span style={{ fontSize: 11, color, fontWeight: 600 }}>{r.porcentajeOcupacion}%</span>
        </div>
      );
    }},
    { key: 'disponible', header: 'Estado', render: (r: UbicacionAlmacen) => (
      <span className={`badge ${r.disponible ? 'badge-entregada' : 'badge-cancelada'}`}>
        {r.disponible ? 'Disponible' : 'Ocupado'}
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
              { label: 'Ocupadas', value: resumen.ubicacionesOcupadas, icon: <MapPin size={18} /> },
              { label: 'Disponibles', value: resumen.ubicacionesDisponibles, icon: <MapPin size={18} /> },
              {
                label: '% Ocupación',
                value: `${resumen.porcentajeOcupacionTotal}%`,
                icon: <MapPin size={18} />,
                delta: resumen.porcentajeOcupacionTotal >= 90 ? 'Crítico' : resumen.porcentajeOcupacionTotal >= 70 ? 'Moderado' : 'Óptimo',
                deltaType: (resumen.porcentajeOcupacionTotal >= 90 ? 'down' : resumen.porcentajeOcupacionTotal >= 70 ? 'neutral' : 'up') as 'down' | 'neutral' | 'up',
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

          {resumen.alertasCapacidad.length > 0 && (
            <AlertBanner
              variant="warning"
              title="Capacidad de Almacenamiento en Riesgo"
              message={resumen.alertasCapacidad.join(' · ')}
            />
          )}
        </>
      )}

      {/* Filtros */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 12 }}>
        <select className="form-input" value={filterPasillo} onChange={e => setFilterPasillo(e.target.value)} style={{ width: 'auto', fontSize: 12 }}>
          <option value="">Todos los pasillos</option>
          {(resumen?.pasillos ?? ['A','B','C','D']).map(p => <option key={p} value={p}>Pasillo {p}</option>)}
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
            <button className="btn btn-primary">Confirmar reubicación</button>
          </>
        }
      >
        <div className="form-group">
          <label className="form-label">ID Stock Físico</label>
          <input className="form-input" placeholder="UUID del stock a reubicar..." />
        </div>
        <div className="form-group">
          <label className="form-label">Nueva Ubicación</label>
          <select className="form-input">
            <option value="">Seleccionar ubicación...</option>
            {ubicaciones.filter(u => u.disponible).map(u => (
              <option key={u.id} value={u.id}>{u.codigo} — {u.porcentajeOcupacion}% ocupado</option>
            ))}
          </select>
        </div>
      </Modal>
    </div>
  );
}
