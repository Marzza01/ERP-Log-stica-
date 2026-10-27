/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState, useCallback } from 'react';
import { RotateCcw, AlertTriangle, CheckCircle2, XCircle, Plus, RefreshCw, Eye } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import AlertBanner from '../../components/ui/AlertBanner';
import { rmaApi, type RegistrarRmaRequest, type DictaminarRmaRequest } from '../../api/rma';
import { wmsApi } from '../../api/wms';
import type { DevolucionRMA, UbicacionAlmacen } from '../../types';

export default function RmaPage() {
  const [rmas, setRmas] = useState<DevolucionRMA[]>([]);
  const [loading, setLoading] = useState(true);
  const [errorCarga, setErrorCarga] = useState('');
  const [filterEstado, setFilterEstado] = useState('');

  // Modal registrar
  const [modalRegistrar, setModalRegistrar] = useState(false);
  const [formRegistrar, setFormRegistrar] = useState<RegistrarRmaRequest>({
    ordenId: '',
    productoId: '',
    cantidad: 1,
    motivoCliente: '',
  });
  const [savingReg, setSavingReg] = useState(false);

  // Modal inspeccionar
  const [selectedRma, setSelectedRma] = useState<DevolucionRMA | null>(null);
  const [formDictamen, setFormDictamen] = useState<DictaminarRmaRequest>({
    clasificacion: 'ReStock',
    inspector: 'Operador Bodega',
    dictamenTecnico: 'Producto apto para reingreso a inventario.',
    descuentoRemate: 0,
    ubicacionDestinoId: '',
    operacionId: crypto.randomUUID(),
  });
  const [savingDictamen, setSavingDictamen] = useState(false);
  const [ubicaciones, setUbicaciones] = useState<UbicacionAlmacen[]>([]);
  const [loadingUbicaciones, setLoadingUbicaciones] = useState(false);
  const [errorDictamen, setErrorDictamen] = useState('');

  const loadData = useCallback(async () => {
    setLoading(true);
    setErrorCarga('');
    try {
      const res = await rmaApi.listar(filterEstado || undefined);
      setRmas(res || []);
    } catch (error) {
      setRmas([]);
      setErrorCarga(error instanceof Error ? error.message : 'No se pudieron cargar las devoluciones RMA desde la API.');
    } finally {
      setLoading(false);
    }
  }, [filterEstado]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  useEffect(() => {
    if (!selectedRma) return;

    let activo = true;
    setLoadingUbicaciones(true);
    wmsApi.listarUbicaciones()
      .then(items => {
        if (activo) setUbicaciones(items || []);
      })
      .catch((error) => {
        if (activo) {
          setUbicaciones([]);
          setErrorDictamen(error instanceof Error ? error.message : 'No se pudieron cargar las ubicaciones WMS.');
        }
      })
      .finally(() => {
        if (activo) setLoadingUbicaciones(false);
      });

    return () => { activo = false; };
  }, [selectedRma]);

  const abrirInspeccion = (rma: DevolucionRMA) => {
    setSelectedRma(rma);
    setErrorDictamen('');
    setFormDictamen({
      clasificacion: rma.clasificacion === 'Pendiente' ? 'ReStock' : rma.clasificacion,
      inspector: rma.inspectorTecnico || 'Operador Bodega',
      dictamenTecnico: rma.dictamenTecnico || 'Inspección física en bodega sin anomalías graves.',
      descuentoRemate: rma.porcentajeRemateDescuento || 0,
      ubicacionDestinoId: '',
      operacionId: crypto.randomUUID(),
    });
  };

  const handleCrearRMA = async () => {
    setSavingReg(true);
    setErrorCarga('');
    try {
      const creado = await rmaApi.solicitar(formRegistrar);
      setRmas(prev => [creado, ...prev]);
      setModalRegistrar(false);
    } catch (error) {
      setErrorCarga(error instanceof Error ? error.message : 'No fue posible registrar la solicitud RMA.');
    } finally {
      setSavingReg(false);
    }
  };

  const handleInspeccionar = async () => {
    if (!selectedRma) return;
    if (formDictamen.clasificacion === 'ReStock' && !formDictamen.ubicacionDestinoId) {
      setErrorDictamen('Debes seleccionar una ubicación WMS para reingresar el producto al inventario.');
      return;
    }

    setSavingDictamen(true);
    setErrorDictamen('');
    try {
      const actualizado = await rmaApi.inspeccionar(selectedRma.id, formDictamen);
      setRmas(prev => prev.map(r => r.id === actualizado.id ? actualizado : r));
      setSelectedRma(null);
    } catch (error) {
      setErrorDictamen(error instanceof Error ? error.message : 'No fue posible procesar el dictamen RMA.');
    } finally {
      setSavingDictamen(false);
    }
  };


  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Logística Inversa & Devoluciones (RMA)
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Flujo de inspección técnica en bodega: Re-stock, Merma (scrap) o Remate y autorización de reembolsos.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem' }}>
          <button
            onClick={loadData}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              padding: '0.5rem 0.85rem',
              borderRadius: '8px',
              border: '1px solid var(--border)',
              background: 'var(--bg-surface)',
              color: 'var(--text-primary)',
              cursor: 'pointer',
            }}
          >
            <RefreshCw size={15} />
            Refrescar
          </button>
          <button
            onClick={() => setModalRegistrar(true)}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              padding: '0.5rem 1rem',
              borderRadius: '8px',
              border: 'none',
              background: 'var(--accent)',
              color: '#fff',
              fontWeight: 600,
              cursor: 'pointer',
            }}
          >
            <Plus size={16} />
            Nueva Solicitud RMA
          </button>
        </div>
      </div>

      {errorCarga && (
        <AlertBanner variant="danger" title="RMA no disponible" message={errorCarga} action={{ label: 'Reintentar', onClick: loadData }} />
      )}

      {/* KPI Cards */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '1rem' }}>
        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total Devoluciones Registradas</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem' }}>
            {rmas.length} casos
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--accent)', marginTop: '0.25rem' }}>Histórico acumulado</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Pendientes de Inspección</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--yellow)', marginTop: '0.35rem' }}>
            {rmas.filter(r => ['Solicitado', 'RecibidoEnBodega', 'EnInspeccion'].includes(r.estado)).length} casos
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>En bodega de recepción</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Casos Completados</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--green)', marginTop: '0.35rem' }}>
            {rmas.filter(r => r.estado === 'Completado').length} casos
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Finalizados por el flujo RMA</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Reingresos Aprobados</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            {rmas.filter(r => r.clasificacion === 'ReStock').length} casos
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--green)', marginTop: '0.25rem' }}>Clasificados como ReStock</div>
        </div>
      </div>

      {/* Filter */}
      <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
        <button
          onClick={() => setFilterEstado('')}
          style={{
            padding: '0.4rem 0.9rem',
            borderRadius: '6px',
            border: '1px solid var(--border)',
            background: filterEstado === '' ? 'var(--accent-glow)' : 'var(--bg-surface)',
            color: filterEstado === '' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: filterEstado === '' ? 600 : 400,
            cursor: 'pointer',
          }}
        >
          Todas
        </button>
        {['Solicitado', 'RecibidoEnBodega', 'EnInspeccion', 'Dictaminado', 'Completado', 'Rechazado'].map(est => (
          <button
            key={est}
            onClick={() => setFilterEstado(est)}
            style={{
              padding: '0.4rem 0.9rem',
              borderRadius: '6px',
              border: '1px solid var(--border)',
              background: filterEstado === est ? 'var(--accent-glow)' : 'var(--bg-surface)',
              color: filterEstado === est ? 'var(--accent)' : 'var(--text-secondary)',
              fontWeight: filterEstado === est ? 600 : 400,
              cursor: 'pointer',
            }}
          >
            {est}
          </button>
        ))}
      </div>

      {/* Tabla RMA */}
      <DataTable
        columns={[
          {
            header: 'ID RMA',
            accessor: 'id',
            render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.id.slice(0, 8)}...</span>,
          },
          {
            header: 'Orden Vinculada',
            accessor: 'ordenId',
            render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.85rem' }}>{row.ordenId}</span>,
          },
          {
            header: 'Motivo de Devolución',
            accessor: 'motivoCliente',
            render: (row) => (
              <span style={{ padding: '0.2rem 0.5rem', borderRadius: '4px', background: 'var(--bg-muted)', color: 'var(--text-primary)', fontSize: '0.75rem', fontWeight: 600 }}>
                {row.motivoCliente}
              </span>
            ),
          },
          {
            header: 'Producto / Cantidad',
            accessor: 'productoId',
            render: (row) => <span style={{ fontSize: '0.85rem', color: 'var(--text-primary)' }}>{row.productoId.slice(0, 8)}… × {row.cantidad}</span>,
          },
          {
            header: 'Fecha Solicitud',
            accessor: 'fechaSolicitud',
            render: (row) => <span style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>{new Date(row.fechaSolicitud).toLocaleDateString('es-SV')}</span>,
          },
          {
            header: 'Estado',
            accessor: 'estado',
            render: (row) => <StatusBadge status={row.estado} />,
          },
          {
            header: 'Acción',
            accessor: 'id',
            render: (row) => (
              <button
                onClick={() => abrirInspeccion(row)}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.35rem',
                  padding: '0.35rem 0.75rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'var(--bg-surface)',
                  color: 'var(--text-primary)',
                  fontSize: '0.8rem',
                  fontWeight: 500,
                  cursor: 'pointer',
                }}
              >
                <Eye size={13} />
                {['Solicitado', 'RecibidoEnBodega', 'EnInspeccion'].includes(row.estado) ? 'Inspeccionar' : 'Ver Dictamen'}
              </button>
            ),
          },
        ]}
        data={rmas}
        keyExtractor={(item) => item.id}
        loading={loading}
        emptyText="No hay solicitudes de devolución registradas."
        emptyAction={
          <button
            className="btn btn-primary btn-sm"
            onClick={() => setModalRegistrar(true)}
            style={{ margin: '0 auto' }}
          >
            + Registrar primera RMA
          </button>
        }
      />

      {/* Modal Nueva Solicitud RMA */}
      {modalRegistrar && (
        <Modal
          title="Registrar Nueva Solicitud de Devolución (RMA)"
          isOpen={true}
          onClose={() => setModalRegistrar(false)}
        >
          <form
            onSubmit={(e) => {
              e.preventDefault();
              handleCrearRMA();
            }}
            style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}
          >
            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                N° de Orden
              </label>
              <input
                type="text"
                placeholder="UUID de la orden"
                value={formRegistrar.ordenId}
                onChange={e => setFormRegistrar(f => ({ ...f, ordenId: e.target.value }))}
                required
                style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
              />
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Motivo
                </label>
                <select
                  value={formRegistrar.motivoCliente}
                  onChange={e => setFormRegistrar(f => ({ ...f, motivoCliente: e.target.value }))}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                >
                  <option value="ProductoDefectuoso">Producto Defectuoso</option>
                  <option value="DanoTransporte">Daño por Transporte / Transportista</option>
                  <option value="NoCorresponde">No Corresponde a lo Solicitado</option>
                  <option value="ErrorEnvio">Error de Envío en Picking</option>
                </select>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Cantidad
                </label>
                <input
                  type="number"
                  step="0.01"
                  value={formRegistrar.cantidad}
                  onChange={e => setFormRegistrar(f => ({ ...f, cantidad: parseInt(e.target.value) || 1 }))}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                ID de Producto (UUID)
              </label>
              <textarea
                value={formRegistrar.productoId}
                onChange={e => setFormRegistrar(f => ({ ...f, productoId: e.target.value }))}
                rows={3}
                required
                style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
              />
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setModalRegistrar(false)}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                type="submit"
                disabled={savingReg}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: savingReg ? 'not-allowed' : 'pointer',
                }}
              >
                {savingReg ? 'Registrando...' : 'Registrar Devolución'}
              </button>
            </div>
          </form>
        </Modal>
      )}

      {/* Modal Dictamen Técnico */}
      {selectedRma && (
        <Modal
          title={`Inspección Técnica RMA — ${selectedRma.ordenId}`}
          isOpen={true}
          onClose={() => setSelectedRma(null)}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div style={{ background: 'var(--bg-surface)', padding: '0.75rem 1rem', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <div style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>Reclamo del Cliente:</div>
              <div style={{ color: 'var(--text-primary)', fontWeight: 500, marginTop: '0.25rem' }}>{selectedRma.motivoCliente}</div>
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Motivo: <strong>{selectedRma.clasificacion}</strong></div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0.75rem' }}>
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Dictamen Físico de Bodega
                </label>
                <select
                  value={formDictamen.clasificacion}
                  onChange={e => setFormDictamen(f => ({ ...f, clasificacion: e.target.value as any }))}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                >
                  <option value="ReStock">Re-Stock (Regresar a inventario disponible)</option>
                  <option value="MermaScrap">Merma / Scrap (Desecho por daño total)</option>
                  <option value="Remate">Remate (Outlet / Reacondicionado)</option>
                </select>
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Monto Descuento Remate (%)
                </label>
                <input
                  type="number"
                  step="0.01"
                  value={formDictamen.descuentoRemate ?? 0}
                  onChange={e => setFormDictamen(f => ({ ...f, descuentoRemate: parseFloat(e.target.value) || 0 }))}
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            {formDictamen.clasificacion === 'ReStock' && (
              <div>
                <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                  Ubicación WMS de reingreso
                </label>
                <select
                  value={formDictamen.ubicacionDestinoId ?? ''}
                  onChange={e => setFormDictamen(f => ({ ...f, ubicacionDestinoId: e.target.value }))}
                  disabled={loadingUbicaciones || selectedRma.estado === 'Completado'}
                  required
                  style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
                >
                  <option value="">{loadingUbicaciones ? 'Cargando ubicaciones...' : 'Seleccionar ubicación'}</option>
                  {ubicaciones.map(u => (
                    <option key={u.id} value={u.id}>
                      {u.codigo} — {u.zona || 'Bodega'} — capacidad {u.capacidadMaxima}
                    </option>
                  ))}
                </select>
                <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.3rem' }}>
                  El ReStock quedará trazado en esta posición y aumentará únicamente su saldo físico.
                </div>
              </div>
            )}

            <div>
              <label style={{ display: 'block', fontSize: '0.8rem', color: 'var(--text-secondary)', marginBottom: '0.25rem' }}>
                Observaciones Técnicas del Inspector
              </label>
              <textarea
                value={formDictamen.dictamenTecnico}
                onChange={e => setFormDictamen(f => ({ ...f, dictamenTecnico: e.target.value }))}
                rows={3}
                style={{ width: '100%', padding: '0.5rem', borderRadius: '6px', background: 'var(--bg-surface)', border: '1px solid var(--border)', color: 'var(--text-primary)', boxSizing: 'border-box' }}
              />
            </div>

            {errorDictamen && (
              <div style={{ padding: '0.65rem 0.8rem', borderRadius: '6px', background: 'rgba(239, 68, 68, 0.08)', border: '1px solid rgba(239, 68, 68, 0.3)', color: 'var(--red)', fontSize: '0.8rem' }}>
                {errorDictamen}
              </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setSelectedRma(null)}
                style={{
                  padding: '0.5rem 1rem',
                  borderRadius: '6px',
                  border: '1px solid var(--border)',
                  background: 'transparent',
                  color: 'var(--text-secondary)',
                  cursor: 'pointer',
                }}
              >
                Cerrar
              </button>
              {selectedRma.estado !== 'Completado' && (
                <button
                  type="button"
                  onClick={handleInspeccionar}
                  disabled={savingDictamen}
                  style={{
                    padding: '0.5rem 1.25rem',
                    borderRadius: '6px',
                    border: 'none',
                    background: 'var(--green)',
                    color: '#000',
                    fontWeight: 700,
                    cursor: savingDictamen ? 'not-allowed' : 'pointer',
                  }}
                >
                  {savingDictamen ? 'Guardando...' : 'Emitir Dictamen & Autorizar'}
                </button>
              )}
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
