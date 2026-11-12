# Verificación de cumplimiento — rúbrica UNICAES LOGIMKT (2026)

**Documento fuente:** Proyecto_ERP_Estudiante(1).pdf, 15 páginas (páginas 1–15, semanas 1–10). **Código examinado:** `consolidacion/erp-segura-20261108` en revisión del 8 nov 2026. **Base consultada:** Neon `marketplaceerp_test`, branch desechable `audit-f2-f3-disposable-20261108` (solo lectura). **CI:** último SHA verificado con 3/3 trabajos y 487 pruebas .NET.

**Interpretación:** “No acreditado” ≠ inexistente; solo significa que no hay prueba suficiente en los materiales accesibles. “No verificado” indica artefacto documental externo no disponible. Nunca declarar porcentaje global de cumplimiento sin evidencias de todos los requisitos.

## Matriz por semanas

| Semana | Requisito verificable del PDF | Estado | Evidencia o brecha | Prueba / acción necesaria |
|---:|---|---|---|---|
| 1 | Catálogo de 500 SKU con precios/categorías | Acreditado en base aislada | Neon: 500 productos, 6 categorías | Mantener pruebas de carga/importación |
| 1 | Validación de stock/precios tras importación | Parcial | 500 con stock mínimo, pero StockInicial importado ≠ existencias WMS | Documentar semántica y reconciliar, sin inventar stock |
| 1 | SIPOC, acta de equipo e informe semanal | Sin evidencia documental | No identificados en repo; podrían estar fuera | Adjuntar entregables reales |
| 2 | Mínimo 5 proveedores salvadoreños | No cumple en base aislada | Neon: 4 proveedores | Incorporar quinto proveedor real validado sin datos ficticios productivos |
| 2 | Reorden automático y política stock seguridad | Parcial | StockMinimo en 500/500, reglas/eficacia no certificadas | Probar disparo, recomendación y justificación por categoría |
| 2 | Recepción simulada de 3 productos | Parcial | Neon: inbound 10 unidades de 2 productos distintos documentados | Demostrar recepción de tres productos y control calidad |
| 2 | BPMN de compras y política stock muerto | Sin evidencia | No se localizó archivo de entrega formal | Preparar BPMN, políticas y ejemplo |
| 3 | OMS estados y flujo B2B/B2C | Parcial | Estados Pendiente/Aprobada/EnProceso/Enviada/Entregada/Cancelada; no Cerrado explícito | Documentar equivalencia, validar transiciones en PostgreSQL |
| 3 | Condiciones B2B mínimo $500, crédito y CCF | Parcial | Modelos disponen campos; creación admin no comprueba mínimo | Definir contrato y validar mínimo/crédito/integridad tributaria |
| 3 | 10 pedidos completos: 5 B2B + 5 B2C | No acreditado | Neon aislado: 0 órdenes cliente; CI no cubre 10 flujos completos | Fixtures reproducibles y capturas de las diez operaciones |
| 3 | Notificaciones automáticas en cada cambio | No acreditado | No se identificó integración operativa | Emitir eventos/notificaciones de prueba y auditar entrega |
| 3 | Devoluciones y reembolso | Parcial | RMA implementado y guards P0 en PR #64; ciclo monetario no certificado | E2E aislado venta→RMA→reembolso/stock |
| 3 | OTIF y Lead Time de 10 pedidos | No acreditado | Sin pedidos de evaluación ni fechas prometidas completas | Fechas registradas, fórmula correcta, resultados trazables |
| 4 | 500 productos publicados | No cumple en base aislada | Neon: 488 activos y publicados, 12 inactivos | Acordar criterio y publicar los 12 solo si procede |
| 4 | Buscador con filtro categoría/precio/disponibilidad | Parcial | Endpoint paginado y catálogo B2C; UI filtros completos no certificados | Prueba visual e integración de cada filtro |
| 4 | Vista B2B con precio diferencial | Parcial | PrecioB2B existe; flujo comercial B2B completo no certificado | Prueba cliente B2B con precios/roles |
| 4 | Carrito multiproducto | No cumple en flujo vigente | CheckoutCompradorPage: un producto por orden | Recuperar UX y contrato seguro desde diseño histórico |
| 4 | Checkout con 3 medios de pago | No acreditado | PaymentOperationsGuard y proveedor Disabled en staging | Diseñar tres medios simulados en Development aislado |
| 4 | 5 compras de prueba | No acreditado | Base evaluada sin órdenes | Cinco capturas y referencias persistidas de prueba |
| 4 | Reseñas/calificaciones, categorías, banners | Parcial | Categorías sí; reseñas/banners no demostrados | Prueba UI y persistencia de reseñas |
| 5 | Flujo integral módulo C→B→A | No acreditado E2E | Tests parciales; Finance/Catalog transacciones no siempre atómicas | Recorrer compra→reserva→picking→despacho→entrega |
| 5 | 50 pedidos simultáneos | No acreditado | Concurrencia parcial de compras; no 50 pedidos B2B/B2C | Ejecutar estrés acotado y validar no oversell |
| 5 | Matriz de ≥10 bugs, cinco mejoras | Parcial | Auditoría documenta múltiples defectos, sin matriz académica final completa | Crear matriz con severidad, reproducciones y solución |
| 6 | Dashboard 8 KPI obligatorios y semáforo | No cumple completo | KpisDashboard incluye 8 diferentes, semáforo existe | Implementar los 8 de PDF manteniendo adicionales |
| 6 | OTIF y Lead Time históricos en gráficos | No acreditado | OTIF usa puntualidad y fechas condicionadas; tendencia requerida no demostrada | Integrar eventos de entrega con series temporales |
| 6 | Rotación de inventario | Incorrecto | Valor monetario de órdenes entregadas dividido entre unidades físicas | Usar COGS / inventario promedio al costo, o unidad/unidad coherente |
| 6 | ABC de 500 productos A80/B15/C5 | No acreditado | No se identificó cálculo funcional en rama | Implementar clasificación con ventas reales/simuladas declaradas |
| 6 | Reportes diario, semanal, mensual | Parcial | Dashboard/exportación, sin todas las series requeridas demostradas | Pruebas con fechas y exportación |
| 6 | IA generativa en descripciones | No acreditado | Sin implementación/evidencia operativa identificada | Implementar o documentar integración verificable con salvaguardas |
| 7 | Picking→packing→despacho | Parcial | Servicios presentes; guards P0/P1 PR #64; E2E real no certificado | Prueba PostgreSQL incluyendo reintentos |
| 7 | Tracking de última milla y notificación | Parcial | Estado interno/guía existe, sin eventos externos probados | Simulación de eventos rastreables y pantalla comprador |
| 7 | Costo logístico por zona y mapa | No acreditado | No se encontró KPI/cálculo completo por zona | Definir zonas El Salvador y costos trazables |
| 7 | Rutas optimizadas y cross-docking | No acreditado | S-Shape es ordenación básica; cross-docking sin evidencia | Algoritmo/diagrama y casos medibles |
| 7 | Logística inversa con KPI | Parcial | RMA en Catalog; falta E2E completo y tasa metodológicamente válida | Prueba postventa y KPI |
| 8 | Forecast demanda top 20 | No acreditado | Modelo financiero 12 meses no es forecast por SKU | Modelo simple por 20 productos y validación |
| 8 | Rotación por categoría/subcategoría | No acreditado correctamente | Métrica general tiene unidades incoherentes | Calcular por categoría y período |
| 8 | IA para tendencias/recomendaciones de compra | No acreditado | Sin evidencia operativa verificable | Ejemplos con datos trazables y supervisión humana |
| 8 | Informe logístico mensual y modelo financiero | Parcial | API proyección 12 meses fija; no Excel económico de la rúbrica | Crear Excel con fórmulas, IVA/costos/margen/costo pedido |
| 9 | Informe técnico ≥50 páginas contenido | No verificado | No hay DOCX/PDF de entrega en árbol Git ni Library encontrada en búsqueda dirigida | Verificar documento externo real |
| 9 | Word editable + PDF; Arial 12/14–16, 2.5 cm, 1.5 | No verificado | No hay documento final disponible para comprobar formato | Inspección automática del DOCX/PDF real |
| 9 | APA 7 y ≥8 fuentes verificables | No verificado | Sin informe final accesible | Verificar bibliografía, citas y fuentes |
| 9 | Capturas HD con etiquetas | No verificado | Sin paquete final de capturas accesible | Generar evidencia desde versión exacta ensayada |
| 9 | Presentación ≤20 diapositivas; manual; diagramas | No verificado | No aparecen PPTX/manual académico final en repositorio | Adjuntar archivos, verificar extensión y consistencia |
| 10 | Auditoría viva, demostración, defensa individual | Pendiente de evidencia presencial | Pruebas CI no equivalen a defensa ante docente | Ensayar cambios de parámetros, KPIs y respuestas |

