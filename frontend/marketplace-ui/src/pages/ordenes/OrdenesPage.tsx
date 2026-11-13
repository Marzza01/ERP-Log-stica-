/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { ChevronRight } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import NuevaOrdenEvaluacion from './NuevaOrdenEvaluacion';
import { apiClient } from '../../api/client';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import { useToast } from '../../context/ToastContext';
import { useAuth } from '../../context/AuthContext';
import { ordenesApi } from '../../api/ordenes';
import type { EstadoOrden } from '../../types';

const ESTADOS: EstadoOrden[] = ['Pendiente', 'Aprobada', 'EnProceso', 'Enviada', 'Entregada', 'Cancelada'];
const TRANSICIONES: Partial<Record<EstadoOrden, EstadoOrden[]>> = {
  Pendiente: ['Aprobada', 'Cancelada'],
  Aprobada: ['EnProceso', 'Cancelada'],
  EnProceso: ['Enviada', 'Cancelada'],
  Enviada: ['Entregada'],
};
const UNCERTAIN_PREFIX = 'erp_orden_estado_incierto_v1:';

interface OrdenVista {
  id: string;
  numeroOrden: string;
  estado: EstadoOrden;
  tipoCliente: 'Empresa' | 'ConsumidorFinal';
  montoTotal: number;
  transportista: string;
  guiaEnvio?: string;
  fechaCreacion: string;
  totalItems: number;
  subTotal?: number;
  impuestoIva?: number;
  costoEnvio?: number;
  zonaEnvio?: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseEstado(value: unknown): EstadoOrden | null {
  if (typeof value === 'string' && ESTADOS.includes(value as EstadoOrden)) return value as EstadoOrden;
  if (typeof value === 'number' && Number.isInteger(value) && value >= 0 && value < ESTADOS.length) return ESTADOS[value];
  return null;
}

function parseTipoCliente(value: unknown): OrdenVista['tipoCliente'] | null {
  if (value === 'B2B' || value === 1) return 'Empresa';
  if (value === 'B2C' || value === 0) return 'ConsumidorFinal';
  return null;
}

function parseOrden(value: unknown, detalle = false): OrdenVista | null {
  if (!isRecord(value)) return null;
  const estado = parseEstado(value.estado);
  const tipoCliente = parseTipoCliente(value.tipoCliente);
  if (typeof value.id !== 'string' || typeof value.numeroOrden !== 'string'
    || !estado || !tipoCliente || typeof value.montoTotal !== 'number'
    || !Number.isFinite(value.montoTotal) || typeof value.transportista !== 'string'
    || typeof value.fechaCreacion !== 'string' || Number.isNaN(Date.parse(value.fechaCreacion))) return null;

  if (detalle && (typeof value.subTotal !== 'number' || !Number.isFinite(value.subTotal)
    || typeof value.impuestoIva !== 'number' || !Number.isFinite(value.impuestoIva)
    || typeof value.costoEnvio !== 'number' || !Number.isFinite(value.costoEnvio))) return null;

  const totalItems = typeof value.totalItems === 'number' ? value.totalItems
    : Array.isArray(value.items) ? value.items.length : 0;
  return {
    id: value.id,
    numeroOrden: value.numeroOrden,
    estado,
    tipoCliente,
    montoTotal: value.montoTotal,
    transportista: value.transportista,
    guiaEnvio: typeof value.guiaEnvio === 'string' ? value.guiaEnvio : undefined,
    fechaCreacion: value.fechaCreacion,
    totalItems,
    subTotal: typeof value.subTotal === 'number' ? value.subTotal : undefined,
    impuestoIva: typeof value.impuestoIva === 'number' ? value.impuestoIva : undefined,
    costoEnvio: typeof value.costoEnvio === 'number' ? value.costoEnvio : undefined,
    zonaEnvio: typeof value.zonaEnvio === 'string' ? value.zonaEnvio : undefined,
  };
}

function readUncertainIds(): Set<string> | null {
  try {
    const ids = new Set<string>();
    for (let index = 0; index < window.localStorage.length; index++) {
      const key = window.localStorage.key(index);
      if (!key?.startsWith(UNCERTAIN_PREFIX)) continue;
      if (window.localStorage.getItem(key) !== 'incierto') return null;
      ids.add(key.slice(UNCERTAIN_PREFIX.length));
    }
    return ids;
  } catch {
    return null;
  }
}

const fmt = (date: string) => new Date(date).toLocaleDateString('es-SV', { day: '2-digit', month: 'short', year: '2-digit' });
const fmtMoney = (amount: number) => '$' + amount.toLocaleString('es-SV', { minimumFractionDigits: 2 });

export default function OrdenesPage() {
  const { usuario } = useAuth();
  const isAdmin = usuario?.rol === 'Admin';
  const toast = useToast();
  const [ordenes, setOrdenes] = useState<OrdenVista[]>([]);
  const [loading, setLoading] = useState(isAdmin);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [dataReady, setDataReady] = useState(false);
  const [filterEstado, setFilterEstado] = useState('');
  const [selectedOrden, setSelectedOrden] = useState<OrdenVista | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [transModal, setTransModal] = useState(false);
  const [nuevoEstado, setNuevoEstado] = useState<EstadoOrden | ''>('');
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
  const loadRequestId = useRef(0);
  const [uncertainIds, setUncertainIds] = useState<Set<string> | null>(readUncertainIds);
  const [isCompact, setIsCompact] = useState(false);

  useEffect(() => {
    const syncLocks = (event: StorageEvent) => {
      if (event.key === null || event.key.startsWith(UNCERTAIN_PREFIX)) {
        setUncertainIds(readUncertainIds());
      }
    };
    window.addEventListener('storage', syncLocks);
    return () => window.removeEventListener('storage', syncLocks);
  }, []);

  const load = useCallback(async () => {
    const requestId = ++loadRequestId.current;
    setSelectedOrden(null);
    setTransModal(false);
    setOrdenes([]);
    setDataReady(false);
    setLoadError(null);
    if (!isAdmin) {
      setLoading(false);
      return;
    }
    setLoading(true);
    try {
      const response = await ordenesApi.listar({ estado: filterEstado || undefined, tamano: 100 });
      if (!response || !Array.isArray(response.ordenes)) throw new Error('Respuesta de órdenes no válida.');
      const parsed = response.ordenes.map(row => parseOrden(row));
      if (parsed.some(row => row === null)) throw new Error('Respuesta de órdenes no válida.');
      if (requestId !== loadRequestId.current) return;
      setOrdenes(parsed as OrdenVista[]);
      setDataReady(true);
    } catch {
      if (requestId !== loadRequestId.current) return;
      setLoadError('No se pudieron consultar las órdenes. Comprueba la conexión e inténtalo de nuevo.');
    } finally {
      if (requestId === loadRequestId.current) setLoading(false);
    }
  }, [filterEstado, isAdmin]);

  useEffect(() => { void load(); }, [load]);

  const abrirDetalle = async (orden: OrdenVista) => {
    if (!isAdmin || savingRef.current) return;
    setSelectedOrden(orden);
    setDetailLoading(true);
    setDetailError(null);
    try {
      const response = await ordenesApi.obtener(orden.id);
      const detail = parseOrden(response, true);
      if (!detail || detail.id !== orden.id) throw new Error('Detalle de orden no válido.');
      setSelectedOrden(detail);
    } catch {
      setDetailError('No se pudo confirmar el detalle actualizado de esta orden.');
    } finally {
      setDetailLoading(false);
    }
  };

  const cancelarEvaluacion = async (id: string) => {
    if (savingRef.current || !isAdmin) return;
    savingRef.current = true; setSaving(true);
    try {
      const result = await apiClient.post<{ id: string; estado: string }>(`/api/ordenes/evaluacion/${id}/cancelar`);
      if (result.id !== id || result.estado !== 'Cancelada') throw new Error('Resultado no confirmado.');
      toast.success('Orden cancelada', 'Se liberaron todas las reservas sin realizar pagos.');
      await load();
    } catch {
      toast.error('Cancelación sin confirmar', 'Reintenta esta misma cancelación para consultar o completar su resultado.');
    } finally { savingRef.current = false; setSaving(false); }
  };

  const abrirTransicion = (orden: OrdenVista) => {
    if (!isAdmin || savingRef.current || uncertainIds === null || uncertainIds.has(orden.id)) return;
    const siguientes = TRANSICIONES[orden.estado];
    if (!siguientes?.length) return;
    setSelectedOrden(orden);
    setNuevoEstado(siguientes[0]);
    setTransModal(true);
  };

  const handleTransicion = async () => {
    if (!isAdmin || !selectedOrden || !nuevoEstado || savingRef.current
      || uncertainIds === null || uncertainIds.has(selectedOrden.id)) return;
    savingRef.current = true;
    setSaving(true);
    const id = selectedOrden.id;
    const estadoAnterior = selectedOrden.estado;
    const target = nuevoEstado;
    let enviada = false;
    try {
      const response = await ordenesApi.obtener(id);
      const actual = parseOrden(response, true);
      if (!actual || actual.id !== id) {
        toast.warning('Estado no verificable', 'No se envió el cambio: la lectura actual no confirmó la orden.');
        return;
      }
      if (actual.estado !== estadoAnterior || !TRANSICIONES[actual.estado]?.includes(target)) {
        toast.warning('Estado cambiado', 'No se envió la transición. Se actualizará el listado con el estado de la API.');
        return;
      }

      const locks = readUncertainIds();
      if (locks === null || locks.has(id)) {
        setUncertainIds(locks);
        toast.warning('Operación bloqueada', 'No se pudo verificar que esta orden admita otra transición.');
        return;
      }
      try {
        window.localStorage.setItem(UNCERTAIN_PREFIX + id, 'incierto');
        const stored = readUncertainIds();
        if (!stored?.has(id)) throw new Error('No se pudo verificar la marca local.');
        setUncertainIds(stored);
      } catch {
        setUncertainIds(null);
        toast.error('Operación bloqueada', 'No se pudo guardar la marca local. No se envió el cambio.');
        return;
      }

      enviada = true;
      const resultado = await ordenesApi.transicionarEstado(id, target);
      if (isRecord(resultado) && resultado.id === id
        && parseEstado(resultado.estadoAnterior) === estadoAnterior
        && parseEstado(resultado.nuevoEstado) === target) {
        toast.success('Estado confirmado por la API', 'La orden ' + actual.numeroOrden + ' pasó a ' + target + '.');
      } else {
        toast.warning('Resultado incierto', 'La respuesta no confirmó el cambio. No se permitirá repetir esta transición desde esta pantalla.');
      }
    } catch {
      if (enviada) {
        toast.warning('Resultado incierto', 'No se pudo confirmar el resultado. No se permitirá repetir esta transición desde esta pantalla.');
      } else {
        toast.error('No se pudo leer el estado actual', 'No se envió ningún cambio de estado.');
      }
    } finally {
      setSelectedOrden(null);
      setTransModal(false);
      await load();
      savingRef.current = false;
      setSaving(false);
    }
  };

  const totalVisible = ordenes.reduce((sum, orden) => sum + orden.montoTotal, 0);
  const b2b = ordenes.filter(orden => orden.tipoCliente === 'Empresa');
  const b2c = ordenes.filter(orden => orden.tipoCliente === 'ConsumidorFinal');
  const columns = [
    { key: 'numeroOrden', header: 'Nº Orden', className: 'td-mono td-primary' },
    { key: 'estado', header: 'Estado', render: (row: OrdenVista) => <StatusBadge status={row.estado} /> },
    { key: 'tipoCliente', header: 'Canal', render: (row: OrdenVista) => row.tipoCliente === 'Empresa' ? 'B2B Empresa' : 'B2C Retail' },
    { key: 'montoTotal', header: 'Total', className: 'td-right', render: (row: OrdenVista) => <span className="td-primary td-num">{fmtMoney(row.montoTotal)}</span> },
    { key: 'transportista', header: 'Transportista' },
    { key: 'guiaEnvio', header: 'Guía', render: (row: OrdenVista) => row.guiaEnvio || '—' },
    { key: 'fechaCreacion', header: 'Fecha', render: (row: OrdenVista) => fmt(row.fechaCreacion) },
    { key: 'acciones', header: '', sortable: false, render: (row: OrdenVista) => {
      if (row.numeroOrden.startsWith('TEST-')) return <button className="btn btn-secondary btn-sm"
        onClick={event => { event.stopPropagation(); void abrirDetalle(row); }}>Detalle</button>;
      if (!TRANSICIONES[row.estado]?.length) return null;
      if (uncertainIds === null || uncertainIds.has(row.id)) return <span>Transición bloqueada: resultado por verificar</span>;
      return (
        <button className="btn btn-secondary btn-sm" disabled={saving}
          onClick={event => { event.stopPropagation(); abrirTransicion(row); }}>
          <ChevronRight size={11} /> Avanzar
        </button>
      );
    } },
  ];

  return (
    <div>
      <div className="page-header" style={{ alignItems: 'flex-start' }}>
        <div>
          <h1 className="page-title">Órdenes (OMS)</h1>
          <p className="page-subtitle">Órdenes confirmadas por la API. Los importes visibles no acreditan un cobro.</p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <button className={'btn btn-secondary btn-sm' + (isCompact ? ' active' : '')}
            onClick={() => setIsCompact(!isCompact)} title="Alternar densidad visual de la tabla">
            {isCompact ? 'Vista Normal' : 'Vista Compacta'}
          </button>
          <select className="form-input" value={filterEstado}
            disabled={!isAdmin || loading || saving}
            onChange={event => setFilterEstado(event.target.value)}
            style={{ width: 'auto', fontSize: 12 }}>
            <option value="">Todos los estados</option>
            {ESTADOS.map(estado => <option key={estado} value={estado}>{estado}</option>)}
          </select>
          {isAdmin && usuario && <NuevaOrdenEvaluacion actorId={usuario.email} onCreated={() => void load()} />}
        </div>
      </div>

      {!isAdmin && <div role="status">El listado y los cambios de órdenes de esta pantalla solo están disponibles para Administrador.</div>}
      {isAdmin && loading && <div role="status">Cargando órdenes...</div>}
      {isAdmin && loadError && <div role="alert">{loadError}</div>}
      {isAdmin && uncertainIds === null && <div role="alert">No se puede verificar el almacenamiento local. Las transiciones están bloqueadas para evitar reintentos inseguros.</div>}
      {isAdmin && uncertainIds !== null && uncertainIds.size > 0 && <div role="status">Las órdenes con intentos de transición previos permanecen bloqueadas en este navegador hasta una revisión segura.</div>}

      {isAdmin && dataReady && (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 12, marginBottom: 16 }}>
            <div className="stat-card"><div className="stat-info"><div className="stat-label">B2B visibles</div><div className="stat-value">{b2b.length} órdenes</div></div></div>
            <div className="stat-card"><div className="stat-info"><div className="stat-label">B2C visibles</div><div className="stat-value">{b2c.length} órdenes</div></div></div>
            <div className="stat-card"><div className="stat-info"><div className="stat-label">Total de órdenes visibles</div><div className="stat-value">{fmtMoney(totalVisible)}</div></div></div>
          </div>
          <DataTable<OrdenVista> title="Listado de Órdenes" columns={columns} data={ordenes}
            rowKey={row => row.id} pageSize={15} compact={isCompact}
            emptyMessage="No hay órdenes confirmadas en este estado"
            onRowClick={row => { void abrirDetalle(row); }} />
        </>
      )}

