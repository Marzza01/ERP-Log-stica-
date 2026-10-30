/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Modal de configuración: seleccionar tarjetas visibles del Dashboard
 */

import React from 'react';
import {
  ALL_DASHBOARD_CARD_KEYS,
  STAT_CARD_KEYS,
  KPI_CARD_KEYS,
  CARD_SHORT_NAMES_MAP,
  CARD_DESCRIPTIONS_MAP,
  type DashboardCardKey,
} from '../../constants/kpiDetails';
import Modal from './Modal';
import type { DashboardKpiVisibility } from '../../hooks/useDashboardKpiVisibility';
import {
  ShoppingCart,
  Package,
  TrendingUp,
  DollarSign,
  Check,
  CheckCircle2,
  Clock,
  RotateCcw,
  FileCheck2,
  Percent,
  Layers,
  Sparkles,
} from 'lucide-react';

interface Props {
  visibility: DashboardKpiVisibility;
}

const TOTAL = ALL_DASHBOARD_CARD_KEYS.length;

// Mapeo coherente de íconos según tema de la tarjeta
const CARD_ICONS_MAP: Record<DashboardCardKey, React.ReactNode> = {
  total_ordenes: <ShoppingCart size={16} />,
  ordenes_entregadas: <Package size={16} />,
  canceladas: <TrendingUp size={16} />,
  comision_generada: <DollarSign size={16} />,
  kpi_1: <CheckCircle2 size={16} />,      // Fill Rate
  kpi_2: <Sparkles size={16} />,          // OTIF
  kpi_3: <RotateCcw size={16} />,         // Tasa Cancelación
  kpi_4: <Clock size={16} />,             // Tiempo Ciclo
  kpi_5: <Layers size={16} />,            // Rotación Inventario
  kpi_6: <RotateCcw size={16} />,         // Tasa Devolución RMA
  kpi_7: <FileCheck2 size={16} />,        // 3-Way Match
  kpi_8: <Percent size={16} />,           // Margen Comisión
};

