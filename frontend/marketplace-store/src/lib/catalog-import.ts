import type { DbClient } from "./db";
import type { CatalogRow } from "./catalog-parser";

/**
 * Carga masiva del ERP: inserta productos nuevos y actualiza datos y precios de los existentes
 * en una sola consulta. Registra el movimiento de inventario inicial de cada SKU nuevo.
 */
export async function importRows(c: DbClient, rows: CatalogRow[], movementType = "CARGA_MASIVA") {
  if (!rows.length) return { created: 0, updated: 0 };
  const res = await c.query<{ sku: string; inserted: boolean }>(
    `INSERT INTO products (sku,name,category,subcategory,price_b2c,price_b2b,stock,stock_min,supplier,origin_country,
       weight_kg,length_cm,width_cm,height_cm,unit,status,ean,description,image_url)
     SELECT sku,name,category,subcategory,price_b2c,price_b2b,stock,stock_min,supplier,origin_country,
       weight_kg,length_cm,width_cm,height_cm,unit,status,ean,description,image_url
     FROM jsonb_to_recordset($1::jsonb) AS x(sku text,name text,category text,subcategory text,price_b2c numeric,
       price_b2b numeric,stock int,stock_min int,supplier text,origin_country text,weight_kg numeric,length_cm numeric,
       width_cm numeric,height_cm numeric,unit text,status text,ean text,description text,image_url text)
     ON CONFLICT (sku) DO UPDATE SET name=EXCLUDED.name, category=EXCLUDED.category, subcategory=EXCLUDED.subcategory,
       price_b2c=EXCLUDED.price_b2c, price_b2b=EXCLUDED.price_b2b, stock_min=EXCLUDED.stock_min,
       supplier=EXCLUDED.supplier, origin_country=EXCLUDED.origin_country, weight_kg=EXCLUDED.weight_kg,
       length_cm=EXCLUDED.length_cm, width_cm=EXCLUDED.width_cm, height_cm=EXCLUDED.height_cm, unit=EXCLUDED.unit,
       status=EXCLUDED.status, ean=EXCLUDED.ean, description=EXCLUDED.description,
       image_url=COALESCE(EXCLUDED.image_url, products.image_url), updated_at=now()
     RETURNING sku, (xmax = 0) AS inserted`,
    [JSON.stringify(rows)],
  );
  // El stock solo se toma del archivo para productos nuevos; en los existentes se conserva el stock real
  // (que ya refleja ventas, devoluciones y recepciones).
  const newSkus = new Set(res.rows.filter((r) => r.inserted).map((r) => r.sku));
  const moves = rows.filter((r) => newSkus.has(r.sku)).map((r) => ({ sku: r.sku, stock: r.stock }));
  if (moves.length) {
    await c.query(
      `INSERT INTO inventory_movements (sku,type,qty,reference)
       SELECT sku, $2, stock, 'Importación de catálogo' FROM jsonb_to_recordset($1::jsonb) AS x(sku text, stock int)`,
      [JSON.stringify(moves), movementType],
    );
  }
  const created = res.rows.filter((r) => r.inserted).length;
  return { created, updated: res.rows.length - created };
}
