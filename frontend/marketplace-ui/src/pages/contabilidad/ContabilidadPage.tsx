/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect, useState, useCallback } from 'react';
import { BookOpen, Scale, CheckCircle2, MinusCircle, FileSpreadsheet, RefreshCw, ChevronRight } from 'lucide-react';
import DataTable from '../../components/ui/DataTable';
import Modal from '../../components/ui/Modal';
import { contabilidadApi, type BalanceComprobacion } from '../../api/contabilidad';
import type { PartidaContable } from '../../types';

export default function ContabilidadPage() {
  const [activeTab, setActiveTab] = useState<'diario' | 'balance'>('diario');
  const [partidas, setPartidas] = useState<PartidaContable[]>([]);
  const [balance, setBalance] = useState<BalanceComprobacion | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorCarga, setErrorCarga] = useState<string | null>(null);
  const [selectedPartida, setSelectedPartida] = useState<PartidaContable | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setErrorCarga(null);
    setPartidas([]);
    setBalance(null);
    try {
      const [diarioRes, balanceRes] = await Promise.all([
        contabilidadApi.libroDiario(),
        contabilidadApi.balanceComprobacion(),
      ]);
      setPartidas(diarioRes.partidas || []);
      setBalance(balanceRes);
    } catch (error: unknown) {
      setPartidas([]);
      setBalance(null);
      setErrorCarga(error instanceof Error ? error.message : 'No se pudo consultar la información contable.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const totalDebeGlobal = partidas.reduce((acc, p) => acc + p.totalDebe, 0);
  const totalHaberGlobal = partidas.reduce((acc, p) => acc + p.totalHaber, 0);
  const estaCuadrado = !loading && !errorCarga && partidas.length > 0 && Math.abs(totalDebeGlobal - totalHaberGlobal) < 0.01;
  const estadoCuadre = loading ? 'Cargando...' : partidas.length === 0 ? 'Sin movimientos' : estaCuadrado ? '100% Cuadrado' : 'Discrepante';
  const colorCuadre = loading || partidas.length === 0 ? 'var(--text-muted)' : estaCuadrado ? 'var(--green)' : 'var(--red)';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
      {/* Header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <div>
          <h1 style={{ fontSize: '1.6rem', fontWeight: 700, color: 'var(--text-primary)', margin: 0 }}>
            Contabilidad General & Libro Diario
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.875rem', marginTop: '0.25rem' }}>
            Generación automática de asientos por partida doble: ventas OMS, compras 3-Way Match y liquidaciones bancarias.
          </p>
        </div>
        <div style={{ display: 'flex', gap: '0.75rem' }}>
          <button
            onClick={loadData}
            disabled={loading}
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

      {errorCarga && <div role="alert" style={{ padding: '1rem', border: '1px solid var(--red)', borderRadius: 8, color: 'var(--red)' }}>No se pudo cargar la contabilidad desde la API: {errorCarga}. Los datos contables no están disponibles. Utiliza Refrescar para reintentar.</div>}
      {/* KPI Cards Strip */}
      {!errorCarga && <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '1rem' }}>
        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total Asientos Registrados</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem' }}>
            {partidas.length} partidas
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--accent)', marginTop: '0.25rem' }}>Libro Diario Vigente</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total Debe Acumulado</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${totalDebeGlobal.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Cargos contables</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Total Haber Acumulado</div>
          <div style={{ fontSize: '1.5rem', fontWeight: 700, color: 'var(--text-primary)', marginTop: '0.35rem', fontFamily: 'var(--font-mono)' }}>
            ${totalHaberGlobal.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Abonos contables</div>
        </div>

        <div style={{ background: 'var(--bg-card)', border: '1px solid var(--border)', borderRadius: '10px', padding: '1rem' }}>
          <div style={{ fontSize: '0.8rem', color: 'var(--text-secondary)', fontWeight: 500 }}>Partida Doble Cuadrada</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.4rem', marginTop: '0.35rem' }}>
            {partidas.length === 0 || loading ? <MinusCircle size={22} style={{ color: colorCuadre }} /> : <CheckCircle2 size={22} style={{ color: colorCuadre }} />}
            <span style={{ fontSize: '1.3rem', fontWeight: 700, color: colorCuadre }}>
              {estadoCuadre}
            </span>
          </div>
          <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginTop: '0.25rem' }}>Debe == Haber</div>
        </div>
      </div>

      }
      {/* Tabs */}
      <div style={{ display: 'flex', gap: '0.5rem', borderBottom: '1px solid var(--border)', paddingBottom: '0.5rem' }}>
        <button
          onClick={() => setActiveTab('diario')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'diario' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'diario' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'diario' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <BookOpen size={16} />
          Libro Diario ({partidas.length})
        </button>

        <button
          onClick={() => setActiveTab('balance')}
          style={{
            padding: '0.5rem 1.2rem',
            borderRadius: '6px',
            border: 'none',
            background: activeTab === 'balance' ? 'var(--accent-glow)' : 'transparent',
            color: activeTab === 'balance' ? 'var(--accent)' : 'var(--text-secondary)',
            fontWeight: activeTab === 'balance' ? 600 : 500,
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
          }}
        >
          <Scale size={16} />
          Balance de Comprobación ({balance?.cuentas.length ?? 0} Cuentas)
        </button>
      </div>

      {/* Tab: Diario */}
      {!errorCarga && activeTab === 'diario' && (
        <DataTable
          columns={[
            {
              header: 'N° Asiento',
              accessor: 'numeroAsiento',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.numeroAsiento}</span>,
            },
            {
              header: 'Fecha',
              accessor: 'fecha',
              render: (row) => <span style={{ fontSize: '0.85rem' }}>{new Date(row.fecha).toLocaleString('es-SV')}</span>,
            },
            {
              header: 'Descripción / Concepto',
              accessor: 'descripcion',
              render: (row) => (
                <div>
                  <div style={{ fontWeight: 500, color: 'var(--text-primary)' }}>{row.descripcion}</div>
                  <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>Ref: {row.referencia}</div>
                </div>
              ),
            },
            {
              header: 'Origen',
              accessor: 'tipoOrigen',
              render: (row) => (
                <span style={{ padding: '0.2rem 0.5rem', borderRadius: '6px', background: 'var(--bg-muted)', color: 'var(--text-primary)', fontSize: '0.75rem', fontWeight: 600 }}>
                  {row.tipoOrigen}
                </span>
              ),
            },
            {
              header: 'Total Debe',
              accessor: 'totalDebe',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--text-primary)' }}>
                  ${row.totalDebe.toFixed(2)}
                </span>
              ),
            },
            {
              header: 'Total Haber',
              accessor: 'totalHaber',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--text-primary)' }}>
                  ${row.totalHaber.toFixed(2)}
                </span>
              ),
            },
            {
              header: 'Detalle',
              accessor: 'id',
              render: (row) => (
                <button
                  onClick={() => setSelectedPartida(row)}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '0.3rem',
                    padding: '0.35rem 0.7rem',
                    borderRadius: '6px',
                    border: '1px solid var(--border)',
                    background: 'var(--bg-surface)',
                    color: 'var(--text-primary)',
                    fontSize: '0.8rem',
                    cursor: 'pointer',
                  }}
                >
                  Ver Cuentas
                  <ChevronRight size={13} />
                </button>
              ),
            },
          ]}
          data={partidas}
          keyExtractor={(item) => item.id}
          loading={loading}
          emptyText="No hay asientos registrados en el Libro Diario."
        />
      )}

      {/* Tab: Balance de Comprobación */}
      {!errorCarga && activeTab === 'balance' && (
        <DataTable
          columns={[
            {
              header: 'Código Cuenta',
              accessor: 'cuenta',
              render: (row) => <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--accent)' }}>{row.cuenta}</span>,
            },
            {
              header: 'Nombre de la Cuenta',
              accessor: 'nombreCuenta',
              render: (row) => <span style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{row.nombreCuenta}</span>,
            },
            {
              header: 'Saldo Deudor ($)',
              accessor: 'saldoDeudor',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', color: row.saldoDeudor > 0 ? 'var(--text-primary)' : 'var(--text-muted)' }}>
                  ${row.saldoDeudor.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
                </span>
              ),
            },
            {
              header: 'Saldo Acreedor ($)',
              accessor: 'saldoAcreedor',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', color: row.saldoAcreedor > 0 ? 'var(--text-primary)' : 'var(--text-muted)' }}>
                  ${row.saldoAcreedor.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
                </span>
              ),
            },
            {
              header: 'Saldo Neto ($)',
              accessor: 'saldoNeto',
              render: (row) => (
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 700, color: row.saldoNeto >= 0 ? 'var(--green)' : 'var(--red)' }}>
                  ${row.saldoNeto.toLocaleString('es-SV', { minimumFractionDigits: 2 })}
                </span>
              ),
            },
          ]}
          data={balance?.cuentas || []}
          keyExtractor={(item) => item.cuenta}
          loading={loading}
          emptyText="No hay cuentas disponibles en el balance de comprobación."
        />
      )}

      {/* Modal Detalle Asiento */}
      {selectedPartida && (
        <Modal
          title={`Detalle de Asiento Contable — ${selectedPartida.numeroAsiento}`}
          isOpen={true}
          onClose={() => setSelectedPartida(null)}
        >
          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
            <div style={{ background: 'var(--bg-surface)', padding: '0.75rem 1rem', borderRadius: '8px', border: '1px solid var(--border)' }}>
              <div style={{ fontWeight: 600, color: 'var(--text-primary)' }}>{selectedPartida.descripcion}</div>
              <div style={{ display: 'flex', gap: '1rem', marginTop: '0.35rem', fontSize: '0.8rem', color: 'var(--text-secondary)' }}>
                <span>Fecha: {new Date(selectedPartida.fecha).toLocaleString('es-SV')}</span>
                <span>Referencia: <strong style={{ color: 'var(--accent)' }}>{selectedPartida.referencia}</strong></span>
                <span>Origen: {selectedPartida.tipoOrigen}</span>
              </div>
            </div>

            {/* Tabla Partida Doble */}
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }}>
              <thead>
                <tr style={{ borderBottom: '1px solid var(--border)', textAlign: 'left', color: 'var(--text-secondary)' }}>
                  <th style={{ padding: '0.5rem' }}>Cuenta</th>
                  <th style={{ padding: '0.5rem' }}>Descripción de la Cuenta</th>
                  <th style={{ padding: '0.5rem', textAlign: 'right' }}>Debe ($)</th>
                  <th style={{ padding: '0.5rem', textAlign: 'right' }}>Haber ($)</th>
                </tr>
              </thead>
              <tbody>
                {selectedPartida.lineas.map(l => (
                  <tr key={l.id} style={{ borderBottom: '1px solid var(--border-muted)' }}>
                    <td style={{ padding: '0.5rem', fontFamily: 'var(--font-mono)', color: 'var(--accent)', fontWeight: 600 }}>{l.cuenta}</td>
                    <td style={{ padding: '0.5rem', color: 'var(--text-primary)' }}>{l.descripcion}</td>
                    <td style={{ padding: '0.5rem', textAlign: 'right', fontFamily: 'var(--font-mono)', color: l.debe > 0 ? 'var(--text-primary)' : 'var(--text-muted)' }}>
                      {l.debe > 0 ? `$${l.debe.toFixed(2)}` : '—'}
                    </td>
                    <td style={{ padding: '0.5rem', textAlign: 'right', fontFamily: 'var(--font-mono)', color: l.haber > 0 ? 'var(--text-primary)' : 'var(--text-muted)' }}>
                      {l.haber > 0 ? `$${l.haber.toFixed(2)}` : '—'}
                    </td>
                  </tr>
                ))}
                <tr style={{ borderTop: '2px solid var(--border)', fontWeight: 700 }}>
                  <td colSpan={2} style={{ padding: '0.6rem 0.5rem', textAlign: 'right' }}>TOTALES CUADRADOS:</td>
                  <td style={{ padding: '0.6rem 0.5rem', textAlign: 'right', fontFamily: 'var(--font-mono)', color: 'var(--green)' }}>
                    ${selectedPartida.totalDebe.toFixed(2)}
                  </td>
                  <td style={{ padding: '0.6rem 0.5rem', textAlign: 'right', fontFamily: 'var(--font-mono)', color: 'var(--green)' }}>
                    ${selectedPartida.totalHaber.toFixed(2)}
                  </td>
                </tr>
              </tbody>
            </table>

            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: '0.5rem' }}>
              <button
                type="button"
                onClick={() => setSelectedPartida(null)}
                style={{
                  padding: '0.5rem 1.25rem',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--accent)',
                  color: '#fff',
                  fontWeight: 600,
                  cursor: 'pointer',
                }}
              >
                Cerrar
              </button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}
