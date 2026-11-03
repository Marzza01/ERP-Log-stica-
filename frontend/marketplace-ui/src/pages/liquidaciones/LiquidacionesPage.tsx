/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState, useCallback, useRef } from 'react';
import { Wallet, CheckCircle2, RefreshCw, AlertCircle, Play } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import { useToast } from '../../context/ToastContext';
import { useAuth } from '../../context/AuthContext';
import { liquidacionesApi } from '../../api/liquidaciones';
import { ApiError } from '../../api/client';
import type { Liquidacion } from '../../types';

const UNCERTAIN_PREFIX = 'erp_liquidacion_intento_incierto_v1:';

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

function isLiquidacion(value: unknown): value is Liquidacion {
  if (typeof value !== 'object' || value === null) return false;
  const row = value as Record<string, unknown>;
  return typeof row.id === 'string' && typeof row.vendedorId === 'string'
    && typeof row.periodo === 'string'
    && typeof row.estado === 'string'
    && ['Pendiente', 'EnProceso', 'Completada', 'Fallida'].includes(row.estado)
    && typeof row.fechaCreacion === 'string'
    && ['montoVentas', 'comisionMarketplace', 'montoLiquidar']
      .every(field => typeof row[field] === 'number' && Number.isFinite(row[field]));
}

