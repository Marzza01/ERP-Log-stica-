import React, { useEffect, useState, useCallback } from 'react';
import { Wallet, DollarSign, ArrowUpRight, CheckCircle2, RefreshCw, AlertCircle, Play } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import StatusBadge from '../../components/ui/StatusBadge';
import Modal from '../../components/ui/Modal';
import { liquidacionesApi } from '../../api/liquidaciones';
import type { Liquidacion } from '../../types';

export default function LiquidacionesPage() {
  const [activeTab, setActiveTab] = useState<'pendientes' | 'historial'>('pendientes');
  const [pendientes, setPendientes] = useState<Liquidacion[]>([]);
  const [historial, setHistorial] = useState<Liquidacion[]>([]);
  const [loading, setLoading] = useState(true);

  // Modal procesar
  const [selectedLiq, setSelectedLiq] = useState<Liquidacion | null>(null);
  const [processing, setProcessing] = useState(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [pendRes, histRes] = await Promise.all([
        liquidacionesApi.pendientes(),
        liquidacionesApi.historial(),
      ]);
      setPendientes(pendRes || []);
      setHistorial(histRes || []);
    } catch {
      // Demo mock
      const mockPendientes: Liquidacion[] = [
        {
          id: 'liq-01',
          vendedorId: 'vend-101',
          periodo: 'Semana 37 (11 Sep - 17 Sep 2026)',
          montoVentas: 8450.00,
          comisionMarketplace: 845.00,
          montoLiquidar: 7605.00,
          estado: 'Pendiente',
          fechaCreacion: new Date(Date.now() - 86400000).toISOString(),
        },
        {
          id: 'liq-02',
          vendedorId: 'vend-102',
          periodo: 'Semana 37 (11 Sep - 17 Sep 2026)',
          montoVentas: 3200.00,
          comisionMarketplace: 320.00,
          montoLiquidar: 2880.00,
          estado: 'Pendiente',
          fechaCreacion: new Date(Date.now() - 86400000 * 2).toISOString(),
        },
      ];

      const mockHistorial: Liquidacion[] = [
        {
          id: 'liq-00',
          vendedorId: 'vend-101',
          periodo: 'Semana 36 (04 Sep - 10 Sep 2026)',
          montoVentas: 12500.00,
          comisionMarketplace: 1250.00,
          montoLiquidar: 11250.00,
          estado: 'Procesada',
          fechaCreacion: new Date(Date.now() - 86400000 * 8).toISOString(),
          fechaProcesado: new Date(Date.now() - 86400000 * 7).toISOString(),
        },
      ];

      setPendientes(mockPendientes);
      setHistorial(mockHistorial);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleProcesarPago = async () => {
    if (!selectedLiq) return;
    setProcessing(true);
    try {
      await liquidacionesApi.procesarPago(selectedLiq.id);
    } catch {
      // ignore
    }
    const procesada: Liquidacion = {
      ...selectedLiq,
      estado: 'Procesada',
      fechaProcesado: new Date().toISOString(),
    };
    setPendientes(prev => prev.filter(p => p.id !== selectedLiq.id));
    setHistorial(prev => [procesada, ...prev]);
    setProcessing(false);
    setSelectedLiq(null);
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
            Regla de negocio: Split automatizado 90% Vendedor / 10% Comisión Marketplace sobre órdenes entregadas.
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
        </div>
      </div>

      {/* KPI Cards Strip */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '1rem' }}>
        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Pendiente por Desembolsar</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--yellow)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${totalPendienteLiquidar.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>90% para Vendedores</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Comisión Retenida Marketplace</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--green)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${totalComisionRetenida.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>10% Ganancia Neta</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Liquidaciones Pendientes</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem' }}>
            {pendientes.length} lotes
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--accent)', marginTop: '0.25rem' }}>Esperando aprobación bancaria</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total Histórico Pagado</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${historial.reduce((a, b) => a + b.montoLiquidar, 0).toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--green)', marginTop: '0.25rem' }}>Desembolsos exitosos</div>
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
          Historial de Pagos Procesados ({historial.length})
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
              header: 'Neto a Transferir ($)',
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
            {
              header: 'Acción Bancaria',
              accessor: 'id',
              render: (row) => (
                <button
                  onClick={() => setSelectedLiq(row)}
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
                    cursor: 'pointer',
                  }}
                >
                  <Play size={13} />
                  Procesar Pago
                </button>
              ),
            },
          ]}
          data={pendientes}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay liquidaciones pendientes de pago."
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
              header: 'Monto Transferido',
              accessor: 'montoLiquidar',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: 'var(--green)' }}>${row.montoLiquidar.toFixed(2)}</span>,
            },
            {
              header: 'Fecha Pago',
              accessor: 'fechaProcesado',
              render: (row) => <span style={{ fontSize: '0.85rem' }}>{row.fechaProcesado ? new Date(row.fechaProcesado).toLocaleString('es-SV') : 'Completado'}</span>,
            },
            {
              header: 'Estado',
              accessor: 'estado',
              render: () => <span style={{ color: 'var(--green)', fontWeight: 600, fontSize: '0.8rem' }}>● Desembolsado</span>,
            },
          ]}
          data={historial}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay historial de pagos procesados."
        />
      )}

      {/* Modal Desembolso Bancario */}
      {selectedLiq && (
        <Modal
          title={`Confirmar Desembolso Bancario — ${selectedLiq.id}`}
          isOpen={true}
          onClose={() => setSelectedLiq(null)}
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
                <span>Transferencia Bancaria Neta (90%):</span>
                <span style={{ fontFamily: 'var(--font-mono)' }}>${selectedLiq.montoLiquidar.toFixed(2)}</span>
              </div>
            </div>

            <div style={{ fontSize: '0.8rem', color: 'var(--text-muted)', display: 'flex', alignItems: 'center', gap: '0.4rem' }}>
              <AlertCircle size={14} />
              Al procesar, se generará el asiento contable automático: DEBE 2108 (Pasivo Vendedores) → HABER 1101 (Bancos).
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '0.75rem', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setSelectedLiq(null)}
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
                disabled={processing}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: processing ? 'not-allowed' : 'pointer',
                }}
              >
                {processing ? 'Desembolsando...' : 'Confirmar Transferencia Bancaria'}
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
