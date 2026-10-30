/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Configuración descriptiva y umbrales de KPIs y Resumen Ejecutivo
 */

export interface ThresholdConfig {
  color: 'Verde' | 'Amarillo' | 'Rojo';
  rango: string;
  descripcion: string;
}

export interface KpiConfig {
  numero: number;
  titulo: string;
  descripcionCorta?: string;
  queMide: string;
  formula: string;
  sentido: 'mayor' | 'menor'; // mayor es mejor vs menor es mejor
  meta: number;
  unidad: string;
  umbrales: ThresholdConfig[];
  modulo: {
    nombre: string;
    ruta: string;
  };
  recomendaciones: Record<'Verde' | 'Amarillo' | 'Rojo', string>;
}

export type StatCardKey = 'total_ordenes' | 'ordenes_entregadas' | 'canceladas' | 'comision_generada';

export interface StatCardConfig {
  key: StatCardKey;
  titulo: string;
  subtitulo: string;
  descripcionCorta?: string;
  queMide: string;
  modulo: {
    nombre: string;
    ruta: string;
  };
}

/**
 * Configuración de los 8 KPIs oficiales del ERP (KpisDashboardController.cs)
 */
export const KPI_DETAILS_CONFIG: Record<number, KpiConfig> = {
  1: {
    numero: 1,
    titulo: 'Fill Rate',
    descripcionCorta: 'Órdenes completas entregadas',
    queMide: 'Porcentaje de órdenes de clientes que son completadas y entregadas exitosamente respecto al total de órdenes generadas.',
    formula: '(Órdenes Entregadas / Total Órdenes) × 100',
    sentido: 'mayor',
    meta: 95,
    unidad: '%',
    umbrales: [
      { color: 'Verde', rango: '≥ 95%', descripcion: 'Excelente nivel de servicio y disponibilidad de catálogo' },
      { color: 'Amarillo', rango: '85% – 94.9%', descripcion: 'Nivel regular; monitorear quiebres de inventario o retrasos' },
      { color: 'Rojo', rango: '< 85%', descripcion: 'Crítico; alto índice de órdenes incompletas o fallidas' },
    ],
    modulo: { nombre: 'Órdenes', ruta: '/ordenes' },
    recomendaciones: {
      Verde: 'El nivel de despacho y disponibilidad es óptimo. Mantener el ritmo de abastecimiento actual.',
      Amarillo: 'Se detectan órdenes pendientes o no completadas. Revisar disponibilidad de SKUs críticos en WMS.',
      Rojo: 'Cumplimiento crítico. Investigar inmediatamente pedidos estancados y coordinar con bodega.',
    },
  },
  2: {
    numero: 2,
    titulo: 'OTIF (On-Time In-Full)',
    descripcionCorta: 'Entregas a tiempo y completas',
    queMide: 'Porcentaje de pedidos entregados puntualmente (dentro de la fecha estimada) y con el 100% de los ítems requeridos.',
    formula: '(Órdenes a Tiempo y Completas / Total Órdenes con Fecha Estimada) × 100',
    sentido: 'mayor',
    meta: 95,
    unidad: '%',
    umbrales: [
      { color: 'Verde', rango: '≥ 95%', descripcion: 'Entregas a tiempo y completas dentro del estándar de excelencia' },
      { color: 'Amarillo', rango: '85% – 94.9%', descripcion: 'Demoras esporádicas en transporte o empaque' },
      { color: 'Rojo', rango: '< 85%', descripcion: 'Incumplimiento frecuente en tiempos de entrega prometidos' },
    ],
    modulo: { nombre: 'Fulfillment', ruta: '/fulfillment' },
    recomendaciones: {
      Verde: 'Excelente sincronización logística. Los transportistas y bodega cumplen los SLAs prometidos.',
      Amarillo: 'Existen demoras en rutas o empaque. Evaluar desempeño de operadores logísticos asignados.',
      Rojo: 'Grave desviación en entregas a tiempo. Auditar tiempos de tránsito y reprogramar despachos.',
    },
  },
  3: {
    numero: 3,
    titulo: 'Tasa de Cancelación',
    descripcionCorta: 'Órdenes canceladas',
    queMide: 'Proporción de pedidos cancelados (por falta de stock, rechazo de pago o anulación del cliente) sobre el total recibido.',
    formula: '(Órdenes Canceladas / Total Órdenes) × 100',
    sentido: 'menor',
    meta: 5,
    unidad: '%',
    umbrales: [
      { color: 'Verde', rango: '≤ 5%', descripcion: 'Cancelaciones dentro del margen de tolerancia operacional' },
      { color: 'Amarillo', rango: '5.01% – 10%', descripcion: 'Nivel de cancelaciones en observación; revisar inventario' },
      { color: 'Rojo', rango: '> 10%', descripcion: 'Cancelaciones excesivas; riesgo de fuga de ventas e insatisfacción' },
    ],
    modulo: { nombre: 'Órdenes', ruta: '/ordenes' },
    recomendaciones: {
      Verde: 'Tasa de cancelación saludable y bajo control. Sin anomalías de inventario o pagos.',
      Amarillo: 'Aumento moderado de cancelaciones. Verificar si se originan en falta de existencias físicas.',
      Rojo: 'Tasa de cancelación fuera de control. Revisar reservas de stock y pasarelas de pago.',
    },
  },
  4: {
    numero: 4,
    titulo: 'Tiempo Ciclo Pedido',
    descripcionCorta: 'Horas del pedido a la entrega',
    queMide: 'Tiempo promedio transcurrido en horas desde que la orden entra al sistema hasta su entrega efectiva al comprador.',
    formula: 'Promedio(FechaEntregaReal - FechaCreacion) en horas',
    sentido: 'menor',
    meta: 48,
    unidad: 'horas',
    umbrales: [
      { color: 'Verde', rango: '≤ 48 hrs', descripcion: 'Lead time logístico óptimo dentro de la ventana esperada' },
      { color: 'Amarillo', rango: '48.1 – 72 hrs', descripcion: 'Procesamiento lento; posibles cuellos de botella en picking' },
      { color: 'Rojo', rango: '> 72 hrs', descripcion: 'Tiempos de entrega excesivos fuera del SLA pactado' },
    ],
    modulo: { nombre: 'Fulfillment', ruta: '/fulfillment' },
    recomendaciones: {
      Verde: 'El flujo de preparación, empaque y despacho opera con alta velocidad y eficiencia.',
      Amarillo: 'Tiempo de ciclo elevado. Agilizar la generación y recolección de picklists en bodega.',
      Rojo: 'Entregas tardías superan las 72 horas. Priorizar órdenes en cola de empaque y coordinar recolección.',
    },
  },
  5: {
    numero: 5,
    titulo: 'Rotación de Inventario',
    descripcionCorta: 'Veces que rota el stock',
    queMide: 'Número de veces que el inventario disponible en almacén se vende y renueva a lo largo del período evaluado.',
    formula: 'Ventas Totales ($) / Valor Total de Inventario Físico ($)',
    sentido: 'mayor',
    meta: 4,
    unidad: 'veces',
    umbrales: [
      { color: 'Verde', rango: '≥ 4 veces', descripcion: 'Alta renovación de existencias; capital de trabajo dinámico' },
      { color: 'Amarillo', rango: '2 – 3.99 veces', descripcion: 'Rotación moderada; vigilar productos de lento movimiento' },
      { color: 'Rojo', rango: '< 2 veces', descripcion: 'Baja rotación; riesgo de sobrestock y costos de almacenaje' },
    ],
    modulo: { nombre: 'WMS / Bodega', ruta: '/wms' },
    recomendaciones: {
      Verde: 'Excelente rotación. El inventario se mueve rápidamente sin acumular stock ocioso.',
      Amarillo: 'Rotación regular. Analizar catálogo para identificar SKUs de baja demanda.',
      Rojo: 'Mercadería estancada. Evaluar ofertas o ajustar órdenes de compra para evitar sobrealmacenamiento.',
    },
  },
  6: {
    numero: 6,
    titulo: 'Tasa de Devolución RMA',
    descripcionCorta: 'Órdenes con devolución',
    queMide: 'Porcentaje de órdenes entregadas que generan una solicitud de devolución o reclamo de garantía (RMA).',
    formula: '(Total Solicitudes RMA / Órdenes Entregadas) × 100',
    sentido: 'menor',
    meta: 3,
    unidad: '%',
    umbrales: [
      { color: 'Verde', rango: '≤ 3%', descripcion: 'Mínimas devoluciones; alta conformidad de los clientes' },
      { color: 'Amarillo', rango: '3.01% – 7%', descripcion: 'Devoluciones en aumento; revisar calidad de empaque y productos' },
      { color: 'Rojo', rango: '> 7%', descripcion: 'Alto índice de reclamos; riesgo de defectos de origen o transporte' },
    ],
    modulo: { nombre: 'Devoluciones / RMA', ruta: '/rma' },
    recomendaciones: {
      Verde: 'Excelente calidad de empaque y despacho. Los productos llegan conformes a destino.',
      Amarillo: 'Se registran devoluciones por encima de lo deseado. Auditar motivos frecuentes en el módulo RMA.',
      Rojo: 'Tasa de reclamos crítica. Revisar proveedores con mayor incidencia de fallas y empaque protector.',
    },
  },
  7: {
    numero: 7,
    titulo: 'Precisión 3-Way Match',
    descripcionCorta: 'Documentos conciliados',
    queMide: 'Porcentaje de auditorías tridireccionales conformes entre Orden de Compra, Factura de Proveedor y Recepción en Bodega.',
    formula: '(Matches Conformes / Total Matches Auditados) × 100',
    sentido: 'mayor',
    meta: 98,
    unidad: '%',
    umbrales: [
      { color: 'Verde', rango: '≥ 98%', descripcion: 'Conciliación precisa; sin discrepancias contables ni de cantidades' },
      { color: 'Amarillo', rango: '90% – 97.9%', descripcion: 'Discrepancias leves en precio unitario o cantidades físicas' },
      { color: 'Rojo', rango: '< 90%', descripcion: 'Frecuentes descuadres entre compras, facturas y almacén' },
    ],
    modulo: { nombre: 'Compras', ruta: '/compras' },
    recomendaciones: {
      Verde: 'Conciliación contable y física impecable. Los pagos a proveedores proceden sin riesgo.',
      Amarillo: 'Discrepancias en trámite. Exigir notas de crédito o aclaraciones a proveedores antes de pagar.',
      Rojo: 'Descuadres sistemáticos. Congelar pagos a proveedores con discrepancias no aclaradas.',
    },
  },
  8: {
    numero: 8,
    titulo: 'Margen Comisión Marketplace',
    descripcionCorta: 'Comisión sobre ventas',
    queMide: 'Porcentaje promedio de comisión retenido por el marketplace sobre las ventas brutas de los vendedores asociados.',
    formula: '(Comisión Total Marketplace / Ventas Totales Brutas) × 100',
    sentido: 'mayor',
    meta: 10,
    unidad: '%',
    umbrales: [
      { color: 'Verde', rango: '≥ 9.5%', descripcion: 'Margen conforme con la tarifa estándar pactada (10%)' },
      { color: 'Amarillo', rango: '8% – 9.49%', descripcion: 'Margen por debajo de la meta por descuentos o comisiones reducidas' },
      { color: 'Rojo', rango: '< 8%', descripcion: 'Margen deficitario; ingresos del marketplace comprometidos' },
    ],
    modulo: { nombre: 'Liquidaciones', ruta: '/liquidaciones' },
    recomendaciones: {
      Verde: 'Ingresos por intermediación dentro de lo presupuestado (10% sobre volumen transaccionado).',
      Amarillo: 'Margen ligeramente diluido. Verificar acuerdos comerciales especiales con vendedores B2B.',
      Rojo: 'Captura de comisión por debajo del umbral mínimo. Auditar reglas de comisión y split de pagos.',
    },
  },
};