export default function LiquidacionesPage() {
  const { usuario } = useAuth();
  const isAdmin = usuario?.rol === 'Admin';
  const [activeTab, setActiveTab] = useState<'pendientes' | 'historial'>('pendientes');
  const [pendientes, setPendientes] = useState<Liquidacion[]>([]);
  const [historial, setHistorial] = useState<Liquidacion[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [dataReady, setDataReady] = useState(false);

  // Modal procesar
  const [selectedLiq, setSelectedLiq] = useState<Liquidacion | null>(null);
  const [processing, setProcessing] = useState(false);
  const processingRef = useRef(false);
  const loadRequestId = useRef(0);
  const [uncertainIds, setUncertainIds] = useState<Set<string> | null>(readUncertainIds);

  useEffect(() => {
    const syncLocks = (event: StorageEvent) => {
      if (event.key === null || event.key.startsWith(UNCERTAIN_PREFIX)) {
        setUncertainIds(readUncertainIds());
      }
    };
    window.addEventListener('storage', syncLocks);
    return () => window.removeEventListener('storage', syncLocks);
  }, []);

  const loadData = useCallback(async () => {
    const requestId = ++loadRequestId.current;
    setLoading(true);
    setLoadError(null);
    setDataReady(false);
    setSelectedLiq(null);
    setPendientes([]);
    setHistorial([]);
    try {
      const [pendRes, histRes] = await Promise.all([
        liquidacionesApi.pendientes(),
        liquidacionesApi.historial(),
      ]);
      if (!Array.isArray(pendRes) || !pendRes.every(isLiquidacion)
        || !Array.isArray(histRes) || !histRes.every(isLiquidacion)) {
        throw new Error('Respuesta de liquidaciones no válida.');
      }
      if (requestId !== loadRequestId.current) return;
      setPendientes(pendRes);
      setHistorial(histRes);
      setDataReady(true);
    } catch {
      if (requestId !== loadRequestId.current) return;
      setLoadError('No se pudieron consultar las liquidaciones. Comprueba la conexión y vuelve a intentar.');
    } finally {
      if (requestId === loadRequestId.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const toast = useToast();

  const handleProcesarPago = async () => {
    if (!isAdmin || !selectedLiq || selectedLiq.estado !== 'Pendiente'
      || processingRef.current || !dataReady || loading || uncertainIds === null) return;
    const id = selectedLiq.id;
    const currentLocks = readUncertainIds();
    if (currentLocks === null || currentLocks.has(id)) {
      setUncertainIds(currentLocks);
      toast.warning('Operación bloqueada', 'No se puede confirmar que este registro sea seguro para procesar.');
      return;
    }
    try {
      window.localStorage.setItem(`${UNCERTAIN_PREFIX}${id}`, 'incierto');
      const storedLocks = readUncertainIds();
      if (!storedLocks?.has(id)) throw new Error('No se pudo verificar el bloqueo local.');
      setUncertainIds(storedLocks);
    } catch {
      setUncertainIds(null);
      toast.error('Operación bloqueada', 'No se pudo guardar el bloqueo local; no se enviará la solicitud.');
      return;
    }
    processingRef.current = true;
    setProcessing(true);
    try {
      const response = await liquidacionesApi.procesarPago(id);
      if (response?.id === id && response?.estado === 'Completada'
        && response?.asientoGenerado === true) {
        toast.success('Liquidación registrada internamente', 'La API confirmó el estado y el asiento contable. No confirma una transferencia bancaria.');
      } else {
        toast.warning('Resultado no verificable', 'El resultado de esta operación no pudo confirmarse. No se permitirá repetirla desde esta pantalla hasta verificar su estado mediante un procedimiento seguro.');
      }
    } catch (error) {
      if (error instanceof ApiError && error.status >= 400 && error.status < 500) {
        toast.warning('Respuesta HTTP sin confirmación', `La API respondió ${error.status}. El resultado de esta operación no pudo confirmarse y no se permitirá repetirla desde esta pantalla hasta verificar su estado mediante un procedimiento seguro.`);
      } else {
        toast.warning('Resultado incierto', 'El resultado de esta operación no pudo confirmarse. No se permitirá repetirla desde esta pantalla hasta verificar su estado mediante un procedimiento seguro.');
      }
    } finally {
      setSelectedLiq(null);
      await loadData();
      processingRef.current = false;
      setProcessing(false);
    }
  };

  const totalPendienteLiquidar = pendientes.reduce((acc, p) => acc + p.montoLiquidar, 0);
  const totalComisionRetenida = pendientes.reduce((acc, p) => acc + p.comisionMarketplace, 0);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Liquidaciones a Vendedores & Split de Pagos
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Registros internos de liquidación. Esta pantalla no confirma transferencias bancarias.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem' }}>
          <button
            onClick={loadData}
            disabled={loading || processing}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.5rem',
              padding: '0.5rem 0.85rem',
              borderRadius: '8px',
              border: '1px solid var(--border)',
              background: 'var(--bg-surface)',
              color: 'var(--text-primary)',
              cursor: loading || processing ? 'not-allowed' : 'pointer',
            }}
          >
            <RefreshCw size={15} />
            Refrescar
          </button>
        </div>
      </div>

      {loading && <div role="status">Cargando liquidaciones...</div>}
      {loadError && <div role="alert" style={{ color: 'var(--red)' }}>{loadError}</div>}
      {!isAdmin && <div role="status">El registro de liquidaciones solo está disponible para Administrador.</div>}
      {isAdmin && uncertainIds === null && <div role="alert">No se puede verificar el almacenamiento local. El registro de liquidaciones está bloqueado para evitar reintentos inseguros.</div>}
      {isAdmin && uncertainIds !== null && uncertainIds.size > 0 && <div role="status">Los intentos sin resultado confirmado permanecen bloqueados en este navegador hasta una revisión segura.</div>}
      {dataReady && <>

      {/* Hero Payout Balance Banner (Solid Professional Enterprise Style) */}
      <div style={{
        display: 'grid',
        gridTemplateColumns: '1.2fr 1fr',
        gap: '1rem',
        padding: '20px 24px',
        background: 'var(--bg-card)',
        border: '1px solid var(--border)',
        borderRadius: 'var(--radius-lg)'
      }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
            <span style={{ fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.06em', color: 'var(--text-muted)', fontWeight: 600 }}>Monto pendiente en liquidaciones registradas</span>
          </div>
          <div style={{ fontSize: '2rem', fontWeight: 800, color: 'var(--text-primary)', letterSpacing: '-0.03em' }} className="td-num">
            ${totalPendienteLiquidar.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', marginTop: '0.35rem' }}>
            Importe calculado de registros pendientes. No acredita fondos ni una transferencia.
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', borderLeft: '1px solid var(--border)', paddingLeft: '1.25rem', gap: 8 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem' }}>
            <span style={{ color: 'var(--text-muted)' }}>Distribución indicada:</span>
            <strong style={{ color: 'var(--text-primary)' }}>90% Vendedor / 10% Plataforma</strong>
          </div>
          <div style={{ height: 6, background: 'var(--bg-surface)', borderRadius: 9999, overflow: 'hidden', display: 'flex' }}>
            <div style={{ width: '90%', background: 'var(--accent)' }} title="90% Sellers" />
            <div style={{ width: '10%', background: 'var(--green)' }} title="10% Comisión" />
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', color: 'var(--text-secondary)' }}>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--accent)' }} /> Cuota Sellers</span>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><span style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--green)' }} /> Comisión calculada: ${totalComisionRetenida.toLocaleString('es-SV', { minimumFractionDigits: 2 })}</span>
          </div>
        </div>
      </div>

      {/* KPI Cards Strip */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '1rem' }}>
        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Liquidaciones pendientes</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--yellow)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${totalPendienteLiquidar.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Monto registrado para vendedores</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Comisión calculada Marketplace</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--green)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${totalComisionRetenida.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Importe de los registros pendientes</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Liquidaciones Pendientes</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem' }}>
            {pendientes.length} lotes
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--accent)', marginTop: '0.25rem' }}>Pendientes de registro contable</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total registrado en historial</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${historial.reduce((a, b) => a + b.montoLiquidar, 0).toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>No equivale a pagos bancarios</div>
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: '0.5rem', borderBottom: '1px solid var(--border)', paddingBottom: '0.5rem' }}>
        <button
          onClick={() => setActiveTab('pendientes')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'pendientes' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'pendientes' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'pendientes' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <Wallet size={16} />
          Liquidaciones Pendientes ({pendientes.length})
        </button>

        <button
          onClick={() => setActiveTab('historial')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'historial' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'historial' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'historial' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <CheckCircle2 size={16} />
          Historial de liquidaciones ({historial.length})
        </button>
      </div>

      {/* Tab: Pendientes */}
      {activeTab === 'pendientes' && (
        <DataTable
          columns={[
            {
              header: 'ID Liquidación',
              accessor: 'id',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.id}</span>,
            },
            {
              header: 'Período',
              accessor: 'periodo',
              render: (row) => <span style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{row.periodo}</span>,
            },
            {
              header: 'Ventas Totales ($)',
              accessor: 'montoVentas',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--text-primary)' }}>
                  ${row.montoVentas.toFixed(2)}
                </span>
              ),
            },
            {
              header: 'Comisión 10% ($)',
              accessor: 'comisionMarketplace',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--yellow)' }}>
                  -${row.comisionMarketplace.toFixed(2)}
                </span>
              ),
            },
            {
              header: 'Monto de liquidación ($)',
              accessor: 'montoLiquidar',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--green)', fontSize: '0.95rem' }}>
                  ${row.montoLiquidar.toFixed(2)}
                </span>
              ),
            },
            {
              header: 'Estado',
              accessor: 'estado',
              render: (row) => <StatusBadge status={row.estado} />,
            },
            ...(isAdmin ? [{
              header: 'Registro interno',
              accessor: 'id',
              render: (row: Liquidacion) => (
                uncertainIds === null || uncertainIds.has(row.id) ? (
                  <span style={{ color: 'var(--text-muted)' }}>Bloqueado: resultado por verificar</span>
                ) : (
                <button
                  onClick={() => { if (!processingRef.current && dataReady && !loading && row.estado === 'Pendiente') setSelectedLiq(row); }}
                  disabled={processing || loading || row.estado !== 'Pendiente'}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.35rem',
                    padding: '0.35rem 0.8rem',
                    borderRadius: '6px',
                    border: '1px solid var(--accent)',
                    background: 'var(--accent-glow)',
                    color: 'var(--accent)',
                    fontSize: '0.8rem',
                    fontWeight: 600,
                    cursor: processing || loading || row.estado !== 'Pendiente' ? 'not-allowed' : 'pointer',
                  }}
                >
                  <Play size={13} />
                  Registrar liquidación
                </button>
                )
              ),
            }] : []),
          ]}
          data={pendientes}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay liquidaciones pendientes registradas."
        />
      )}

      {/* Tab: Historial */}
      {activeTab === 'historial' && (
        <DataTable
          columns={[
            {
              header: 'ID Liquidación',
              accessor: 'id',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--green)' }}>{row.id}</span>,
            },
            {
              header: 'Período',
              accessor: 'periodo',
              render: (row) => <span style={{ fontWeight: 600 }}>{row.periodo}</span>,
            },
            {
              header: 'Ventas Totales',
              accessor: 'montoVentas',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)' }}>${row.montoVentas.toFixed(2)}</span>,
            },
            {
              header: 'Comisión 10%',
              accessor: 'comisionMarketplace',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--yellow)' }}>${row.comisionMarketplace.toFixed(2)}</span>,
            },
            {
              header: 'Monto registrado',
              accessor: 'montoLiquidar',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--green)' }}>${row.montoLiquidar.toFixed(2)}</span>,
            },
            {
              header: 'Fecha de liquidación',
              accessor: 'fechaProcesado',
              render: (row) => <span style={{ fontSize: '0.85rem' }}>{row.fechaProcesado ? new Date(row.fechaProcesado).toLocaleString('es-SV') : '—'}</span>,
            },
            {
              header: 'Estado',
              accessor: 'estado',
              render: (row) => <StatusBadge status={row.estado} />,
            },
          ]}
          data={historial}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay liquidaciones en el historial."
        />
      )}

      {/* Confirmación de registro interno */}
      {isAdmin && selectedLiq && (
        <Modal
          title={`Registrar liquidación — ${selectedLiq.id}`}
          isOpen={true}
          onClose={() => { if (!processingRef.current) setSelectedLiq(null); }}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div style={{ background: 'var(--bg-surface)', padding: '0.75rem 1rem', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <div style={{ fontSize: '0.85rem', color: 'var(--text-secondary)' }}>Período de Liquidación:</div>
              <div style={{ fontWeight: 600, color: 'var(--text-primary)', marginTop: '0.2rem' }}>{selectedLiq.periodo}</div>
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', background: 'var(--bg-card)', padding: '1rem', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.9rem' }}>
                <span style={{ color: 'var(--text-secondary)' }}>Ventas Brutas Entregadas:</span>
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600 }}>${selectedLiq.montoVentas.toFixed(2)}</span>
              </div>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.9rem', color: 'var(--yellow)' }}>
                <span>Retención Comisión Marketplace (10%):</span>
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600 }}>-${selectedLiq.comisionMarketplace.toFixed(2)}</span>
              </div>
              <div style={{ height: '1px', background: 'var(--border)', margin: '0.25rem 0' }} />
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '1.1rem', fontWeight: 700, color: 'var(--green)' }}>
                <span>Monto de liquidación:</span>
                <span style={{ fontFamily: 'var(--font-mono)' }}>${selectedLiq.montoLiquidar.toFixed(2)}</span>
              </div>
            </div>

            <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
              <AlertCircle size={14} />
              La API registra la liquidación y un asiento contable interno. No ejecuta ni verifica una transferencia bancaria.
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setSelectedLiq(null)}
                disabled={processing}
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
                type="button"
                onClick={handleProcesarPago}
                disabled={processing || uncertainIds === null || uncertainIds.has(selectedLiq.id)}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: processing || uncertainIds === null || uncertainIds.has(selectedLiq.id) ? 'not-allowed' : 'pointer',
                }}
              >
                {processing ? 'Registrando...' : 'Registrar liquidación interna'}
              </button>
            </div>
          </div>
        </Modal>
      )}
      </>}
    </div>
  );
}
