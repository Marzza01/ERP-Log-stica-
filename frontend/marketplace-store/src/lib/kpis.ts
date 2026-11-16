import "server-only";
import { query } from "./db";

export type Light = "verde" | "amarillo" | "rojo" | "sin-datos";

export type Kpi = {
  id: string;
  name: string;
  value: number | null;
  display: string;
  formula: string;
  goal: string;
  light: Light;
  scor: string;
};

/**
 * Semáforo: umbral verde y umbral amarillo. "higher" indica si un valor alto es bueno.
 * Puedes ajustar estas metas a las que defina tu docente.
 */
const T = {
  otif: { green: 95, yellow: 85, higher: true },
  lead: { green: 72, yellow: 120, higher: false }, // horas
  fill: { green: 98, yellow: 90, higher: true },
  returns: { green: 3, yellow: 6, higher: false },
  logistic: { green: 4, yellow: 6, higher: false }, // $ por pedido
  rotation: { green: 4, yellow: 2, higher: true }, // veces al año (proyectado)
  oms: { green: 24, yellow: 48, higher: false }, // horas
  nps: { green: 50, yellow: 20, higher: true },
};

function light(v: number | null, t: { green: number; yellow: number; higher: boolean }): Light {
  if (v === null || Number.isNaN(v)) return "sin-datos";
  if (t.higher) return v >= t.green ? "verde" : v >= t.yellow ? "amarillo" : "rojo";
  return v <= t.green ? "verde" : v <= t.yellow ? "amarillo" : "rojo";
}

const fmt = (v: number | null, suffix: string, digits = 1) =>
  v === null ? "Sin datos" : `${v.toLocaleString("es-SV", { maximumFractionDigits: digits })}${suffix}`;