/**
 * Configuración de las 4 tarjetas de resumen ejecutivo del Dashboard
 */
export const STAT_CARDS_CONFIG: Record<StatCardKey, StatCardConfig> = {
  total_ordenes: {
    key: 'total_ordenes',
    titulo: 'Total de Órdenes Recibidas',
    subtitulo: 'Volumen consolidado de demanda',
    descripcionCorta: 'Volumen total del período',
    queMide: 'Registra la totalidad de pedidos generados en la plataforma dentro del período seleccionado, incluyendo órdenes completadas, en curso y canceladas.',
    modulo: { nombre: 'Órdenes', ruta: '/ordenes' },
  },
  ordenes_entregadas: {
    key: 'ordenes_entregadas',
    titulo: 'Órdenes Entregadas con Éxito',
    subtitulo: 'Cumplimiento efectivo de despacho',
    descripcionCorta: 'Completadas con éxito',
    queMide: 'Contabiliza los pedidos que finalizaron su ciclo de vida con entrega comprobada al comprador, determinando la tasa de cumplimiento del negocio.',
    modulo: { nombre: 'Órdenes', ruta: '/ordenes' },
  },
  canceladas: {
    key: 'canceladas',
    titulo: 'Órdenes Canceladas',
    subtitulo: 'Incidencias y pedidos anulados',
    descripcionCorta: 'Requieren atención',
    queMide: 'Pedidos interrumpidos por falta de inventario, problemas de pago o solicitud del cliente. Su volumen impacta directamente en el Fill Rate y satisfacción.',
    modulo: { nombre: 'Órdenes', ruta: '/ordenes' },
  },
  comision_generada: {
    key: 'comision_generada',
    titulo: 'Comisión Generada por Marketplace',
    subtitulo: 'Monetización e intermediación comercial',
    descripcionCorta: 'Ingreso del marketplace',
    queMide: 'Ingreso acumulado por el marketplace derivado de la comisión porcentual (10%) aplicada a las ventas brutas de los vendedores de la plataforma.',
    modulo: { nombre: 'Liquidaciones', ruta: '/liquidaciones' },
  },
};