export default function DashboardConfigModal({ visibility }: Props) {
  const {
    configOpen,
    closeConfig,
    draft,
    toggleDraft,
    setDraftKeys,
    applyDraft,
    resetDraft,
    isDraftEmpty,
  } = visibility;

  // Atajos
  const isAllSelected = draft.size === TOTAL;
  const isOnlyStat =
    draft.size === STAT_CARD_KEYS.length &&
    STAT_CARD_KEYS.every(k => draft.has(k)) &&
    KPI_CARD_KEYS.every(k => !draft.has(k));
  const isOnlyKpi =
    draft.size === KPI_CARD_KEYS.length &&
    KPI_CARD_KEYS.every(k => draft.has(k)) &&
    STAT_CARD_KEYS.every(k => !draft.has(k));

  // Contadores por grupo
  const statSelectedCount = STAT_CARD_KEYS.filter(k => draft.has(k)).length;
  const kpiSelectedCount = KPI_CARD_KEYS.filter(k => draft.has(k)).length;

  // Acciones rápidas por grupo
  const handleSelectGroup = (keys: DashboardCardKey[], select: boolean) => {
    const next = new Set(draft);
    keys.forEach(k => {
      if (select) next.add(k);
      else next.delete(k);
    });
    setDraftKeys([...next]);
  };

  return (
    <Modal
      title="Personalizar Dashboard"
      open={configOpen}
      onClose={closeConfig}
      size="md"
      className="dash-config-modal"
      footer={
        <div className="dash-config-footer">
          <button
            className="btn btn-ghost btn-sm"
            onClick={resetDraft}
            type="button"
          >
            Restablecer
          </button>
          <div className="dash-config-footer-right">
            <button
              className="btn btn-secondary btn-sm"
              onClick={closeConfig}
              type="button"
            >
              Cancelar
            </button>
            <button
              className="btn btn-primary btn-sm"
              onClick={applyDraft}
              disabled={isDraftEmpty}
              type="button"
            >
              Aplicar
            </button>
          </div>
        </div>
      }
    >
      <div className="dash-config-content">
        {/* Atajos superiores: segmented tabs + contador */}
        <div className="dash-config-topbar">
          <div className="tabs">
            <button
              type="button"
              className={`tab${isAllSelected ? ' active' : ''}`}
              onClick={() => setDraftKeys([...ALL_DASHBOARD_CARD_KEYS])}
            >
              Todos ({TOTAL})
            </button>
            <button
              type="button"
              className={`tab${isOnlyStat ? ' active' : ''}`}
              onClick={() => setDraftKeys([...STAT_CARD_KEYS])}
            >
              Solo resumen ({STAT_CARD_KEYS.length})
            </button>
            <button
              type="button"
              className={`tab${isOnlyKpi ? ' active' : ''}`}
              onClick={() => setDraftKeys([...KPI_CARD_KEYS])}
            >
              Solo KPIs ({KPI_CARD_KEYS.length})
            </button>
          </div>

          <span className="dash-config-counter">
            {draft.size} de {TOTAL} visibles
          </span>
        </div>

        {/* Grupo 1: Resumen Ejecutivo */}
        <div role="group" aria-labelledby="dash-config-group-stat" className="dash-config-group">
          <div className="dash-config-group-header">
            <h4 id="dash-config-group-stat" className="dash-config-group-title">
              Resumen Ejecutivo ({statSelectedCount} de {STAT_CARD_KEYS.length})
            </h4>
            <div className="dash-config-group-actions">
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => handleSelectGroup(STAT_CARD_KEYS, true)}
              >
                Todos
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => handleSelectGroup(STAT_CARD_KEYS, false)}
              >
                Ninguno
              </button>
            </div>
          </div>

          <div className="dash-config-grid">
            {STAT_CARD_KEYS.map(key => (
              <SelectableCardRow
                key={key}
                cardKey={key}
                checked={draft.has(key)}
                onToggle={toggleDraft}
              />
            ))}
          </div>
        </div>

        <div className="dash-config-divider" />

        {/* Grupo 2: Indicadores KPI */}
        <div role="group" aria-labelledby="dash-config-group-kpi" className="dash-config-group">
          <div className="dash-config-group-header">
            <h4 id="dash-config-group-kpi" className="dash-config-group-title">
              Indicadores KPI ({kpiSelectedCount} de {KPI_CARD_KEYS.length})
            </h4>
            <div className="dash-config-group-actions">
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => handleSelectGroup(KPI_CARD_KEYS, true)}
              >
                Todos
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => handleSelectGroup(KPI_CARD_KEYS, false)}
              >
                Ninguno
              </button>
            </div>
          </div>

          <div className="dash-config-grid">
            {KPI_CARD_KEYS.map(key => (
              <SelectableCardRow
                key={key}
                cardKey={key}
                checked={draft.has(key)}
                onToggle={toggleDraft}
              />
            ))}
          </div>
        </div>

        {/* Error al seleccionar 0 */}
        {isDraftEmpty && (
          <p className="dash-config-error" role="alert">
            Selecciona al menos un indicador para mostrar en el Dashboard
          </p>
        )}
      </div>
    </Modal>
  );
}

/* ─── Fila-Tarjeta Seleccionable Accesible ─── */

function SelectableCardRow({
  cardKey,
  checked,
  onToggle,
}: {
  cardKey: DashboardCardKey;
  checked: boolean;
  onToggle: (key: DashboardCardKey) => void;
}) {
  const shortName = CARD_SHORT_NAMES_MAP[cardKey] ?? cardKey;
  const desc = CARD_DESCRIPTIONS_MAP[cardKey] ?? '';
  const icon = CARD_ICONS_MAP[cardKey] ?? <CheckCircle2 size={16} />;

  return (
    <label className={`dash-config-card${checked ? ' selected' : ''}`}>
      <input
        type="checkbox"
        checked={checked}
        onChange={() => onToggle(cardKey)}
        className="dash-config-sr-input"
        aria-label={`${shortName} - ${desc}`}
      />
      <div className="dash-config-card-left">
        <div className="dash-config-card-icon" aria-hidden="true">
          {icon}
        </div>
        <div className="dash-config-card-texts">
          <span className="dash-config-card-name">{shortName}</span>
          <span className="dash-config-card-desc">{desc}</span>
        </div>
      </div>
      <div className="dash-config-indicator" aria-hidden="true">
        {checked && <Check size={12} strokeWidth={3} />}
      </div>
    </label>
  );
}
