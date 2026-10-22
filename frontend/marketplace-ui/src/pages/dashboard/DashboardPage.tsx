/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { useEffect, useState } from 'react';
import {
  LineChart, Line, BarChart, Bar, XAxis, YAxis, CartesianGrid,
  Tooltip, ResponsiveContainer, Legend,
} from 'recharts';
import { TrendingUp, ShoppingCart, Package, DollarSign, RefreshCw, ChevronRight } from 'lucide-react';
import KpiCard from '../../components/ui/KpiCard';
import AlertBanner from '../../components/ui/AlertBanner';
import KpiDetailModal, { type SelectedKpiItem } from '../../components/ui/KpiDetailModal';
import type { StatCardKey } from '../../constants/kpiDetails';
import { kpisApi } from '../../api/kpis';
import type { DashboardKpis, ModeloFinanciero } from '../../types';

const PERIOD_OPTIONS = [
  { label: '7 días', value: 7 },
  { label: '30 días', value: 30 },
  { label: '90 días', value: 90 },
];

export default function DashboardPage() {
  const [period, setPeriod] = useState(30);
  const [data, setData] = useState<DashboardKpis | null>(null);
  const [modelo, setModelo] = useState<ModeloFinanciero | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedCard, setSelectedCard] = useState<SelectedKpiItem | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const [d, m] = await Promise.all([kpisApi.getDashboard(period), kpisApi.getModeloFinanciero()]);
      setData(d);
      setModelo(m);
    } catch {
      setError('No se pudo conectar a la API. Usando datos demo.');
      // Demo data
      setData({
        periodo: { desde: '2026-08-18', hasta: '2026-09-18', diasPeriodo: period },
        generadoEn: new Date().toISOString(),
        resumenEjecutivo: { totalOrdenes: 248, ordenesEntregadas: 201, ordenesCanceladas: 12, totalMatches3Way: 87, totalRmasPeriodo: 8, totalComisionGenerada: 4820.5 },
        kpis: [
          { numero: 1, nombre: 'Fill Rate', valor: 81.05, unidad: '%', meta: 95, semaforo: 'Rojo', descripcion: 'Porcentaje de órdenes completadas', cumpleMeta: false },
          { numero: 2, nombre: 'OTIF', valor: 0, unidad: '%', meta: 95, semaforo: 'Rojo', descripcion: 'On-Time In-Full', cumpleMeta: false },
          { numero: 3, nombre: 'Tasa Cancelación', valor: 4.84, unidad: '%', meta: 5, semaforo: 'Verde', descripcion: 'Menor es mejor', cumpleMeta: true },
          { numero: 4, nombre: 'Tiempo Ciclo Pedido', valor: 0, unidad: 'horas', meta: 48, semaforo: 'Verde', descripcion: 'Tiempo promedio de entrega', cumpleMeta: true },
          { numero: 5, nombre: 'Rotación de Inventario', valor: 0, unidad: 'veces', meta: 4, semaforo: 'Rojo', descripcion: 'Renovación del inventario', cumpleMeta: false },
          { numero: 6, nombre: 'Tasa Devolución RMA', valor: 3.98, unidad: '%', meta: 3, semaforo: 'Amarillo', descripcion: 'Porcentaje de devoluciones', cumpleMeta: false },
          { numero: 7, nombre: 'Precisión 3-Way Match', valor: 100, unidad: '%', meta: 98, semaforo: 'Verde', descripcion: 'Matches conformes', cumpleMeta: true },
          { numero: 8, nombre: 'Margen Comisión', valor: 10, unidad: '%', meta: 10, semaforo: 'Verde', descripcion: 'Comisión marketplace', cumpleMeta: true },
        ],
      });
      setModelo({
        generadoEn: new Date().toISOString(),
        supuestos: {},
        resumenAnual: { ventasTotalesAnuales: 578234, comisionTotalAnual: 57823, costosTotalesAnuales: 37585, utilidadTotalAnual: 20238 },
        meses: Array.from({ length: 12 }, (_, i) => ({
          mes: i + 1,
          periodo: `2026-${String(i + 1).padStart(2, '0')}`,
          ventasProyectadas: 48000 * Math.pow(1.08, i),
          comisionMarketplace: 4800 * Math.pow(1.08, i),
          costosOperativos: 3120 * Math.pow(1.08, i),
          utilidadNeta: 1680 * Math.pow(1.08, i),
          margenNetoPct: 35,
          factorCrecimiento: Math.pow(1.08, i),
        })),
      });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [period]);

  const res = data?.resumenEjecutivo;

  const statCards: {
    key: StatCardKey;
    label: string;
    value: string | number;
    sub: string;
    delta: string;
    deltaType: 'up' | 'down' | 'neutral';
    icon: React.ReactNode;
  }[] = [
    {
      key: 'total_ordenes',
      label: 'Total Órdenes',
      value: res?.totalOrdenes ?? 0,
      sub: `Últimos ${period} días`,
      delta: '+8.4%',
      deltaType: 'up',
      icon: <ShoppingCart size={18} />,
    },
    {
      key: 'ordenes_entregadas',
      label: 'Órdenes Entregadas',
      value: res?.ordenesEntregadas ?? 0,
      sub: 'Completadas a tiempo',
      delta: `${res?.totalOrdenes ? Math.round(((res.ordenesEntregadas ?? 0) / res.totalOrdenes) * 100) : 0}% tasa`,
      deltaType: 'up',
      icon: <Package size={18} />,
    },
    {
      key: 'canceladas',
      label: 'Canceladas',
      value: res?.ordenesCanceladas ?? 0,
      sub: 'Requieren atención',
      delta: `${res?.totalOrdenes ? ((res.ordenesCanceladas ?? 0) / res.totalOrdenes * 100).toFixed(1) : 0}%`,
      deltaType: ((res?.ordenesCanceladas ?? 0) > 10 ? 'down' : 'neutral'),
      icon: <TrendingUp size={18} />,
    },
    {
      key: 'comision_generada',
      label: 'Comisión Generada',
      value: `$${(res?.totalComisionGenerada ?? 0).toLocaleString('es-SV', { maximumFractionDigits: 0 })}`,
      sub: 'Marketplace 10%',
      delta: '100% cobrable',
      deltaType: 'neutral',
      icon: <DollarSign size={18} />,
    },
  ];

  return (
    <div>
      {/* Header Limpio y Profesional */}
      <div className="page-header" style={{ alignItems: 'flex-start' }}>
        <div>
          <h1 className="page-title">Dashboard Ejecutivo</h1>
          <p className="page-subtitle">
            Monitoreo en tiempo real de fulfillment, nivel de servicio logístico y flujo financiero.
          </p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          <div className="tabs">
            {PERIOD_OPTIONS.map(o => (
              <button key={o.value} className={`tab${period === o.value ? ' active' : ''}`} onClick={() => setPeriod(o.value)}>
                {o.label}
              </button>
            ))}
          </div>
          <button className="btn btn-secondary" onClick={load} disabled={loading}>
            <RefreshCw size={13} className={loading ? 'spin' : ''} />
            {loading ? 'Cargando...' : 'Actualizar'}
          </button>
        </div>
      </div>

      {error && (
        <AlertBanner
          variant="warning"
          title="Modo Demo / Datos Simulados"
          message={error}
          action={{
            label: 'Reintentar',
            onClick: load,
          }}
          onClose={() => setError(null)}
        />
      )}

      {/* Resumen ejecutivo neutro y uniforme */}
      <div className="stat-grid">
        {statCards.map((s, i) => (
          <div
            className={`stat-card animate-in stagger-${i + 1}`}
            key={s.label}
            role="button"
            tabIndex={0}
            aria-haspopup="dialog"
            onClick={() => setSelectedCard({ type: 'stat', key: s.key })}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                setSelectedCard({ type: 'stat', key: s.key });
              }
            }}
          >
            <div className="stat-icon-wrap">
              {s.icon}
            </div>
            <div className="stat-info">
              <div className="stat-header">
                <span className="stat-label" style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                  {s.label}
                  <ChevronRight size={11} style={{ color: 'var(--text-muted)', opacity: 0.7 }} />
                </span>
                <span className={`stat-delta ${s.deltaType}`}>{s.delta}</span>
              </div>
              <div className="stat-value">
                {s.value}
              </div>
              <div className="stat-sub">{s.sub}</div>
            </div>
          </div>
        ))}
      </div>

      {/* 8 KPI Cards */}
      {loading ? (
        <div className="kpi-grid">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="kpi-card">
              <div className="skeleton" style={{ height: 12, width: '60%', borderRadius: 4 }} />
              <div className="skeleton" style={{ height: 32, width: '40%', borderRadius: 4, marginTop: 8 }} />
              <div className="skeleton" style={{ height: 3, width: '100%', borderRadius: 4, marginTop: 8 }} />
            </div>
          ))}
        </div>
      ) : (
        <div className="kpi-grid">
          {(data?.kpis ?? []).map(kpi => (
            <KpiCard
              key={kpi.numero}
              kpi={kpi}
              onClick={() => setSelectedCard({ type: 'kpi', kpi })}
            />
          ))}
        </div>
      )}

      {/* Charts */}
      {modelo && (
        <div className="grid-2" style={{ marginTop: 0 }}>
          {/* Proyección 12 meses */}
          <div className="chart-container animate-in stagger-6">
            <div className="chart-header">
              <div>
                <div className="card-title">Proyección Financiera 12 Meses</div>
                <div className="card-subtitle">Ventas · Comisión · Utilidad (crecimiento 8% mensual)</div>
              </div>
            </div>
            <ResponsiveContainer width="100%" height={200}>
              <LineChart data={modelo.meses} margin={{ top: 0, right: 0, left: -20, bottom: 0 }}>
                <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
                <XAxis dataKey="periodo" tick={{ fontSize: 10, fill: 'var(--text-muted)' }} tickFormatter={v => v.slice(5)} />
                <YAxis tick={{ fontSize: 10, fill: 'var(--text-muted)' }} tickFormatter={v => `$${(v / 1000).toFixed(0)}k`} />
                <Tooltip
                  contentStyle={{ background: 'var(--bg-surface)', border: '1px solid var(--border)', borderRadius: 8, fontSize: 11 }}
                  formatter={(v: unknown) => v !== undefined ? [`$${Number(v).toFixed(0)}`, undefined] : []}
                />
                <Legend iconType="circle" iconSize={8} wrapperStyle={{ fontSize: 11 }} />
                <Line type="monotone" dataKey="ventasProyectadas" name="Ventas" stroke="#6366f1" strokeWidth={2} dot={false} />
                <Line type="monotone" dataKey="comisionMarketplace" name="Comisión" stroke="#22c55e" strokeWidth={2} dot={false} />
                <Line type="monotone" dataKey="utilidadNeta" name="Utilidad" stroke="#f59e0b" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>

          {/* Bar chart resumen anual */}
          <div className="chart-container animate-in stagger-7">
            <div className="chart-header">
              <div>
                <div className="card-title">Resumen Anual Proyectado</div>
                <div className="card-subtitle">Distribución ventas / comisión / costos / utilidad</div>
              </div>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 16 }}>
              {[
                { label: 'Ventas Totales', value: modelo.resumenAnual.ventasTotalesAnuales, color: '#6366f1' },
                { label: 'Comisión Total', value: modelo.resumenAnual.comisionTotalAnual, color: '#22c55e' },
                { label: 'Costos Op.', value: modelo.resumenAnual.costosTotalesAnuales, color: '#f59e0b' },
                { label: 'Utilidad Neta', value: modelo.resumenAnual.utilidadTotalAnual, color: '#22d3ee' },
              ].map(s => (
                <div key={s.label} style={{ background: 'var(--bg-surface)', borderRadius: 'var(--radius-md)', padding: '12px 14px' }}>
                  <div style={{ fontSize: 10, color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.06em', fontWeight: 600 }}>{s.label}</div>
                  <div style={{ fontSize: 18, fontWeight: 800, color: s.color, letterSpacing: '-0.03em', marginTop: 4 }}>
                    ${s.value.toLocaleString('es-SV', { maximumFractionDigits: 0 })}
                  </div>
                </div>
              ))}
            </div>
            <ResponsiveContainer width="100%" height={120}>
              <BarChart data={modelo.meses.slice(0, 6)} margin={{ top: 0, right: 0, left: -20, bottom: 0 }}>
                <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
                <XAxis dataKey="periodo" tick={{ fontSize: 10, fill: 'var(--text-muted)' }} tickFormatter={v => v.slice(5)} />
                <YAxis tick={{ fontSize: 10, fill: 'var(--text-muted)' }} tickFormatter={v => `$${(v / 1000).toFixed(0)}k`} />
                <Tooltip contentStyle={{ background: 'var(--bg-surface)', border: '1px solid var(--border)', borderRadius: 8, fontSize: 11 }} formatter={(v: unknown) => v !== undefined ? [`$${Number(v).toFixed(0)}`] : []} />
                <Bar dataKey="comisionMarketplace" name="Comisión" fill="#6366f1" radius={[3, 3, 0, 0]} />
                <Bar dataKey="utilidadNeta" name="Utilidad" fill="#22c55e" radius={[3, 3, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}

      {/* Modal de Detalle de KPI y Resumen Ejecutivo */}
      <KpiDetailModal
        selected={selectedCard}
        data={data}
        onClose={() => setSelectedCard(null)}
      />
    </div>
  );
}