/* ─── Tipos y constantes unificadas para visibilidad del Dashboard ─── */

/** Clave única para cada tarjeta del Dashboard (4 stat + 8 kpi) */
export type KpiCardKey = `kpi_${number}`;
export type DashboardCardKey = StatCardKey | KpiCardKey;

/** Las 4 claves de resumen ejecutivo, en orden de renderizado */
export const STAT_CARD_KEYS: StatCardKey[] = [
  'total_ordenes',
  'ordenes_entregadas',
  'canceladas',
  'comision_generada',
];

/** Las 8 claves KPI, derivadas de KPI_DETAILS_CONFIG */
export const KPI_CARD_KEYS: KpiCardKey[] = Object.keys(KPI_DETAILS_CONFIG)
  .map(n => `kpi_${n}` as KpiCardKey);

/** Todas las 12 claves del Dashboard */
export const ALL_DASHBOARD_CARD_KEYS: DashboardCardKey[] = [
  ...STAT_CARD_KEYS,
  ...KPI_CARD_KEYS,
];

/** Mapa clave → etiqueta legible (single source of truth para nombres) */
export const CARD_LABELS_MAP: Record<DashboardCardKey, string> = {
  ...Object.fromEntries(
    STAT_CARD_KEYS.map(k => [k, STAT_CARDS_CONFIG[k].titulo])
  ),
  ...Object.fromEntries(
    Object.entries(KPI_DETAILS_CONFIG).map(([n, cfg]) => [`kpi_${n}`, cfg.titulo])
  ),
} as Record<DashboardCardKey, string>;

