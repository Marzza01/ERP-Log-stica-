import "server-only";
import { query } from "./db";

export type Product = {
  sku: string;
  name: string;
  category: string;
  subcategory: string | null;
  price_b2c: number;
  price_b2b: number;
  stock: number;
  stock_min: number;
  supplier: string | null;
  origin_country: string | null;
  weight_kg: number | null;
  length_cm: number | null;
  width_cm: number | null;
  height_cm: number | null;
  unit: string | null;
  status: string;
  ean: string | null;
  description: string | null;
  image_url: string | null;
  rating: number | null;
  reviews: number;
};

const SELECT = `
  SELECT p.*, r.rating, COALESCE(r.reviews, 0)::int AS reviews
  FROM products p
  LEFT JOIN (SELECT sku, avg(rating)::float AS rating, count(*)::int AS reviews FROM reviews GROUP BY sku) r ON r.sku = p.sku`;

export type CatalogFilters = {
  q?: string;
  cat?: string;
  sub?: string;
  min?: number;
  max?: number;
  available?: boolean;
  sort?: string;
  page?: number;
};

export const PAGE_SIZE = 24;

export async function searchProducts(f: CatalogFilters, priceCol: "price_b2c" | "price_b2b") {
  const where: string[] = ["p.status = 'Activo'"];
  const params: unknown[] = [];
  const add = (sql: string, v: unknown) => {
    params.push(v);
    where.push(sql.replace("?", `$${params.length}`));
  };
  if (f.q) {
    params.push(`%${f.q}%`);
    const i = params.length;
    where.push(`(p.name ILIKE $${i} OR p.sku ILIKE $${i} OR p.description ILIKE $${i})`);
  }
  if (f.cat) add("p.category = ?", f.cat);
  if (f.sub) add("p.subcategory = ?", f.sub);
  if (f.min !== undefined) add(`p.${priceCol} >= ?`, f.min);
  if (f.max !== undefined) add(`p.${priceCol} <= ?`, f.max);
  if (f.available) where.push("p.stock > 0");
  const order =
    f.sort === "precio_asc" ? `p.${priceCol} ASC` :
    f.sort === "precio_desc" ? `p.${priceCol} DESC` :
    f.sort === "nombre" ? "p.name ASC" :
    f.sort === "valoracion" ? "r.rating DESC NULLS LAST, p.name" :
    "p.sku ASC";
  const page = Math.max(1, f.page ?? 1);
  const whereSql = where.join(" AND ");
  const [{ n }] = await query<{ n: number }>(
    `SELECT count(*)::int AS n FROM products p WHERE ${whereSql}`, params,
  );
  const rows = await query<Product>(
    `${SELECT} WHERE ${whereSql} ORDER BY ${order} LIMIT ${PAGE_SIZE} OFFSET ${(page - 1) * PAGE_SIZE}`, params,
  );
  return { rows, total: n, page, pages: Math.max(1, Math.ceil(n / PAGE_SIZE)) };
}

export async function getProduct(sku: string) {
  const [p] = await query<Product>(`${SELECT} WHERE p.sku = $1`, [sku]);
  return p ?? null;
}

export async function getCategories() {
  return query<{ category: string; n: number; subs: string[] }>(`
    SELECT category, count(*)::int AS n, array_agg(DISTINCT subcategory) FILTER (WHERE subcategory IS NOT NULL) AS subs
    FROM products WHERE status = 'Activo' GROUP BY category ORDER BY count(*) DESC`);
}

export async function getFeatured(limit = 8) {
  // Destacados: los mejor valorados y, si no hay reseñas, los de mayor stock.
  return query<Product>(
    `${SELECT} WHERE p.status = 'Activo' AND p.stock > 0
     ORDER BY r.rating DESC NULLS LAST, p.stock DESC LIMIT $1`, [limit],
  );
}

export async function getBestSellers(limit = 4) {
  return query<Product>(
    `${SELECT} JOIN (SELECT sku, sum(qty) AS sold FROM order_items GROUP BY sku) s ON s.sku = p.sku
     WHERE p.status = 'Activo' ORDER BY s.sold DESC LIMIT $1`, [limit],
  );
}

export async function getRelated(p: Product, limit = 4) {
  return query<Product>(
    `${SELECT} WHERE p.status = 'Activo' AND p.category = $1 AND p.sku <> $2 AND p.stock > 0
     ORDER BY (p.subcategory = $3) DESC, p.sku LIMIT $4`,
    [p.category, p.sku, p.subcategory, limit],
  );
}

export async function getReviews(sku: string) {
  return query<{ id: number; author: string; rating: number; comment: string | null; created_at: Date }>(
    "SELECT id, author, rating, comment, created_at FROM reviews WHERE sku = $1 ORDER BY id DESC LIMIT 50", [sku],
  );
}
