// Imagen de cada producto. Se usa tanto en el servidor como en el navegador.
//
// Prioridad:
//   1. products.image_url: la foto que manda el ERP (campo ImagenUrl) o la columna "Imagen" del Excel.
//   2. Una ilustración 3D según el tipo de producto (nombre y subcategoría), guardada en /public/productos.
//      Son los Fluent Emoji 3D de Microsoft (licencia MIT), copiados con `node scripts/imagenes-productos.mjs`.

const RULES: [RegExp, string][] = [
  // Bebé (antes que "monitor", "termómetro", "silla", "leche")
  [/bebe y maternidad\|.*(biberon|chupon|formula)/, "🍼"],
  [/bebe y maternidad\|.*juguete/, "🧸"],
  [/bebe y maternidad\|.*termometro/, "🌡️"],
  [/bebe y maternidad\|.*(almohada)/, "🛏️"],
  [/bebe y maternidad\|.*(mochila)/, "🎒"],
  [/bebe y maternidad\|/, "👶"],
  // Servicios digitales
  [/netflix|disney|youtube|amazon/, "🎬"],
  [/spotify/, "🎵"],
  [/tarjeta regalo/, "🎁"],
  [/tarjeta prepago|pasarela pago/, "💳"],
  [/firma electronica/, "✍️"],
  [/curso|certificacion/, "🎓"],
  [/api email/, "📧"],
  [/api sms|slack|chatbot/, "💬"],
  [/chatgpt|claude|ia generativa|ia y automatizacion/, "🤖"],
  [/s3|cdn|vps|backup cloud|dropbox|hosting/, "☁️"],
  [/antivirus|vpn|ssl/, "🔒"],
  [/dominio web/, "🌐"],
  [/crm|erp modulo|quickbooks/, "📊"],
  [/canva|figma|adobe|autocad/, "🎨"],
  [/zoom/, "📹"],
  [/software\||servicios/, "💼"],
  // Electrónica
  [/smartwatch|banda actividad|reloj gps/, "⌚"],
  [/e-reader|kindle/, "📖"],
  [/tablet grafica|pen stylus/, "🖊️"],
  [/smartphone|tablet/, "📱"],
  [/mini pc|nuc intel|pc |all-in-one|workstation|servidor torre|monitor/, "🖥️"],
  [/macbook|laptop |chromebook/, "💻"],
  [/mouse/, "🖱️"],
  [/teclado musical|midi/, "🎹"],
  [/interfaz audio|amplificador/, "🎛️"],
  [/teclado/, "⌨️"],
  [/auricular|audifono|orejeras/, "🎧"],
  [/bateria portatil|ups /, "🔋"],
  [/adaptador|hub usb|cable|cargador|enchufe/, "🔌"],
  [/disco duro|ssd|memoria ram|lector tarjetas/, "💾"],
  [/impresora|escaner/, "🖨️"],
  [/filamento/, "🧵"],
  [/router|switch/, "📡"],
  [/webcam|camara mirrorless|lente|gopro|tripode/, "📷"],
  [/camara seguridad|videocamara/, "📹"],
  [/karaoke|microfono/, "🎙️"],
  [/consola|control inalambrico/, "🎮"],
  [/dron/, "🚁"],
  [/proyector/, "📽️"],
  [/receptor|reproductor|smart tv/, "📺"],
  [/bocina|soundbar|sistema audio|altavoz/, "🔊"],
  [/ring light|foco|lampara/, "💡"],
  [/cerradura/, "🔐"],
  [/robot aspirador/, "🤖"],
  [/termostato|termometro/, "🌡️"],
  [/limpiador pantallas/, "🧴"],
  // Calzado y seguridad
  [/tacones/, "👠"],
  [/ballerinas|mocasines/, "🥿"],
  [/bota lluvia/, "👢"],
  [/botas|zapato dielectrico/, "🥾"],
  [/sandalia|ojotas|crocs/, "🩴"],
  [/sneakers|tenis|zapatillas/, "👟"],
  [/zapato/, "👞"],
  [/casco/, "⛑️"],
  [/arnes|chaleco reflectivo/, "🦺"],
  [/extintor/, "🧯"],
  [/guante/, "🧤"],
  [/kit primeros/, "🩹"],
  [/linterna/, "🔦"],
  [/mascarilla n95/, "😷"],
  [/gafas seguridad/, "🥽"],
  // Ropa y accesorios
  [/abrigo|trench|chaqueta|blazer|chaleco/, "🧥"],
  [/traje bano|bra deportivo/, "👙"],
  [/traje formal|camisa|polo/, "👔"],
  [/vestido|falda|mono jumper/, "👗"],
  [/shorts|bermuda/, "🩳"],
  [/jean|pantalon|jogger|leggings/, "👖"],
  [/blusa|crop|top |conjunto|cardigan|ropa mujer/, "👚"],
  [/camiseta|jersey|sweater|sudadera|pijama|ropa hombre/, "👕"],
  [/anillo/, "💍"],
  [/cartera/, "👛"],
  [/bolso|cinturon/, "👜"],
  [/bufanda/, "🧣"],
  [/collar|pulsera/, "📿"],
  [/gafas sol/, "🕶️"],
  [/gorra/, "🧢"],
  [/mochila/, "🎒"],
  [/paraguas/, "☂️"],
  [/sombrero/, "👒"],
  [/reloj pared/, "🕰️"],
  [/reloj|correa/, "⌚"],
  // Alimentos y bebidas
  [/agua coco/, "🥥"],
  [/agua/, "💧"],
  [/cafe|cafetera/, "☕"],
  [/cerveza/, "🍺"],
  [/vino/, "🍷"],
  [/ron |whisky/, "🥃"],
  [/kombucha|te verde/, "🍵"],
  [/jugo mango/, "🥭"],
  [/jugo/, "🧃"],
  [/bebida|gaseosa/, "🥤"],
  [/aceite/, "🫒"],
  [/snacks\|arroz/, "🍘"],
  [/arroz/, "🍚"],
  [/atun/, "🐟"],
  [/avena|granola|cereal/, "🥣"],
  [/chocolate|barra/, "🍫"],
  [/frijoles/, "🫘"],
  [/harina/, "🌾"],
  [/jamonada/, "🥓"],
  [/leche|yogurt/, "🥛"],
  [/mantequilla mani|mani/, "🥜"],
  [/manteca|mantequilla/, "🧈"],
  [/mayonesa|salsa|vinagre|recipientes|deposito/, "🫙"],
  [/mermelada/, "🍓"],
  [/miel/, "🍯"],
  [/oregano/, "🌿"],
  [/sal fina|pimienta|azucar/, "🧂"],
  [/pasta/, "🍝"],
  [/queso/, "🧀"],
  [/chips|papitas/, "🥔"],
  [/galletas/, "🍪"],
  [/gelatina/, "🍮"],
  [/gomitas/, "🍬"],
  [/nueces/, "🌰"],
  [/palomitas|popcorn/, "🍿"],
  // Salud y cuidado personal
  [/cuidado personal\|.*cepillo/, "🪥"],
  [/hilo dental/, "🦷"],
  [/jabon|gel ducha/, "🧼"],
  [/maquinilla|depiladora/, "🪒"],
  [/esmalte/, "💅"],
  [/espejo/, "🪞"],
  [/plancha cabello|rizador|secador/, "💇"],
  [/cuidado personal\|/, "🧴"],
  [/bcaa|colageno|magnesio|multivitamina|omega|proteina|vitamina/, "💊"],
  [/pesas|banda resistencia|cuerda|foam roller/, "🏋️"],
  [/bascula/, "⚖️"],
  [/colchoneta yoga/, "🧘"],
  [/masajeador/, "💆"],
  [/glucometro|tensiometro|pulsioximetro|nebulizador/, "🩺"],
  // Ferretería
  [/martillo/, "🔨"],
  [/atornillador|destornillador/, "🪛"],
  [/escalera/, "🪜"],
  [/cinta metrica|nivel laser|plomada/, "📏"],
  [/sierra|caladora|amoladora|multiherramienta/, "🪚"],
  [/alicate|llaves|ratchet|llave paso|codo|tubo|varilla/, "🔧"],
  [/taladro|broca|cincel|anclaje|tornillo/, "🔩"],
  [/cemento|ceramica/, "🧱"],
  [/tablaroca/, "🪵"],
  [/pintura|impermeabilizante/, "🪣"],
  [/pegamento|sellador|silicona/, "🧴"],
  [/cinta aislar/, "🔌"],
  [/gabinete|organizador closet/, "🗄️"],
  [/herramientas\||ferreteria/, "🛠️"],
  // Hogar
  [/tijera|jardin\|juego herramientas/, "✂️"],
  [/asador|bbq/, "🍖"],
  [/aspersor|manguera|regadera/, "🚿"],
  [/comedero aves/, "🐦"],
  [/maceta|planta/, "🪴"],
  [/cortacesped|fertilizante|tierra/, "🌱"],
  [/hamaca/, "🌴"],
  [/cuchillos|tabla corte/, "🔪"],
  [/hervidor/, "🫖"],
  [/freidora|sartenada|horno|microondas|sandwichera/, "🍳"],
  [/licuadora|batidora/, "🥤"],
  [/cama|colchon|almohada|edredon|sabanas/, "🛏️"],
  [/sofa|cojin/, "🛋️"],
  [/silla|mesa|escritorio/, "🪑"],
  [/estanteria|librero/, "📚"],
  [/marco fotos|cuadro/, "🖼️"],
  [/cortina/, "🪟"],
  [/vela|difusor/, "🕯️"],
  [/alfombra/, "🧶"],
];

