/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Hook: Gestión de visibilidad de tarjetas del Dashboard
 */

import { useState, useCallback, useMemo } from 'react';
import {
  ALL_DASHBOARD_CARD_KEYS,
  STAT_CARD_KEYS,
  KPI_CARD_KEYS,
  type DashboardCardKey,
} from '../constants/kpiDetails';

const LS_KEY = 'erp_dashboard_kpis_visibles';
const ALL_KEYS_SET = new Set<string>(ALL_DASHBOARD_CARD_KEYS);

/** Lee las claves visibles desde localStorage; devuelve todas si falla o está vacío */
function readFromStorage(): DashboardCardKey[] {
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (!raw) return [...ALL_DASHBOARD_CARD_KEYS];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [...ALL_DASHBOARD_CARD_KEYS];
    // Filtra solo claves conocidas
    const valid = parsed.filter(
      (k): k is DashboardCardKey => typeof k === 'string' && ALL_KEYS_SET.has(k)
    );
    return valid.length > 0 ? valid : [...ALL_DASHBOARD_CARD_KEYS];
  } catch {
    return [...ALL_DASHBOARD_CARD_KEYS];
  }
}

/** Escribe las claves visibles en localStorage */
function writeToStorage(keys: DashboardCardKey[]): void {
  try {
    localStorage.setItem(LS_KEY, JSON.stringify(keys));
  } catch {
    // localStorage lleno o no disponible — silenciosamente ignora
  }
}

export interface DashboardKpiVisibility {
  /** Claves actualmente visibles */
  visibleKeys: Set<DashboardCardKey>;
  /** Total de tarjetas visibles */
  visibleCount: number;
  /** Stat cards visibles filtradas, en orden original */
  visibleStatKeys: DashboardCardKey[];
  /** KPI cards visibles filtradas (devuelve números), en orden original */
  visibleKpiNumbers: number[];
  /** Abre el modal de configuración */
  openConfig: () => void;
  /** Cierra el modal y descarta borrador */
  closeConfig: () => void;
  /** ¿Está abierto el modal de configuración? */
  configOpen: boolean;
  /** Borrador (claves seleccionadas en el modal) */
  draft: Set<DashboardCardKey>;
  /** Toggle una clave en el borrador */
  toggleDraft: (key: DashboardCardKey) => void;
  /** Reemplaza el borrador completo */
  setDraftKeys: (keys: DashboardCardKey[]) => void;
  /** Aplica el borrador al estado real y guarda en localStorage */
  applyDraft: () => void;
  /** Resetea borrador a las 12 */
  resetDraft: () => void;
  /** ¿El borrador tiene 0 seleccionados? */
  isDraftEmpty: boolean;
}

export default function useDashboardKpiVisibility(): DashboardKpiVisibility {
  const [visibleList, setVisibleList] = useState<DashboardCardKey[]>(readFromStorage);
  const [configOpen, setConfigOpen] = useState(false);
  const [draft, setDraft] = useState<Set<DashboardCardKey>>(new Set());

  const visibleKeys = useMemo(() => new Set(visibleList), [visibleList]);

  const visibleStatKeys = useMemo(
    () => STAT_CARD_KEYS.filter(k => visibleKeys.has(k)),
    [visibleKeys]
  );

  const visibleKpiNumbers = useMemo(
    () => KPI_CARD_KEYS
      .filter(k => visibleKeys.has(k))
      .map(k => Number(k.replace('kpi_', ''))),
    [visibleKeys]
  );

  const openConfig = useCallback(() => {
    setDraft(new Set(visibleList));
    setConfigOpen(true);
  }, [visibleList]);

  const closeConfig = useCallback(() => {
    setConfigOpen(false);
  }, []);

  const toggleDraft = useCallback((key: DashboardCardKey) => {
    setDraft(prev => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }, []);

  const setDraftKeys = useCallback((keys: DashboardCardKey[]) => {
    setDraft(new Set(keys));
  }, []);

  const applyDraft = useCallback(() => {
    if (draft.size === 0) return;
    // Preserva orden original
    const ordered = ALL_DASHBOARD_CARD_KEYS.filter(k => draft.has(k));
    setVisibleList(ordered);
    writeToStorage(ordered);
    setConfigOpen(false);
  }, [draft]);

  const resetDraft = useCallback(() => {
    setDraft(new Set(ALL_DASHBOARD_CARD_KEYS));
  }, []);

  return {
    visibleKeys,
    visibleCount: visibleList.length,
    visibleStatKeys,
    visibleKpiNumbers,
    openConfig,
    closeConfig,
    configOpen,
    draft,
    toggleDraft,
    setDraftKeys,
    applyDraft,
    resetDraft,
    isDraftEmpty: draft.size === 0,
  };
}