export async function getKpis() {
  const [s] = await query<{
    delivered: number; otif_ok: number; lead_h: number | null; returned: number; logistic: number | null;
    req: number | null; sent: number | null; oms_h: number | null; nps_n: number; promoters: number; detractors: number;
    orders: number; revenue: number | null; ticket: number | null; ticket_b2c: number | null; ticket_b2b: number | null;
    b2c: number; b2b: number;
  }>(`
    WITH valid AS (SELECT * FROM orders WHERE status <> 'Cancelado'),
    delivered AS (SELECT * FROM valid WHERE delivered_at IS NOT NULL),
    full_orders AS (
      SELECT o.id, bool_and(i.qty >= i.qty_requested) AS in_full
      FROM delivered o JOIN order_items i ON i.order_id = o.id GROUP BY o.id
    ),
    proc AS (
      SELECT o.id, EXTRACT(EPOCH FROM (min(e2.created_at) - min(e1.created_at))) / 3600 AS h
      FROM orders o
      JOIN order_events e1 ON e1.order_id = o.id AND e1.status = 'Recibido'
      JOIN order_events e2 ON e2.order_id = o.id AND e2.status = 'Enviado'
      GROUP BY o.id
    )
    SELECT
      (SELECT count(*) FROM delivered)::int AS delivered,
      (SELECT count(*) FROM delivered d JOIN full_orders f ON f.id = d.id WHERE d.delivered_at <= d.promised_at AND f.in_full)::int AS otif_ok,
      (SELECT avg(EXTRACT(EPOCH FROM (delivered_at - created_at)) / 3600) FROM delivered)::float AS lead_h,
      (SELECT count(*) FROM valid WHERE status = 'Devuelto')::int AS returned,
      (SELECT avg(logistics_cost) FROM valid)::float AS logistic,
      (SELECT sum(i.qty_requested) FROM order_items i JOIN valid o ON o.id = i.order_id)::float AS req,
      (SELECT sum(i.qty) FROM order_items i JOIN valid o ON o.id = i.order_id)::float AS sent,
      (SELECT avg(h) FROM proc)::float AS oms_h,
      (SELECT count(*) FROM orders WHERE nps_score IS NOT NULL)::int AS nps_n,
      (SELECT count(*) FROM orders WHERE nps_score >= 9)::int AS promoters,
      (SELECT count(*) FROM orders WHERE nps_score <= 6)::int AS detractors,
      (SELECT count(*) FROM valid)::int AS orders,
      (SELECT sum(total) FROM valid WHERE status <> 'Devuelto')::float AS revenue,
      (SELECT avg(total) FROM valid)::float AS ticket,
      (SELECT avg(total) FROM valid WHERE channel = 'B2C')::float AS ticket_b2c,
      (SELECT avg(total) FROM valid WHERE channel = 'B2B')::float AS ticket_b2b,
      (SELECT count(*) FROM valid WHERE channel = 'B2C')::int AS b2c,
      (SELECT count(*) FROM valid WHERE channel = 'B2B')::int AS b2b
  `);

  // Rotación de inventario = unidades vendidas ÷ inventario promedio, proyectada a un año.
  const [rot] = await query<{ sold: number; stock: number; days: number }>(`
    SELECT
      COALESCE((SELECT -sum(qty) FROM inventory_movements WHERE type = 'SALIDA_VENTA'), 0)::float
        - COALESCE((SELECT sum(qty) FROM inventory_movements WHERE type IN ('ENTRADA_DEVOLUCION','ENTRADA_CANCELACION')), 0)::float AS sold,
      (SELECT COALESCE(sum(stock),0) FROM products)::float AS stock,
      GREATEST(1, EXTRACT(EPOCH FROM (now() - COALESCE((SELECT min(created_at) FROM orders), now()))) / 86400)::float AS days
  `);
  const avgStock = rot.stock + rot.sold / 2;
  const rotation = s.orders && avgStock > 0 ? (rot.sold / avgStock) * (365 / rot.days) : null;

  const pct = (a: number, b: number) => (b > 0 ? (a / b) * 100 : null);
  const otif = pct(s.otif_ok, s.delivered);
  const fill = s.req ? pct(s.sent ?? 0, s.req) : null;
  const returns = pct(s.returned, s.delivered);
  const nps = s.nps_n ? ((s.promoters - s.detractors) / s.nps_n) * 100 : null;

  const [csat] = await query<{ n: number; ok: number }>(
    "SELECT count(*)::int AS n, (count(*) FILTER (WHERE rating >= 4))::int AS ok FROM reviews",
  );
  const csatPct = pct(csat.ok, csat.n);

  const kpis: Kpi[] = [
    { id: "otif", name: "OTIF (On Time In Full)", value: otif, display: fmt(otif, "%"), formula: "Pedidos entregados a tiempo y completos ÷ pedidos entregados × 100", goal: "≥ 95%", light: light(otif, T.otif), scor: "Deliver" },
    { id: "lead", name: "Lead Time del pedido", value: s.lead_h, display: fmt(s.lead_h, " h"), formula: "Promedio (fecha de entrega − fecha del pedido)", goal: "≤ 72 h", light: light(s.lead_h, T.lead), scor: "Deliver" },
    { id: "fill", name: "Fill Rate", value: fill, display: fmt(fill, "%"), formula: "Unidades surtidas ÷ unidades solicitadas × 100", goal: "≥ 98%", light: light(fill, T.fill), scor: "Source / Make" },
    { id: "returns", name: "Tasa de devoluciones", value: returns, display: fmt(returns, "%"), formula: "Pedidos devueltos ÷ pedidos entregados × 100", goal: "≤ 3%", light: light(returns, T.returns), scor: "Return" },
    { id: "logistic", name: "Costo logístico por pedido", value: s.logistic, display: s.logistic === null ? "Sin datos" : `$${s.logistic.toFixed(2)}`, formula: "Costo total de envío ÷ número de pedidos", goal: "≤ $4.00", light: light(s.logistic, T.logistic), scor: "Deliver" },
    { id: "rotation", name: "Rotación de inventario", value: rotation, display: fmt(rotation, " veces/año", 2), formula: "Unidades vendidas ÷ inventario promedio (proyectado a 1 año)", goal: "≥ 4 veces/año", light: light(rotation, T.rotation), scor: "Plan / Source" },
    { id: "oms", name: "Tiempo de procesamiento OMS", value: s.oms_h, display: fmt(s.oms_h, " h"), formula: "Promedio (hora de envío − hora de recepción del pedido)", goal: "≤ 24 h", light: light(s.oms_h, T.oms), scor: "Make" },
    {
      id: "nps", name: "NPS / CSAT", value: nps,
      display: `NPS ${nps === null ? "—" : nps.toFixed(0)} · CSAT ${csatPct === null ? "—" : csatPct.toFixed(0) + "%"}`,
      formula: "NPS = % promotores (9-10) − % detractores (0-6). CSAT = reseñas de 4-5 estrellas ÷ total de reseñas",
      goal: "NPS ≥ 50 · CSAT ≥ 85%", light: light(nps, T.nps), scor: "Deliver / Return",
    },
  ];

  return {
    kpis,
    summary: {
      orders: s.orders, revenue: s.revenue ?? 0, ticket: s.ticket, ticketB2C: s.ticket_b2c, ticketB2B: s.ticket_b2b,
      b2c: s.b2c, b2b: s.b2b, delivered: s.delivered, npsResponses: s.nps_n, reviews: csat.n,
    },
  };
}
