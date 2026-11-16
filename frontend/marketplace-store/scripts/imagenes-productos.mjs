// Copia a public/productos las ilustraciones 3D (Fluent Emoji de Microsoft, licencia MIT)
// que usa src/lib/product-image.ts.
//
// Uso:
//   npm pack @lobehub/fluent-emoji-3d && tar xzf lobehub-fluent-emoji-3d-*.tgz
//   node scripts/imagenes-productos.mjs package/assets
import { copyFileSync, existsSync, mkdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { ALL_EMOJIS, emojiFile, productEmoji } from "../src/lib/product-image.ts";

const src = process.argv[2];
if (!src) throw new Error("Indica la carpeta assets del paquete @lobehub/fluent-emoji-3d");
const out = join(import.meta.dirname, "..", "public", "productos");
mkdirSync(out, { recursive: true });

const missing = [];
for (const e of ALL_EMOJIS) {
  const name = emojiFile(e);
  const withFe0f = [...e].map((c) => c.codePointAt(0).toString(16)).join("-");
  const from = [name, withFe0f, `${name}-fe0f`].map((n) => join(src, `${n}.webp`)).find(existsSync);
  if (from) copyFileSync(from, join(out, `${name}.webp`));
  else missing.push(`${e} (${name})`);
}

const { header, rows } = JSON.parse(readFileSync(join(import.meta.dirname, "..", "data", "catalogo.json"), "utf8"));
const col = (re) => header.findIndex((h) => re.test(h));
const [n, c, s] = [col(/nombre/i), col(/^categor/i), col(/subcat/i)];
const count = {};
for (const r of rows) {
  const e = productEmoji({ name: r[n], category: r[c], subcategory: r[s] });
  count[e] = (count[e] ?? 0) + 1;
}
console.log(`${ALL_EMOJIS.length - missing.length} imágenes copiadas en public/productos.`);
console.log("Productos por imagen:", Object.entries(count).sort((a, b) => b[1] - a[1]).map(([e, k]) => `${e}${k}`).join(" "));
if (missing.length) console.log("Faltan:", missing.join(", "));
