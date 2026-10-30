/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Modal de detalle para los 8 KPIs y 4 tarjetas de resumen del Dashboard
 */

import React from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowRight, ExternalLink } from 'lucide-react';
import Modal from './Modal';
import type { DashboardKpis, Kpi, SemaforoColor } from '../../types';
import {
  KPI_DETAILS_CONFIG,
  STAT_CARDS_CONFIG,
  type StatCardKey,
} from '../../constants/kpiDetails';
import { ZoneBarChart, CompositionChart, type CompositionItem } from './kpi-charts';

export type SelectedKpiItem =
  | { type: 'kpi'; kpi: Kpi }
  | { type: 'stat'; key: StatCardKey };

interface KpiDetailModalProps {
  selected: SelectedKpiItem | null;
  data: DashboardKpis | null;
  onClose: () => void;
}

const semaforoColors: Record<SemaforoColor, string> = {
  Verde: 'var(--green)',
  Amarillo: 'var(--yellow)',
  Rojo: 'var(--red)',
};

export default function KpiDetailModal({ selected, data, onClose }: KpiDetailModalProps) {
  const navigate = useNavigate();

  if (!selected) return null;

  const res = data?.resumenEjecutivo;
  const periodo = data?.periodo;
  const totalOrdenes = Math.max(0, res?.totalOrdenes ?? 0);
  const ordenesEntregadas = Math.max(0, res?.ordenesEntregadas ?? 0);
  const ordenesCanceladas = Math.max(0, res?.ordenesCanceladas ?? 0);
  const ordenesEnCurso = Math.max(0, totalOrdenes - ordenesEntregadas - ordenesCanceladas);

  // ───────────────────────────────────────────────────────────────────────────
  // Render: KPI Detail (1–8)
  // ───────────────────────────────────────────────────────────────────────────
  if (selected.type === 'kpi') {
    // Buscar la versión más actualizada del KPI según data
    const currentKpi = data?.kpis.find(k => k.numero === selected.kpi.numero) ?? selected.kpi;
    const config = KPI_DETAILS_CONFIG[currentKpi.numero];
    if (!config) return null;

    const color = semaforoColors[currentKpi.semaforo];
    const cls = currentKpi.semaforo.toLowerCase();

    // Cálculo de diferencia vs meta
    const diff = currentKpi.valor - currentKpi.meta;
    let diffText = '';
    let diffFavorable = false;

    if (config.sentido === 'mayor') {
      diffFavorable = diff >= 0;
      if (diff >= 0) {
        diffText = `${diff.toLocaleString('es-SV', { maximumFractionDigits: 1 })} ${currentKpi.unidad === '%' ? 'pts' : currentKpi.unidad} por encima de la meta`;
      } else {
        diffText = `${Math.abs(diff).toLocaleString('es-SV', { maximumFractionDigits: 1 })} ${currentKpi.unidad === '%' ? 'pts' : currentKpi.unidad} por debajo de la meta`;
      }
    } else {
      diffFavorable = diff <= 0;
      if (diff <= 0) {
        diffText = `${Math.abs(diff).toLocaleString('es-SV', { maximumFractionDigits: 1 })} ${currentKpi.unidad === '%' ? 'pts' : currentKpi.unidad} dentro del límite permitido`;
      } else {
        diffText = `${diff.toLocaleString('es-SV', { maximumFractionDigits: 1 })} ${currentKpi.unidad === '%' ? 'pts' : currentKpi.unidad} por encima del umbral máximo tolerado`;
      }
    }

    // Progreso visual de la barra hacia la meta
    let progressPct = 0;
    if (config.sentido === 'mayor') {
      progressPct = currentKpi.meta > 0
        ? Math.min(100, Math.max(0, (currentKpi.valor / currentKpi.meta) * 100))
        : 0;
    } else {
      progressPct = currentKpi.meta > 0
        ? (currentKpi.valor <= currentKpi.meta
            ? 100
            : Math.max(10, Math.min(100, (currentKpi.meta / Math.max(currentKpi.valor, 0.01)) * 100)))
        : 100;
    }

    const recomendacion = config.recomendaciones[currentKpi.semaforo] ?? '';

    // Renderizado del componente visual específico para este KPI (Gráficos A y B)
    const renderKpiVisuals = () => {
      let compositionComponent: React.ReactNode = null;

      switch (currentKpi.numero) {
        case 1: { // Fill Rate → Composición: Entregadas / Canceladas / En curso
          const compItems: CompositionItem[] = [
            { nombre: 'Entregadas', valor: ordenesEntregadas, color: 'var(--green)' },
            { nombre: 'En curso', valor: ordenesEnCurso, color: 'var(--blue)' },
            { nombre: 'Canceladas', valor: ordenesCanceladas, color: 'var(--red)' },
          ];
          compositionComponent = (
            <CompositionChart
              tipo="donut"
              datos={compItems}
              centroTitulo={totalOrdenes > 0 ? `${((ordenesEntregadas / totalOrdenes) * 100).toFixed(1)}%` : '0%'}
              centroSubtitulo="Fill Rate"
            />
          );
          break;
        }

        case 3: { // Tasa Cancelación → Canceladas vs Resto de órdenes
          const restoOrdenes = Math.max(0, totalOrdenes - ordenesCanceladas);
          const compItems: CompositionItem[] = [
            { nombre: 'Canceladas', valor: ordenesCanceladas, color: 'var(--red)' },
            { nombre: 'Resto de órdenes', valor: restoOrdenes, color: 'var(--blue)' },
          ];
          compositionComponent = (
            <CompositionChart
              tipo="donut"
              datos={compItems}
              centroTitulo={totalOrdenes > 0 ? `${((ordenesCanceladas / totalOrdenes) * 100).toFixed(1)}%` : '0%'}
              centroSubtitulo="Cancelación"
            />
          );
          break;
        }

        case 6: { // Tasa Devolución RMA → RMAs vs Entregadas sin devolución
          const totalRmas = Math.max(0, res?.totalRmasPeriodo ?? 0);
          const entregadasSinRma = Math.max(0, ordenesEntregadas - totalRmas);
          const compItems: CompositionItem[] = [
            { nombre: 'Solicitudes RMA', valor: totalRmas, color: 'var(--red)' },
            { nombre: 'Entregadas sin RMA', valor: entregadasSinRma, color: 'var(--green)' },
          ];
          compositionComponent = (
            <CompositionChart
              tipo="donut"
              datos={compItems}
              centroTitulo={ordenesEntregadas > 0 ? `${((totalRmas / ordenesEntregadas) * 100).toFixed(1)}%` : '0%'}
              centroSubtitulo="Tasa RMA"
            />
          );
          break;
        }

        case 7: { // Precisión 3-Way Match → Conformes vs Con discrepancias (derivado)
          const auditadas = Math.max(0, res?.totalMatches3Way ?? 0);
          const conformes = auditadas > 0 ? Math.round((currentKpi.valor / 100) * auditadas) : 0;
          const discrepancias = Math.max(0, auditadas - conformes);
          const compItems: CompositionItem[] = [
            { nombre: 'Conformes', valor: conformes, color: 'var(--green)', badge: 'estimado' },
            { nombre: 'Con discrepancias', valor: discrepancias, color: 'var(--yellow)', badge: 'estimado' },
          ];
          compositionComponent = (
            <CompositionChart
              tipo="donut"
              datos={compItems}
              centroTitulo={`${currentKpi.valor.toLocaleString('es-SV', { maximumFractionDigits: 1 })}%`}
              centroSubtitulo="Conformes"
            />
          );
          break;
        }

        case 8: { // Margen Comisión → de cada $100 vendidos: Comisión retenida vs Resto
          const comisionValor = Math.max(0, currentKpi.valor);
          const restoValor = Math.max(0, 100 - comisionValor);
          const compItems: CompositionItem[] = [
            { nombre: 'Comisión retenida', valor: comisionValor, color: 'var(--green)', badge: 'referencial' },
            { nombre: 'Resto para vendedores', valor: restoValor, color: 'var(--blue)', badge: 'referencial' },
          ];
          compositionComponent = (
            <CompositionChart
              tipo="donut"
              datos={compItems}
              centroTitulo={`${currentKpi.valor.toLocaleString('es-SV', { maximumFractionDigits: 1 })}%`}
              centroSubtitulo="Comisión"
            />
          );
          break;
        }

        default:
          // OTIF (2), Tiempo Ciclo Pedido (4), Rotación de Inventario (5) -> Solo gráfico A
          break;
      }

      return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {/* Gráfico A: Valor vs Meta con franjas semafóricas */}
          <ZoneBarChart
            valor={currentKpi.valor}
            meta={currentKpi.meta}
            unidad={currentKpi.unidad}
            sentido={config.sentido}
            umbrales={config.umbrales}
            color={color}
            titulo={config.titulo}
          />

          {/* Gráfico B: Donut de composición con datos reales */}
          {compositionComponent}
        </div>
      );
    };

    // Render de datos del período específicos por KPI
    const renderRelatedData = () => {
      switch (currentKpi.numero) {
        case 1: // Fill Rate
          return (
            <div className="kpi-detail-data-box">
              <div className="kpi-detail-data-row">
                <span>Órdenes completadas:</span>
                <span>{ordenesEntregadas.toLocaleString('es-SV')} de {totalOrdenes.toLocaleString('es-SV')}</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Tasa efectiva:</span>
                <span>{totalOrdenes > 0 ? ((ordenesEntregadas / totalOrdenes) * 100).toFixed(1) : '0'}%</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Período evaluado:</span>
                <span>Últimos {periodo?.diasPeriodo ?? 30} días</span>
              </div>
            </div>
          );
        case 3: // Tasa Cancelación
          return (
            <div className="kpi-detail-data-box">
              <div className="kpi-detail-data-row">
                <span>Órdenes canceladas:</span>
                <span>{ordenesCanceladas.toLocaleString('es-SV')} de {totalOrdenes.toLocaleString('es-SV')}</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Tasa de cancelación:</span>
                <span>{totalOrdenes > 0 ? ((ordenesCanceladas / totalOrdenes) * 100).toFixed(1) : '0'}%</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Período evaluado:</span>
                <span>Últimos {periodo?.diasPeriodo ?? 30} días</span>
              </div>
            </div>
          );
        case 6: // Tasa Devolución RMA
          return (
            <div className="kpi-detail-data-box">
              <div className="kpi-detail-data-row">
                <span>Total solicitudes RMA:</span>
                <span>{(res?.totalRmasPeriodo ?? 0).toLocaleString('es-SV')}</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Órdenes entregadas base:</span>
                <span>{ordenesEntregadas.toLocaleString('es-SV')}</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Incidencia relativa:</span>
                <span>{ordenesEntregadas > 0 ? (((res?.totalRmasPeriodo ?? 0) / ordenesEntregadas) * 100).toFixed(1) : '0'}%</span>
              </div>
            </div>
          );
        case 7: // Precisión 3-Way Match
          return (
            <div className="kpi-detail-data-box">
              <div className="kpi-detail-data-row">
                <span>Total auditorías registradas:</span>
                <span>{(res?.totalMatches3Way ?? 0).toLocaleString('es-SV')}</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Estado de auditoría:</span>
                <span>{currentKpi.valor >= 98 ? 'Alta conformidad' : 'Con discrepancias'}</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Período evaluado:</span>
                <span>Últimos {periodo?.diasPeriodo ?? 30} días</span>
              </div>
            </div>
          );
        case 8: // Margen Comisión
          return (
            <div className="kpi-detail-data-box">
              <div className="kpi-detail-data-row">
                <span>Comisión generada acumulada:</span>
                <span>${(res?.totalComisionGenerada ?? 0).toLocaleString('es-SV', { maximumFractionDigits: 2 })}</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Tasa nominal pactada:</span>
                <span>10.0%</span>
              </div>
              <div className="kpi-detail-data-row">
                <span>Período evaluado:</span>
                <span>Últimos {periodo?.diasPeriodo ?? 30} días</span>
              </div>
            </div>
          );
        default: // OTIF (2), Tiempo Ciclo (4), Rotación (5)
          return (
            <div className="kpi-detail-data-box">
              <div className="kpi-detail-data-row">
                <span>Período analizado:</span>
                <span>Últimos {periodo?.diasPeriodo ?? 30} días</span>
              </div>
              {periodo?.desde && periodo?.hasta && (
                <div className="kpi-detail-data-row">
                  <span>Rango de fechas:</span>
                  <span>{periodo.desde} al {periodo.hasta}</span>
                </div>
              )}
              <div className="kpi-detail-data-row">
                <span>Muestra operacional:</span>
                <span>Calculado sobre el flujo logístico del período</span>
              </div>
            </div>
          );
      }
    };

    return (
      <Modal
        title={`Detalle de KPI — ${config.titulo}`}
        open={true}
        onClose={onClose}
        size="md"
        footer={
          <div className="kpi-detail-footer">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => {
                navigate(config.modulo.ruta);
                onClose();
              }}
            >
              Ir a {config.modulo.nombre}
              <ArrowRight size={14} />
            </button>
            <button type="button" className="btn btn-primary" onClick={onClose}>
              Cerrar
            </button>
          </div>
        }
      >
        <div className="kpi-detail-container">
          {/* Header con estado del semáforo */}
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-secondary)' }}>
              Indicador #{currentKpi.numero}
            </span>
            <span className={`semaforo ${cls}`}>
              <span style={{ width: 6, height: 6, borderRadius: '50%', background: color, display: 'inline-block' }} />
              Semáforo {currentKpi.semaforo}
            </span>
          </div>

          {/* Valor actual grande y meta */}
          <div className="kpi-detail-hero">
            <div className="kpi-detail-value-wrap">
              <span className="kpi-detail-value">
                {currentKpi.valor.toLocaleString('es-SV', { maximumFractionDigits: 1 })}
              </span>
              <span className="kpi-detail-unit">{currentKpi.unidad}</span>
            </div>
            <div style={{ textAlign: 'right' }}>
              <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)' }}>
                Meta: {currentKpi.meta} {currentKpi.unidad}
              </div>
              <div className={`kpi-detail-meta-diff ${diffFavorable ? 'favorable' : 'desfavorable'}`}>
                {diffText}
              </div>
            </div>
          </div>

          {/* Barra de progreso */}
          <div className="kpi-progress" style={{ height: 6 }}>
            <div
              className="kpi-progress-bar"
              style={{ width: `${progressPct}%`, background: color }}
            />
          </div>

          {/* Sección VISUALIZACIÓN */}
          <div className="kpi-detail-section">
            <span className="kpi-detail-section-title">Visualización</span>
            {renderKpiVisuals()}
          </div>

          {/* Qué mide */}
          <div className="kpi-detail-section">
            <span className="kpi-detail-section-title">¿Qué mide este indicador?</span>
            <p style={{ margin: 0, fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5 }}>
              {config.queMide}
            </p>
          </div>

          {/* Fórmula */}
          <div className="kpi-detail-section">
            <span className="kpi-detail-section-title">Fórmula de cálculo</span>
            <div className="kpi-detail-formula">
              {config.formula}
            </div>
          </div>

          {/* Umbrales del semáforo */}
          <div className="kpi-detail-section">
            <span className="kpi-detail-section-title">Umbrales del semáforo</span>
            <div className="kpi-detail-threshold-table">
              {config.umbrales.map(u => {
                const isActive = u.color.toLowerCase() === cls;
                return (
                  <div
                    key={u.color}
                    className={`kpi-detail-threshold-row ${isActive ? 'kpi-detail-active' : ''}`}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                      <span className={`semaforo ${u.color.toLowerCase()}`}>
                        {u.color}
                      </span>
                      <span>{u.rango}</span>
                    </div>
                    <span style={{ fontSize: 11, color: isActive ? 'var(--text-primary)' : 'var(--text-muted)' }}>
                      {isActive ? '● Valor actual' : u.descripcion}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>

          {/* Datos relacionados del período */}
          <div className="kpi-detail-section">
            <span className="kpi-detail-section-title">Datos relacionados del período</span>
            {renderRelatedData()}
          </div>

          {/* Recomendación según semáforo */}
          <div className="kpi-detail-section">
            <span className="kpi-detail-section-title">Recomendación operativa</span>
            <div className="kpi-detail-recommendation">
              {recomendacion}
            </div>
          </div>
        </div>
      </Modal>
    );
  }

  // ───────────────────────────────────────────────────────────────────────────
  // Render: Resumen Ejecutivo Stat Card (4 tarjetas)
  // ───────────────────────────────────────────────────────────────────────────
  const statConfig = STAT_CARDS_CONFIG[selected.key];
  if (!statConfig) return null;

  const tasaEntrega = totalOrdenes > 0 ? ((ordenesEntregadas / totalOrdenes) * 100).toFixed(1) : '0.0';
  const tasaCancel = totalOrdenes > 0 ? ((ordenesCanceladas / totalOrdenes) * 100).toFixed(1) : '0.0';
  const comisionMonto = Math.max(0, res?.totalComisionGenerada ?? 0);
  // Valores derivados estimados de comisión
  const ventasEstimadasBase = comisionMonto > 0 ? comisionMonto / 0.10 : 0;
  const porcionVendedoresEstimada = ventasEstimadasBase * 0.90;

  const renderStatBody = () => {
    switch (selected.key) {
      case 'total_ordenes':
        return (
          <>
            <div className="kpi-detail-hero">
              <div className="kpi-detail-value-wrap">
                <span className="kpi-detail-value">
                  {totalOrdenes.toLocaleString('es-SV')}
                </span>
                <span className="kpi-detail-unit">órdenes</span>
              </div>
              <div style={{ textAlign: 'right' }}>
                <span className="badge verde" style={{ fontSize: 12 }}>
                  {tasaEntrega}% tasa de entrega
                </span>
              </div>
            </div>

            {/* Sección Visualización */}
            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Visualización</span>
              <CompositionChart
                tipo="donut"
                datos={[
                  { nombre: 'Entregadas', valor: ordenesEntregadas, color: 'var(--green)' },
                  { nombre: 'En curso', valor: ordenesEnCurso, color: 'var(--blue)' },
                  { nombre: 'Canceladas', valor: ordenesCanceladas, color: 'var(--red)' },
                ]}
                centroTitulo={totalOrdenes.toLocaleString('es-SV')}
                centroSubtitulo="Órdenes"
              />
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Desglose operacional del período</span>
              <div className="kpi-detail-data-box">
                <div className="kpi-detail-data-row">
                  <span>Órdenes entregadas:</span>
                  <span style={{ color: 'var(--green)' }}>{ordenesEntregadas.toLocaleString('es-SV')}</span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Órdenes en curso (preparación / envío):</span>
                  <span style={{ color: 'var(--blue)' }}>{ordenesEnCurso.toLocaleString('es-SV')}</span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Órdenes canceladas:</span>
                  <span style={{ color: 'var(--red)' }}>{ordenesCanceladas.toLocaleString('es-SV')}</span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Período evaluado:</span>
                  <span>Últimos {periodo?.diasPeriodo ?? 30} días</span>
                </div>
              </div>
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Descripción</span>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5 }}>
                {statConfig.queMide}
              </p>
            </div>
          </>
        );

      case 'ordenes_entregadas': {
        const pendientesYCanceladas = Math.max(0, totalOrdenes - ordenesEntregadas);
        return (
          <>
            <div className="kpi-detail-hero">
              <div className="kpi-detail-value-wrap">
                <span className="kpi-detail-value">
                  {ordenesEntregadas.toLocaleString('es-SV')}
                </span>
                <span className="kpi-detail-unit">entregadas</span>
              </div>
              <div style={{ textAlign: 'right' }}>
                <span className="badge verde" style={{ fontSize: 12 }}>
                  {tasaEntrega}% del total
                </span>
              </div>
            </div>

            {/* Sección Visualización */}
            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Visualización</span>
              <CompositionChart
                tipo="donut"
                datos={[
                  { nombre: 'Entregadas', valor: ordenesEntregadas, color: 'var(--green)' },
                  { nombre: 'Pendientes y Canceladas', valor: pendientesYCanceladas, color: 'var(--yellow)' },
                ]}
                centroTitulo={`${tasaEntrega}%`}
                centroSubtitulo="Entregadas"
              />
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Estado de cumplimiento</span>
              <div className="kpi-detail-data-box">
                <div className="kpi-detail-data-row">
                  <span>Total de órdenes recibidas:</span>
                  <span>{totalOrdenes.toLocaleString('es-SV')}</span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Entregadas a satisfacción:</span>
                  <span style={{ color: 'var(--green)' }}>{ordenesEntregadas.toLocaleString('es-SV')}</span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Pendientes de entrega (en curso):</span>
                  <span style={{ color: 'var(--blue)' }}>{ordenesEnCurso.toLocaleString('es-SV')}</span>
                </div>
              </div>
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Relación con el KPI #1 (Fill Rate)</span>
              <div className="kpi-detail-recommendation">
                Las órdenes entregadas determinan directamente el <strong>Fill Rate</strong>. Con {ordenesEntregadas} órdenes entregadas de {totalOrdenes}, el Fill Rate actual se sitúa en <strong>{tasaEntrega}%</strong> frente a una meta requerida de <strong>95%</strong>.
              </div>
            </div>
          </>
        );
      }

      case 'canceladas': {
        const cancelOk = Number(tasaCancel) <= 5.0;
        const resto = Math.max(0, totalOrdenes - ordenesCanceladas);
        return (
          <>
            <div className="kpi-detail-hero">
              <div className="kpi-detail-value-wrap">
                <span className="kpi-detail-value">
                  {ordenesCanceladas.toLocaleString('es-SV')}
                </span>
                <span className="kpi-detail-unit">canceladas</span>
              </div>
              <div style={{ textAlign: 'right' }}>
                <span className={`badge ${cancelOk ? 'verde' : 'rojo'}`} style={{ fontSize: 12 }}>
                  {tasaCancel}% del total
                </span>
              </div>
            </div>

            {/* Sección Visualización */}
            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Visualización</span>
              <CompositionChart
                tipo="donut"
                datos={[
                  { nombre: 'Canceladas', valor: ordenesCanceladas, color: 'var(--red)' },
                  { nombre: 'Resto de órdenes', valor: resto, color: 'var(--blue)' },
                ]}
                centroTitulo={`${tasaCancel}%`}
                centroSubtitulo="Canceladas"
                textoReferenciaMeta="Meta institucional: ≤ 5.0% de cancelaciones"
              />
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Evaluación frente a meta institucional</span>
              <div className="kpi-detail-data-box">
                <div className="kpi-detail-data-row">
                  <span>Órdenes canceladas registradas:</span>
                  <span>{ordenesCanceladas.toLocaleString('es-SV')}</span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Meta de tasa de cancelación:</span>
                  <span>≤ 5.0%</span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Estado de tolerancia:</span>
                  <span style={{ color: cancelOk ? 'var(--green)' : 'var(--red)' }}>
                    {cancelOk ? '✓ Dentro del límite tolerable' : '✗ Supera el umbral máximo tolerado'}
                  </span>
                </div>
              </div>
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Impacto operacional</span>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5 }}>
                {statConfig.queMide}
              </p>
            </div>
          </>
        );
      }

      case 'comision_generada':
        return (
          <>
            <div className="kpi-detail-hero">
              <div className="kpi-detail-value-wrap">
                <span className="kpi-detail-value">
                  ${comisionMonto.toLocaleString('es-SV', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                </span>
                <span className="kpi-detail-unit">USD</span>
              </div>
              <div style={{ textAlign: 'right' }}>
                <span className="badge azul" style={{ fontSize: 12 }}>
                  Tarifa pactada: 10%
                </span>
              </div>
            </div>

            {/* Sección Visualización */}
            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Visualización</span>
              <CompositionChart
                tipo="stacked-bar"
                esMontoDinero={true}
                datos={[
                  { nombre: 'Marketplace (10%)', valor: comisionMonto, color: 'var(--green)', badge: 'estimado', esMontoDinero: true },
                  { nombre: 'Vendedores (90%)', valor: porcionVendedoresEstimada, color: 'var(--blue)', badge: 'estimado', esMontoDinero: true },
                ]}
                textoReferenciaMeta="Split proyectado de ingresos transaccionales (10% / 90%)"
              />
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Valores estimados derivados del modelo (10%)</span>
              <div className="kpi-detail-data-box">
                <div className="kpi-detail-data-row">
                  <span>Comisión neta retenida:</span>
                  <span style={{ color: 'var(--green)' }}>
                    ${comisionMonto.toLocaleString('es-SV', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
                  </span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Ventas brutas estimadas base:</span>
                  <span>
                    ${ventasEstimadasBase.toLocaleString('es-SV', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} (estimado)
                  </span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Porción estimada para vendedores (90%):</span>
                  <span>
                    ${porcionVendedoresEstimada.toLocaleString('es-SV', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} (estimado)
                  </span>
                </div>
                <div className="kpi-detail-data-row">
                  <span>Período evaluado:</span>
                  <span>Últimos {periodo?.diasPeriodo ?? 30} días</span>
                </div>
              </div>
              <span style={{ fontSize: 11, color: 'var(--text-muted)', fontStyle: 'italic', marginTop: 2 }}>
                * Nota: Las ventas brutas y porción de vendedores son proyecciones estimadas derivadas de la comisión del 10%.
              </span>
            </div>

            <div className="kpi-detail-section">
              <span className="kpi-detail-section-title">Descripción del flujo de monetización</span>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--text-secondary)', lineHeight: 1.5 }}>
                {statConfig.queMide}
              </p>
            </div>
          </>
        );
    }
  };

  return (
    <Modal
      title={statConfig.titulo}
      open={true}
      onClose={onClose}
      size="md"
      footer={
        <div className="kpi-detail-footer">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => {
              navigate(statConfig.modulo.ruta);
              onClose();
            }}
          >
            Ir a {statConfig.modulo.nombre}
            <ExternalLink size={14} />
          </button>
          <button type="button" className="btn btn-primary" onClick={onClose}>
            Cerrar
          </button>
        </div>
      }
    >
      <div className="kpi-detail-container">
        {renderStatBody()}
      </div>
    </Modal>
  );
}