/** Mapa clave → nombre corto (coherente con las tarjetas del Dashboard) */
export const CARD_SHORT_NAMES_MAP: Record<DashboardCardKey, string> = {
  total_ordenes: 'Total Órdenes',
  ordenes_entregadas: 'Órdenes Entregadas',
  canceladas: 'Canceladas',
  comision_generada: 'Comisión Generada',
  kpi_1: 'Fill Rate',
  kpi_2: 'OTIF (On-Time In-Full)',
  kpi_3: 'Tasa de Cancelación',
  kpi_4: 'Tiempo Ciclo Pedido',
  kpi_5: 'Rotación de Inventario',
  kpi_6: 'Tasa de Devolución RMA',
  kpi_7: 'Precisión 3-Way Match',
  kpi_8: 'Margen Comisión',
};

/** Mapa clave → descripción corta para el selector modal */
export const CARD_DESCRIPTIONS_MAP: Record<DashboardCardKey, string> = {
  total_ordenes: STAT_CARDS_CONFIG.total_ordenes.descripcionCorta ?? 'Volumen total del período',
  ordenes_entregadas: STAT_CARDS_CONFIG.ordenes_entregadas.descripcionCorta ?? 'Completadas con éxito',
  canceladas: STAT_CARDS_CONFIG.canceladas.descripcionCorta ?? 'Requieren atención',
  comision_generada: STAT_CARDS_CONFIG.comision_generada.descripcionCorta ?? 'Ingreso del marketplace',
  ...Object.fromEntries(
    Object.entries(KPI_DETAILS_CONFIG).map(([n, cfg]) => [`kpi_${n}`, cfg.descripcionCorta ?? cfg.titulo])
  ),
} as Record<DashboardCardKey, string>;