const CATEGORY_FALLBACK: [RegExp, string][] = [
  [/electr/, "💻"],
  [/ropa|accesor/, "👕"],
  [/aliment|bebida/, "🥫"],
  [/ferret|industr/, "🧰"],
  [/hogar|decor/, "🛋️"],
  [/salud|cuidado/, "💊"],
  [/servicio|digital/, "☁️"],
];

const fold = (s: string) =>
  s.toLowerCase().normalize("NFD").replace(/[̀-ͯ]/g, "");

/** Emoji que representa al producto. */
export function productEmoji(p: { name: string; category: string; subcategory?: string | null }): string {
  const key = `${fold(p.subcategory ?? "")}|${fold(p.name)} `;
  for (const [re, e] of RULES) if (re.test(key)) return e;
  const cat = fold(p.category);
  for (const [re, e] of CATEGORY_FALLBACK) if (re.test(cat)) return e;
  return "📦";
}

/** Nombre del archivo en /public/productos: código Unicode del emoji, sin el selector de variación. */
export function emojiFile(e: string): string {
  return [...e]
    .map((c) => c.codePointAt(0)!.toString(16))
    .filter((h) => h !== "fe0f")
    .join("-");
}

export type ImageInput = { name: string; category: string; subcategory?: string | null; image_url?: string | null };

/** URL de la imagen del producto (la del ERP/Excel si existe; si no, la ilustración por tipo). */
export function productImage(p: ImageInput): string {
  const url = p.image_url?.trim();
  if (url) return url;
  return `/productos/${emojiFile(productEmoji(p))}.webp`;
}

/** Ilustración de una categoría completa (portada). */
export function categoryImage(category: string): string {
  const cat = fold(category);
  const e = CATEGORY_FALLBACK.find(([re]) => re.test(cat))?.[1] ?? "📦";
  return `/productos/${emojiFile(e)}.webp`;
}

/** Todos los emojis posibles (para copiar sus imágenes). */
export const ALL_EMOJIS = [...new Set([...RULES, ...CATEGORY_FALLBACK].map(([, e]) => e).concat("📦"))];
