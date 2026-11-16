import "server-only";
import { SCHEMA_SQL } from "./schema";
import { importRows } from "./catalog-import";
import { parseRows } from "./catalog-parser";
import catalogo from "../../data/catalogo.json";

/** Conexión mínima que usan el ERP y el OMS (compatible con Postgres/Neon y con PGlite local). */
export type DbClient = {
  query<T = Record<string, unknown>>(text: string, params?: unknown[]): Promise<{ rows: T[] }>;
};

type Driver = {
  query: DbClient["query"];
  exec(sql: string): Promise<void>;
  transaction<T>(fn: (c: DbClient) => Promise<T>): Promise<T>;
};

declare global {
  var __logimktDriver: Promise<Driver> | undefined;
  var __logimktReady: Promise<void> | undefined;
}

/** Producción (Vercel): Postgres en Neon mediante DATABASE_URL. */
async function pgDriver(url: string): Promise<Driver> {
  const { Pool, types } = await import("pg");
  // NUMERIC y COUNT llegan como texto desde Postgres: los convertimos a número.
  types.setTypeParser(1700, (v) => parseFloat(v));
  types.setTypeParser(20, (v) => parseInt(v, 10));
  const pool = new Pool({ connectionString: url, max: 5 });
  return {
    query: async <T,>(text: string, params: unknown[] = []) => ({ rows: (await pool.query(text, params)).rows as T[] }),
    exec: async (sql) => {
      await pool.query(sql);
    },
    transaction: async (fn) => {
      const c = await pool.connect();
      try {
        await c.query("BEGIN");
        const out = await fn({ query: async <T,>(t: string, p: unknown[] = []) => ({ rows: (await c.query(t, p)).rows as T[] }) });
        await c.query("COMMIT");
        return out;
      } catch (e) {
        await c.query("ROLLBACK");
        throw e;
      } finally {
        c.release();
      }
    },
  };
}

/** Modo local sin configuración: base de datos Postgres embebida (PGlite) guardada en la carpeta .data/ */
async function pgliteDriver(): Promise<Driver> {
  const { PGlite, types } = await import("@electric-sql/pglite");
  const { mkdirSync } = await import("node:fs");
  const dir = `${process.cwd()}/.data/pglite`;
  mkdirSync(dir, { recursive: true });
  const db = await PGlite.create(dir, {
    parsers: {
      [types.NUMERIC]: (v: string) => parseFloat(v),
      [types.INT8]: (v: string) => parseInt(v, 10),
    },
  });
  return {
    query: async <T,>(text: string, params: unknown[] = []) => ({ rows: (await db.query<T>(text, params)).rows }),
    exec: async (sql) => {
      await db.exec(sql);
    },
    transaction: (fn) =>
      db.transaction((t) => fn({ query: async <T,>(q: string, p: unknown[] = []) => ({ rows: (await t.query<T>(q, p)).rows }) })),
  };
}

function driver(): Promise<Driver> {
  if (!globalThis.__logimktDriver) {
    const url = process.env.DATABASE_URL;
    if (!url && process.env.VERCEL) {
      throw new Error("Falta la variable de entorno DATABASE_URL (cadena de conexión de Neon).");
    }
    globalThis.__logimktDriver = url ? pgDriver(url) : pgliteDriver();
  }
  return globalThis.__logimktDriver;
}

async function init() {
  const d = await driver();
  try {
    await d.exec(SCHEMA_SQL);
  } catch (e) {
    // Si dos instancias crean las tablas a la vez, una puede fallar: lo ignoramos.
    const code = (e as { code?: string }).code;
    if (code !== "23505" && code !== "42P07") throw e;
  }
  // Carga inicial automática del catálogo de 500 productos si la tabla está vacía.
  const { rows } = await d.query<{ n: number }>("SELECT COUNT(*)::int AS n FROM products");
  if (rows[0].n === 0) {
    const parsed = parseRows(catalogo.header, catalogo.rows);
    await d.transaction((c) => importRows(c, parsed.rows, "CARGA_INICIAL"));
  }
}

export function ready(): Promise<void> {
  if (!globalThis.__logimktReady) {
    globalThis.__logimktReady = init().catch((e) => {
      globalThis.__logimktReady = undefined;
      throw e;
    });
  }
  return globalThis.__logimktReady;
}

export async function query<T = Record<string, unknown>>(text: string, params: unknown[] = []): Promise<T[]> {
  await ready();
  return (await (await driver()).query<T>(text, params)).rows;
}

/** Ejecuta varias consultas dentro de una transacción (todo o nada). */
export async function tx<T>(fn: (c: DbClient) => Promise<T>): Promise<T> {
  await ready();
  return (await driver()).transaction(fn);
}