      <Modal title={'Orden ' + (selectedOrden?.numeroOrden ?? '')}
        open={!!selectedOrden && !transModal} onClose={() => { if (!detailLoading) setSelectedOrden(null); }} size="md">
        {detailLoading && <div role="status">Cargando detalle actualizado...</div>}
        {detailError && <div role="alert">{detailError}</div>}
        {selectedOrden && !detailLoading && !detailError && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            <div className="grid-2">
              {([
                ['Estado', <StatusBadge status={selectedOrden.estado} />],
                ['Tipo Cliente', selectedOrden.tipoCliente],
                ['Subtotal', selectedOrden.subTotal === undefined ? '—' : fmtMoney(selectedOrden.subTotal)],
                ['IVA 13%', selectedOrden.impuestoIva === undefined ? '—' : fmtMoney(selectedOrden.impuestoIva)],
                ['Costo envío', selectedOrden.costoEnvio === undefined ? '—' : fmtMoney(selectedOrden.costoEnvio)],
                ['Total', fmtMoney(selectedOrden.montoTotal)],
                ['Transportista', selectedOrden.transportista],
                ['Guía', selectedOrden.guiaEnvio || '—'],
                ['Zona', selectedOrden.zonaEnvio || '—'],
                ['Fecha', fmt(selectedOrden.fechaCreacion)],
              ] as [string, React.ReactNode][]).map(([label, value]) => (
                <div key={label} style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{label}</span>
                  <span style={{ fontSize: 13 }}>{value}</span>
                </div>
              ))}
            </div>
            {selectedOrden.numeroOrden.startsWith('TEST-') && selectedOrden.estado === 'Pendiente' && (
              <button className="btn btn-secondary" disabled={saving} onClick={() => void cancelarEvaluacion(selectedOrden.id)}>
                Cancelar evaluación y liberar reservas
              </button>
            )}
            {!selectedOrden.numeroOrden.startsWith('TEST-') && !!TRANSICIONES[selectedOrden.estado]?.length && (
              <button className="btn btn-primary"
                disabled={uncertainIds === null || uncertainIds.has(selectedOrden.id)}
                onClick={() => abrirTransicion(selectedOrden)}>
                <ChevronRight size={13} /> Avanzar estado
              </button>
            )}
          </div>
        )}
      </Modal>

      <Modal title="Cambiar estado de orden" open={transModal}
        onClose={() => { if (!savingRef.current) { setTransModal(false); setSelectedOrden(null); } }}
        footer={<>
          <button className="btn btn-secondary" disabled={saving}
            onClick={() => { setTransModal(false); setSelectedOrden(null); }}>Cancelar</button>
          <button className="btn btn-primary" onClick={handleTransicion}
            disabled={saving || !nuevoEstado || uncertainIds === null
              || (selectedOrden !== null && uncertainIds.has(selectedOrden.id))}>
            {saving ? 'Guardando...' : 'Confirmar cambio a ' + (nuevoEstado || '...')}
          </button>
        </>}>
        <p>Orden: <strong>{selectedOrden?.numeroOrden}</strong></p>
        <p>Estado mostrado: <StatusBadge status={selectedOrden?.estado ?? ''} /></p>
        <p>Se consultará el estado actual antes de enviar el cambio. Si el resultado es incierto, se bloquearán nuevas transiciones de esta orden en este navegador.</p>
        <div className="form-group">
          <label className="form-label">Nuevo estado</label>
          <select className="form-input" value={nuevoEstado} disabled={saving}
            onChange={event => setNuevoEstado(event.target.value as EstadoOrden)}>
            {(selectedOrden ? TRANSICIONES[selectedOrden.estado] ?? [] : []).map(estado =>
              <option key={estado} value={estado}>{estado}</option>)}
          </select>
        </div>
      </Modal>
    </div>
  );
}
