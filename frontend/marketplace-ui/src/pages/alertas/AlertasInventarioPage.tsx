/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, BellRing, CheckCircle2, PackageX, RefreshCw } from 'lucide-react';
import DataTable, { type Column } from '../../components/ui/DataTable';
import AlertBanner from '../../components/ui/AlertBanner';
import { alertasApi, type AlertaInventario, type AlertasInventarioResponse } from '../../api/alertas';
import { useAuth } from '../../context/AuthContext';

export default function AlertasInventarioPage() {
  const { usuario } = useAuth();
  const [data, setData] = useState<AlertasInventarioResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [tipo, setTipo] = useState('ALL');
  const [prioridad, setPrioridad] = useState('ALL');
  const [estado, setEstado] = useState('ALL');
  const [ackId, setAckId] = useState<string | null>(null);

  const cargar = async () => {
    setLoading(true);
    setError('');
    try {
      setData(await alertasApi.listar());
    } catch (err) {
      setData(null);
      setError(err instanceof Error ? err.message : 'No se pudieron cargar las alertas de inventario.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { cargar(); }, []);

  const reconocer = async (row: AlertaInventario) => {
    setAckId(row.alertaId);
    setError('');
    try {
      await alertasApi.reconocer(row.productoId, row.tipo, usuario?.nombre);
      await cargar();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo marcar la alerta como revisada.');
    } finally {
      setAckId(null);
    }
  };

  const items = useMemo(() => (data?.items ?? []).filter(row =>
    (tipo === 'ALL' || row.tipo === tipo) &&
    (prioridad === 'ALL' || row.prioridad === prioridad) &&
    (estado === 'ALL' || row.estado === estado)
  ), [data, tipo, prioridad, estado]);

  const columns: Column<AlertaInventario>[] = [
    {
      key: 'prioridad',
      header: 'Prioridad',
      render: row => (
        <span className={`badge ${row.prioridad === 'CRITICAL' ? 'badge-cancelada' : row.prioridad === 'HIGH' ? 'badge-pendiente' : 'badge-aprobada'}`}>
          {row.prioridad === 'CRITICAL' ? 'Crítica' : row.prioridad === 'HIGH' ? 'Alta' : 'Media'}
        </span>
      ),
    },
    {
      key: 'sku',
      header: 'SKU / Producto',
      render: row => (
        <div>
          <div className="td-mono td-primary">{row.sku}</div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>{row.producto}</div>
        </div>
      ),
    },
    {
      key: 'tipo',
      header: 'Tipo',
      render: row => row.tipo === 'OUT_OF_STOCK' ? 'Sin existencia' : row.tipo === 'CRITICAL_STOCK' ? 'Stock crítico' : 'Stock bajo',
    },
    {
      key: 'cantidadDisponible',
      header: 'Disponible',
      render: row => (
        <div>
          <strong>{row.cantidadDisponible}</strong>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            Físico {row.cantidadFisica} · Res. {row.cantidadReservada} · Bloq. {row.cantidadBloqueada}
          </div>
        </div>
      ),
    },
    {
      key: 'stockMinimo',
      header: 'Mínimo / Reorden',
      render: row => <span>{row.stockMinimo} / {row.puntoReorden}</span>,
    },
    {
      key: 'faltanteAReorden',
      header: 'Faltante',
      render: row => <strong>{row.faltanteAReorden}</strong>,
    },
    {
      key: 'cantidadEnTransito',
      header: 'En tránsito',
      render: row => (
        <div>
          <strong>{row.cantidadEnTransito}</strong>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            {row.ordenesCompra.length ? row.ordenesCompra.join(', ') : 'Sin PO enviada'}
          </div>
        </div>
      ),
    },
    { key: 'ubicaciones', header: 'Ubicación', render: row => row.ubicaciones },
    {
      key: 'recomendacion',
      header: 'Recomendación',
      render: row => <span style={{ fontSize: 11, color: 'var(--text-secondary)' }}>{row.recomendacion}</span>,
      sortable: false,
    },
    {
      key: 'estado',
      header: 'Estado',
      render: row => (
        <div>
          <span className={`badge ${row.estado === 'ACKNOWLEDGED' ? 'badge-entregada' : 'badge-pendiente'}`}>
            {row.estado === 'ACKNOWLEDGED' ? 'Revisada' : 'Pendiente'}
          </span>
          {row.reconocidaPor && (
            <div style={{ fontSize: 10, color: 'var(--text-muted)', marginTop: 3 }}>{row.reconocidaPor}</div>
          )}
        </div>
      ),
    },
    {
      key: 'accion',
      header: 'Acción',
      sortable: false,
      render: row => row.estado === 'ACKNOWLEDGED' ? (
        <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>Sin acción pendiente</span>
      ) : (
        <button className="btn btn-secondary btn-sm" onClick={() => reconocer(row)} disabled={ackId === row.alertaId}>
          <CheckCircle2 size={12} /> Marcar revisada
        </button>
      ),
    },
  ];

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Alertas de inventario</h1>
          <p className="page-subtitle">Priorización automática de SKU con disponibilidad baja, crítica o agotada</p>
        </div>
        <button className="btn btn-secondary" onClick={cargar} disabled={loading}>
          <RefreshCw size={13} /> Actualizar
        </button>
      </div>

      {error && <AlertBanner variant="danger" title="No se pudo completar la operación" message={error} onClose={() => setError('')} />}

      <div className="stat-grid" style={{ marginBottom: 16 }}>
        {[
          { label: 'Total alertas', value: data?.total ?? 0, icon: <BellRing size={18} /> },
          { label: 'Pendientes', value: data?.pendientes ?? 0, icon: <AlertTriangle size={18} /> },
          { label: 'Prioridad crítica', value: data?.prioridadCritica ?? 0, icon: <PackageX size={18} /> },
          { label: 'Prioridad alta', value: data?.prioridadAlta ?? 0, icon: <AlertTriangle size={18} /> },
          { label: 'Sin existencia', value: data?.sinExistencia ?? 0, icon: <PackageX size={18} /> },
          { label: 'Faltante a reorden', value: data?.faltanteAReorden ?? 0, icon: <RefreshCw size={18} /> },
        ].map(card => (
          <div key={card.label} className="stat-card">
            <div className="stat-icon-wrap">{card.icon}</div>
            <div className="stat-info">
              <div className="stat-header"><span className="stat-label">{card.label}</span></div>
              <div className="stat-value">{card.value}</div>
            </div>
          </div>
        ))}
      </div>

      <AlertBanner
        variant="info"
        title="Alertas derivadas del inventario real"
        message="Marcar una alerta como revisada no modifica stock ni resuelve la condición. La alerta desaparece automáticamente cuando la disponibilidad vuelve a una condición normal. El stock en tránsito se toma de PO enviadas o parcialmente recibidas."
      />

      <div className="table-container" style={{ marginBottom: 16 }}>
        <div className="table-toolbar" style={{ flexWrap: 'wrap' }}>
          <span className="table-title">Filtros operativos</span>
          <div className="table-actions" style={{ flexWrap: 'wrap' }}>
            <select value={tipo} onChange={e => setTipo(e.target.value)} className="form-input" style={{ width: 170 }}>
              <option value="ALL">Todos los tipos</option>
              <option value="LOW_STOCK">Stock bajo</option>
              <option value="CRITICAL_STOCK">Stock crítico</option>
              <option value="OUT_OF_STOCK">Sin existencia</option>
            </select>
            <select value={prioridad} onChange={e => setPrioridad(e.target.value)} className="form-input" style={{ width: 160 }}>
              <option value="ALL">Todas las prioridades</option>
              <option value="MEDIUM">Media</option>
              <option value="HIGH">Alta</option>
              <option value="CRITICAL">Crítica</option>
            </select>
            <select value={estado} onChange={e => setEstado(e.target.value)} className="form-input" style={{ width: 150 }}>
              <option value="ALL">Todos los estados</option>
              <option value="OPEN">Pendientes</option>
              <option value="ACKNOWLEDGED">Revisadas</option>
            </select>
            <button className="btn btn-secondary btn-sm" onClick={() => { setTipo('ALL'); setPrioridad('ALL'); setEstado('ALL'); }}>Limpiar</button>
          </div>
        </div>
      </div>

      <DataTable
        title="Alertas activas de inventario"
        columns={columns}
        data={items}
        loading={loading}
        rowKey={row => row.alertaId}
        pageSize={10}
        emptyMessage="No hay alertas activas para los filtros seleccionados"
      />
    </div>
  );
}