## 8 KPI académicos que deben existir a la vez

1. OTIF (On Time In Full): confirmar **plazo y cantidad completa**; registrar fecha comprometida y entrega real.
2. Lead Time del pedido: fecha recepción/creación a entrega con definición explícita.
3. Fill Rate: pedidos/líneas satisfechos plenamente según denominador acordado.
4. Tasa de devoluciones: considerar órdenes, unidades o líneas según definición documentada, evitando sobreconteo.
5. Costo logístico por pedido: costo por zona y período con costos reales o supuestos transparentes.
6. Rotación de inventario: magnitudes dimensionalmente compatibles, idealmente costo de ventas / inventario promedio a costo.
7. Tiempo de procesamiento OMS: tiempo real entre transiciones, no simplemente minutos desde la creación.
8. NPS / CSAT: encuestas respondidas y fórmula documentada; sin encuestas debe decir “Sin datos”.

**Atención:** PDF páginas 5–6 enumera KPI, pero sus columnas de fórmulas exactas, metas y rangos de semáforo están sin rellenar. Definirlas con una metodología explícita, sin atribuir al PDF valores que no trae. Mantener los KPI adicionales existentes: cancelación, 3-Way Match y margen comisión.

## Criterios de evaluación y documentos

- PDF página 11: informe **≥50 páginas de contenido real** excluyendo portada, índice y bibliografía; formato DOCX editable + PDF, cuerpo Arial 12, títulos Arial 14–16, márgenes 2.5 cm, interlineado 1.5, APA 7, **≥8 fuentes**.
- Página 12: fórmula, resultado y tendencia visual para **cada KPI**, capturas HD etiquetadas.
- Página 10: presentación **máximo 20 diapositivas**, manual, demostración ensayada.
- Página 15: nota final **80 % proyecto / 20 % defensa individual**.
- **Inconsistencia interna del propio PDF:** título de sección 8A (p.13) dice “Proyecto — 60 %”, mientras tabla (p.14) suma **80 %** y cuadro final (p.15) confirma **80/20**. Aclarar con docente; no inventar otra ponderación.
- Los ejemplos empresariales de **150 vendedores**, **800/2400 pedidos por día** (p.2) son **contexto del caso**; no reemplazan los diez pedidos demostrativos ni obligan a poblar automáticamente la base con tales volúmenes.
- No se detectaron archivos `.xlsx`, `.docx`, `.pptx`, ni `.pdf` de entrega en el árbol Git de la rama. La búsqueda dirigida en la Library solo mostró la rúbrica, pero esto no demuestra inexistencia de otros entregables fuera de ella.

## Acciones que bloquean declarar excelente

### Riesgos transaccionales — antes de una demo mutable
- Integridad OMS/WMS/RMA/Finance ante reintentos, carreras y falla parcial; no usar datos productivos ni pagos reales.
- Ensayo E2E B2B/B2C en PostgreSQL aislado con el flujo completo y las 10 órdenes de prueba.
- Corregir fórmulas del dashboard e integrar datos de fechas/promesas/encuestas/costos.

### Funcionalidad y rúbrica
- Cinco proveedores válidos; demostrar publicación de 500 productos según condiciones de negocio; recepción de tres artículos.
- Carrito multiartículo, tres métodos simulados autorizados solo en entorno aislado y evidencias de cinco compras.
- Prueba de 50 órdenes concurrentes, ABC 500, demanda top 20, tracking/zonas/cross-docking y reportes.
- SIPOC, BPMN, manual, informe y anexos académicos verificables.

### Liberación
- PR #64 es **borrador y no debe fusionarse** antes de las pruebas restantes. `main` y producción sin modificaciones.
- No activar 12 productos inactivos ni crear stock/ventas falsos en producción para “cumplir” la rúbrica. Utilizar fixtures trazables en entorno de demostración aislado.
