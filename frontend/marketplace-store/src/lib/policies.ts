// Políticas del marketplace. Los números salen de shared.ts y orders.ts para que el texto
// siempre coincida con lo que hace el sistema (mínimos, envío, plazos, estados, métodos de pago).
import {
  B2B_MIN_ORDER, B2C_FREE_SHIPPING_FROM, B2C_MIN_ORDER, B2C_SHIPPING, CARRIERS, ORDER_FLOW, PAYMENT_METHODS, PROMISE_DAYS,
  RETURN_REASONS, money,
} from "./shared";

export type PolicySection = { title: string; paragraphs?: string[]; items?: string[]; table?: string[][] };
export type Policy = { slug: string; title: string; icon: string; summary: string; sections: PolicySection[] };

export const POLICIES_UPDATED = "16 de noviembre de 2026";

const pay = (c: "B2C" | "B2B") => PAYMENT_METHODS[c].map((m) => m.label);

export const POLICIES: Policy[] = [
  {
    slug: "terminos",
    title: "Términos y condiciones",
    icon: "📜",
    summary: "Quiénes somos, cómo funciona el marketplace y las reglas generales de uso y compra.",
    sections: [
      {
        title: "1. Quiénes somos",
        paragraphs: [
          "LOGIMKT S.A. de C.V. opera un marketplace salvadoreño B2C (personas) y B2B (empresas y distribuidores) con productos de electrónica y tecnología, ropa y accesorios, alimentos y bebidas, ferretería e industria, hogar y decoración, y salud y cuidado personal.",
          "La tienda en línea (este sitio) está conectada al ERP/OMS de LOGIMKT, que es el sistema oficial de catálogo, inventario, pedidos, pagos y facturación. Cuando hay diferencias entre lo que muestra la tienda y lo que registra el ERP, prevalece el ERP.",
        ],
      },
      {
        title: "2. Tipos de cliente",
        paragraphs: [
          "En la parte superior del sitio eliges para quién compras. Esa elección define precios, mínimos, métodos de pago y condiciones de envío:",
        ],
        table: [
          ["", "Mi casa (B2C)", "Mi empresa (B2B)"],
          ["Precio", "Precio al consumidor", "Precio mayorista"],
          ["Compra mínima", money(B2C_MIN_ORDER), money(B2B_MIN_ORDER)],
          ["Datos requeridos", "Nombre, correo, dirección", "Además: razón social y NIT o NRC"],
          ["Envío", `${money(B2C_SHIPPING)}; gratis desde ${money(B2C_FREE_SHIPPING_FROM)}`, "Incluido"],
          ["Entrega estimada", `Hasta ${PROMISE_DAYS.B2C} días`, `Hasta ${PROMISE_DAYS.B2B} días`],
          ["Formas de pago", pay("B2C").join(", "), pay("B2B").join(", ")],
        ],
      },
      {
        title: "3. Formación del contrato",
        items: [
          "Al pulsar «Confirmar pedido» haces una oferta de compra. El pedido queda confirmado únicamente cuando el ERP valida el catálogo y reserva el inventario. El código de seguimiento es aleatorio y no sustituye la confirmación del ERP.",
          "En el canal conectado al ERP, si no hay inventario suficiente para todas las unidades solicitadas, el pedido no se confirma y se solicita corregir el carrito. No se realizan cargos reales.",
          "LOGIMKT puede rechazar o cancelar pedidos con datos falsos o incompletos, precios publicados por error evidente o sospecha de fraude. Cualquier importe recibido se revisa y gestiona manualmente.",
        ],
      },
      {
        title: "4. Precios y catálogo",
        items: [
          "Los precios están en dólares de los Estados Unidos (USD), la moneda de curso legal en El Salvador.",
          "El precio que se cobra es el vigente al momento de confirmar el pedido; cambios posteriores no afectan pedidos ya confirmados.",
          "Catálogo, precios y disponibilidad se sincronizan desde el ERP. Los productos marcados como inactivos no se muestran ni se pueden comprar.",
          "Las imágenes son ilustrativas cuando el vendedor no ha cargado una fotografía del producto.",
        ],
      },
      {
        title: "5. Reseñas y encuesta de satisfacción",
        items: [
          "Puedes calificar productos de 1 a 5 estrellas y responder la encuesta NPS en la página de tu pedido. Esas respuestas alimentan los indicadores de calidad (NPS y CSAT).",
          "No se permiten reseñas ofensivas, con datos personales de terceros, publicidad o que no correspondan al producto. LOGIMKT puede retirarlas.",
        ],
      },
      {
        title: "6. Responsabilidad y ley aplicable",
        items: [
          "LOGIMKT responde por la correcta gestión del pedido, el envío y la atención de devoluciones conforme a estas políticas y a la Ley de Protección al Consumidor de El Salvador.",
          "Para clientes B2B aplican además las condiciones comerciales pactadas (crédito, lotes mínimos y facturación con crédito fiscal).",
          "Cualquier controversia se rige por las leyes de la República de El Salvador. Los consumidores conservan su derecho de acudir a la Defensoría del Consumidor.",
        ],
      },
    ],
  },
  {
    slug: "pedidos-y-stock",
    title: "Pedidos, stock y seguimiento",
    icon: "📦",
    summary: "Cómo verificamos y reservamos el inventario, los estados de tu pedido y cómo rastrearlo.",
    sections: [
      {
        title: "1. Disponibilidad en tiempo real",
        items: [
          "El stock que ves en el catálogo proviene del inventario del ERP. Agregar un producto al carrito no lo aparta: el inventario se verifica y se reserva únicamente al confirmar el pedido.",
          "Si dos clientes compran el último artículo al mismo tiempo, el sistema atiende primero al pedido que se confirma primero; nunca se vende el mismo inventario dos veces.",
          "En la integración con el ERP no se confirman pedidos con surtido parcial: si falta stock de cualquier línea, la reserva se rechaza y el cliente debe actualizar el carrito. La tienda no marca el pedido como confirmado hasta recibir la aceptación del ERP.",
          `Para empresas, el mínimo de ${money(B2B_MIN_ORDER)} se calcula sobre el subtotal completo que el ERP valida y confirma.`,
        ],
      },
      {
        title: "2. Estados del pedido",
        paragraphs: ["Cada pedido avanza por estos estados, y cada cambio queda registrado con fecha y hora en la página del pedido:"],
        table: [
          ["Estado", "Qué significa"],
          ["Pendiente ERP", "Recibimos la solicitud; el ERP está validando catálogo y disponibilidad. Todavía no está confirmada."],
          [ORDER_FLOW[0], "El pedido fue recibido y quedó registrado en la tienda."],
          [ORDER_FLOW[1], "Stock verificado y reservado en el ERP; se genera la orden de preparación."],
          [ORDER_FLOW[2], "El almacén hace picking y packing de tus productos."],
          [ORDER_FLOW[3], "Se asignó transportista y número de guía."],
          [ORDER_FLOW[4], "El pedido llegó a tu dirección. Se cobra el pago contra entrega o se confirma la transferencia."],
          [ORDER_FLOW[5], "Pedido finalizado."],
          ["Cancelado", "El pedido se anuló antes del envío y el inventario se liberó."],
          ["Devuelto", "La solicitud RMA quedó registrada en el ERP; el reembolso y el reingreso de inventario requieren inspección y autorización."],
        ],
      },
      {
        title: "3. Seguimiento y notificaciones",
        items: [
          "Con tu código LMK puedes ver en «Rastrear un pedido» el estado, la línea de tiempo, el transportista, la guía y la fecha prometida de entrega.",
          "Cada cambio de estado genera una notificación al cliente.",
          "Cuando el pedido está registrado en el ERP, la página también muestra el número de orden oficial y su estado en el OMS.",
        ],
      },
      {
        title: "4. Continuidad si el ERP no responde",
        paragraphs: [
          "Si falla la conexión, el pedido queda en estado Pendiente ERP, sin mostrarse como confirmado. El equipo puede reintentar desde el panel usando el mismo código externo; el ERP aplica idempotencia para evitar órdenes duplicadas."
        ],
      },
    ],
  },
  {
    slug: "pagos",
    title: "Pagos y facturación",
    icon: "💳",
    summary: "Métodos de pago para personas y empresas, cuándo se considera pagado un pedido y crédito B2B.",
    sections: [
      {
        title: "1. Métodos aceptados",
        table: [
          ["Método", "Clientes", "Estado del pago al confirmar"],
          ["Transferencia bancaria", "B2C y B2B", "Pendiente de verificación manual"],
          ["Pago contra entrega (efectivo)", "Solo B2C", "Pendiente de cobro; se confirma tras la entrega"],
          ["Crédito a 30 días", "Solo B2B", "Pendiente de autorización administrativa"],
        ],
      },
      {
        title: "2. Reglas",
        items: [
          "La transferencia debe verificarse manualmente en el ERP antes de preparar el pedido; si no se verifica en 24 horas, puede cancelarse y liberarse la reserva.",
          "El pago contra entrega se cobra al entregar. Un administrador confirma el cobro en el ERP; no se registra como pagado automáticamente.",
          "El crédito a 30 días está disponible para empresas con NIT/NRC y queda sujeto a autorización administrativa en el ERP.",
          "Las pasarelas y los cobros con tarjeta permanecen deshabilitados. La tienda no recibe ni almacena datos de tarjetas y no realiza cargos reales.",
        ],
      },
      {
        title: "3. Facturación",
        items: [
          "El documento tributario se emite desde el ERP: Factura de Consumidor Final para personas y Comprobante de Crédito Fiscal para empresas con NRC.",
          "El IVA (13 %) se desglosa en el documento tributario según corresponda.",
          "Verifica que la razón social y el NIT/NRC que ingresas en el checkout sean correctos; las correcciones requieren anular y reemitir el documento.",
        ],
      },
      {
        title: "Nota del proyecto académico",
        paragraphs: ["En esta versión no se realizan cargos reales ni se procesan tarjetas. Solo se aceptan solicitudes de transferencia, pago contra entrega y crédito B2B, sujetos a validación manual en el ERP."],
      },
    ],
  },
  {
    slug: "envios",
    title: "Envíos y entregas",
    icon: "🚚",
    summary: "Cobertura, costos, plazos de entrega y transportistas.",
    sections: [
      {
        title: "1. Cobertura",
        paragraphs: [
          "Entregamos en todo El Salvador, con rutas optimizadas para San Salvador y su zona metropolitana, Santa Ana y las principales cabeceras departamentales. La expansión a Guatemala y Honduras está proyectada para 2027.",
        ],
      },
      {
        title: "2. Costos y plazos",
        table: [
          ["", "Personas (B2C)", "Empresas (B2B)"],
          ["Costo de envío", `${money(B2C_SHIPPING)} por pedido`, "Incluido en el precio"],
          ["Envío gratis", `Pedidos desde ${money(B2C_FREE_SHIPPING_FROM)}`, "Siempre"],
          ["Entrega prometida", `Hasta ${PROMISE_DAYS.B2C} días desde la confirmación`, `Hasta ${PROMISE_DAYS.B2B} días desde la confirmación`],
        ],
      },
      {
        title: "3. Proceso de entrega",
        items: [
          "Fulfillment: picking → packing → despacho. El pedido pasa a «Enviado» cuando se entrega al transportista.",
          `Transportistas aliados: ${CARRIERS.join(", ")}. El transportista y la guía aparecen en el seguimiento del pedido.`,
          "La entrega se hace en la dirección indicada en el checkout. Si no hay nadie para recibir, el transportista intentará una segunda visita y se comunicará al teléfono registrado.",
          "Revisa el paquete al recibirlo. Si llega dañado o incompleto, anótalo con el transportista y repórtalo dentro de las 48 horas siguientes.",
        ],
      },
      {
        title: "4. Compromiso de puntualidad (OTIF)",
        paragraphs: [
          "Medimos cada entrega: un pedido cuenta como cumplido si llega a tiempo (antes de la fecha prometida) y completo. Nuestra meta es un OTIF de 95 % o más, publicada en el panel de indicadores.",
          "Durante temporada alta (noviembre a enero) los plazos pueden extenderse; si tu fecha prometida cambia, te lo notificaremos.",
        ],
      },
    ],
  },
  {
    slug: "cancelaciones",
    title: "Cancelaciones",
    icon: "✋",
    summary: "Hasta cuándo puedes cancelar un pedido y qué pasa con tu dinero y el inventario.",
    sections: [
      {
        title: "1. Cuándo se puede cancelar",
        items: [
          "Puedes cancelar sin costo mientras el pedido esté en «Recibido», «Confirmado» o «En preparación».",
          "Una vez «Enviado», ya no se puede cancelar: espera la entrega y solicita una devolución.",
          "Para cancelar, comunícate con atención al cliente indicando tu código de pedido LMK.",
        ],
      },
      {
        title: "2. Qué sucede al cancelar",
        items: [
          "La cancelación se registra en el ERP antes de liberar la copia local del inventario. El ERP libera su reserva si la orden ya existía.",
          "Como no se realizan cobros reales, la tienda no emite reembolsos automáticos. Cualquier devolución de fondos de una transferencia verificada requiere gestión administrativa fuera de la pasarela.",
          "LOGIMKT puede cancelar un pedido por falta de verificación de pago, datos de entrega inválidos o imposibilidad de surtir; se informa al cliente y se revisa cualquier importe recibido manualmente.",
        ],
      },
    ],
  },
  {
    slug: "devoluciones",
    title: "Devoluciones y reembolsos",
    icon: "↩️",
    summary: "Plazos, causas aceptadas, proceso de logística inversa y formas de reembolso.",
    sections: [
      {
        title: "1. Plazos",
        table: [
          ["Causa", "Plazo para solicitarla", "Costo del retorno"],
          [RETURN_REASONS[0], "30 días desde la entrega", "Lo cubre LOGIMKT"],
          [RETURN_REASONS[1], "30 días desde la entrega", "Lo cubre LOGIMKT"],
          [RETURN_REASONS[2], "48 horas desde la entrega", "Lo cubre LOGIMKT"],
          [RETURN_REASONS[3], "8 días desde la entrega (solo B2C)", "Lo cubre el cliente"],
        ],
      },
      {
        title: "2. Condiciones",
        items: [
          "Solo se aceptan devoluciones de pedidos en estado «Entregado» o «Cerrado».",
          "Por arrepentimiento, el producto debe estar sin uso, completo y en su empaque original.",
          "No aplica devolución por arrepentimiento en: alimentos y bebidas y productos de salud y cuidado personal abiertos. Sí aplica si llegaron defectuosos, dañados o equivocados.",
          "Las empresas (B2B) pueden devolver por defecto, daño o error de despacho; las devoluciones por arrepentimiento en compras mayoristas se negocian caso por caso.",
        ],
      },
      {
        title: "3. Proceso (logística inversa)",
        items: [
          "Solicitud: el cliente contacta a atención al cliente con su código LMK, la causa y, si aplica, fotos del producto.",
          "Autorización: se registra la devolución con su causa en el OMS y se agenda la recolección con el transportista.",
          "Recolección e inspección: el almacén recibe y revisa el producto.",
          "Reingreso: si está en buen estado vuelve al inventario disponible; si está dañado o defectuoso se separa para reclamo al proveedor o merma.",
          "Reembolso: el ERP registra la resolución después de la inspección y la autorización de Finanzas. La tienda no marca un reembolso como emitido antes de esa confirmación.",
        ],
      },
      {
        title: "4. Reembolsos",
        table: [
          ["Método original", "Cómo se reembolsa", "Plazo"],
          ["Transferencia", "Gestión manual a la cuenta de origen, si corresponde", "Después de la inspección y autorización"],
          ["Contra entrega", "No hay cargo previo; cualquier ajuste se acuerda con el cliente", "Después de la inspección"],
          ["Crédito 30 días (B2B)", "Nota de crédito en el ERP, si Finanzas la autoriza", "Al aprobar la devolución"],
        ],
      },
      {
        title: "5. Medición",
        paragraphs: [
          "Cada devolución se registra con su causa para calcular la tasa de devoluciones (devueltos ÷ entregados). Nuestra meta es mantenerla en 3 % o menos y usamos las causas para corregir problemas con proveedores, empaque o despacho.",
        ],
      },
    ],
  },
  {
    slug: "privacidad",
    title: "Privacidad y datos personales",
    icon: "🔒",
    summary: "Qué datos recogemos, para qué los usamos, con quién se comparten y tus derechos.",
    sections: [
      {
        title: "1. Datos que recogemos",
        items: [
          "Para comprar: nombre, correo, teléfono (opcional), dirección y ciudad de entrega.",
          "Si compras como empresa: razón social y NIT o NRC.",
          "Del pedido: productos, montos, método de pago y estado. La tienda no solicita ni almacena números de tarjeta.",
          "Opiniones: reseñas y respuestas a la encuesta de satisfacción.",
        ],
      },
      {
        title: "2. Para qué los usamos",
        items: [
          "Procesar, entregar y dar seguimiento a tu pedido, y emitir el documento tributario.",
          "Enviarte notificaciones sobre el estado del pedido.",
          "Atender cancelaciones, devoluciones y reclamos.",
          "Calcular indicadores logísticos y de satisfacción de forma agregada (no se publican datos individuales).",
        ],
      },
      {
        title: "3. Con quién se comparten",
        items: [
          "El ERP/OMS de LOGIMKT, que gestiona inventario, pagos y facturación.",
          "El transportista asignado, solo con los datos necesarios para la entrega.",
          "No hay integración activa con pasarela de pagos ni envío de datos de tarjeta.",
          "No vendemos ni cedemos tus datos a terceros con fines publicitarios.",
        ],
      },
      {
        title: "4. Cookies y almacenamiento en tu navegador",
        items: [
          "Cookie «modo»: recuerda si compras para tu casa o tu empresa.",
          "Carrito: se guarda en el almacenamiento local de tu navegador hasta que confirmas el pedido o lo vacías.",
          "No usamos cookies de publicidad ni de rastreo de terceros.",
        ],
      },
      {
        title: "5. Conservación y tus derechos",
        items: [
          "Conservamos los datos de pedidos el tiempo que exigen las obligaciones tributarias y comerciales de El Salvador.",
          "Puedes pedir acceso, corrección o eliminación de tus datos, u oponerte a su uso, contactando a atención al cliente. Atenderemos la solicitud conforme a la legislación salvadoreña de protección de datos personales.",
          "Aplicamos medidas de seguridad como conexiones cifradas (HTTPS) y acceso restringido al panel de operación.",
        ],
      },
    ],
  },
];

export function getPolicy(slug: string): Policy | undefined {
  return POLICIES.find((p) => p.slug === slug);
}
