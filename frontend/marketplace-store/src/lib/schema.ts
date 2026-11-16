// Esquema de la base de datos (ERP + OMS). Se crea automáticamente la primera vez.
export const SCHEMA_SQL = `
CREATE TABLE IF NOT EXISTS products (
  sku            TEXT PRIMARY KEY,
  name           TEXT NOT NULL,
  category       TEXT NOT NULL,
  subcategory    TEXT,
  price_b2c      NUMERIC(12,2) NOT NULL,
  price_b2b      NUMERIC(12,2) NOT NULL,
  stock          INTEGER NOT NULL DEFAULT 0 CHECK (stock >= 0),
  stock_min      INTEGER NOT NULL DEFAULT 0,
  supplier       TEXT,
  origin_country TEXT,
  weight_kg      NUMERIC(10,3),
  length_cm      NUMERIC(10,2),
  width_cm       NUMERIC(10,2),
  height_cm      NUMERIC(10,2),
  unit           TEXT,
  status         TEXT NOT NULL DEFAULT 'Activo',
  ean            TEXT,
  description    TEXT,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS products_category_idx ON products (category);

CREATE TABLE IF NOT EXISTS orders (
  id              SERIAL PRIMARY KEY,
  code            TEXT UNIQUE,
  channel         TEXT NOT NULL CHECK (channel IN ('B2C','B2B')),
  customer_name   TEXT NOT NULL,
  customer_email  TEXT NOT NULL,
  customer_phone  TEXT,
  company         TEXT,
  tax_id          TEXT,
  address         TEXT NOT NULL,
  city            TEXT NOT NULL,
  payment_method  TEXT NOT NULL,
  payment_status  TEXT NOT NULL,
  subtotal        NUMERIC(12,2) NOT NULL,
  tax_amount      NUMERIC(12,2) NOT NULL DEFAULT 0,
  shipping_charge NUMERIC(12,2) NOT NULL DEFAULT 0,
  total           NUMERIC(12,2) NOT NULL,
  logistics_cost  NUMERIC(12,2) NOT NULL DEFAULT 0,
  total_weight_kg NUMERIC(12,3) NOT NULL DEFAULT 0,
  status          TEXT NOT NULL,
  carrier         TEXT,
  tracking        TEXT,
  promised_at     TIMESTAMPTZ NOT NULL,
  shipped_at      TIMESTAMPTZ,
  delivered_at    TIMESTAMPTZ,
  return_reason   TEXT,
  nps_score       INTEGER CHECK (nps_score BETWEEN 0 AND 10),
  nps_comment     TEXT,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS order_items (
  id            SERIAL PRIMARY KEY,
  order_id      INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
  sku           TEXT NOT NULL REFERENCES products(sku),
  name          TEXT NOT NULL,
  qty_requested INTEGER NOT NULL,
  qty           INTEGER NOT NULL,
  unit_price    NUMERIC(12,2) NOT NULL,
  line_total    NUMERIC(12,2) NOT NULL
);
CREATE INDEX IF NOT EXISTS order_items_order_idx ON order_items (order_id);

CREATE TABLE IF NOT EXISTS order_events (
  id         SERIAL PRIMARY KEY,
  order_id   INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
  status     TEXT NOT NULL,
  note       TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS order_events_order_idx ON order_events (order_id);

CREATE TABLE IF NOT EXISTS inventory_movements (
  id         SERIAL PRIMARY KEY,
  sku        TEXT NOT NULL REFERENCES products(sku),
  type       TEXT NOT NULL,
  qty        INTEGER NOT NULL,
  reference  TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reviews (
  id         SERIAL PRIMARY KEY,
  sku        TEXT NOT NULL REFERENCES products(sku),
  author     TEXT NOT NULL,
  rating     INTEGER NOT NULL CHECK (rating BETWEEN 1 AND 5),
  comment    TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS reviews_sku_idx ON reviews (sku);

-- Integración con el ERP/OMS externo (API .NET de LOGIMKT)
ALTER TABLE products ADD COLUMN IF NOT EXISTS erp_id TEXT;
ALTER TABLE products ADD COLUMN IF NOT EXISTS erp_synced_at TIMESTAMPTZ;
ALTER TABLE products ADD COLUMN IF NOT EXISTS image_url TEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS tax_amount NUMERIC(12,2) NOT NULL DEFAULT 0;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS erp_order_id TEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS erp_order_number TEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS erp_status TEXT;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS erp_error TEXT;

CREATE TABLE IF NOT EXISTS erp_sync_log (
  id         SERIAL PRIMARY KEY,
  kind       TEXT NOT NULL,
  ok         BOOLEAN NOT NULL,
  detail     TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
`;
