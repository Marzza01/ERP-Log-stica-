-- ============================================================================
-- AUDITORÍA DE SPLITS HUÉRFANOS (SOLO LECTURA)
-- ERP LOGIMKT S.A. de C.V. — Módulo Finance / Liquidaciones
-- ============================================================================
-- PROPÓSITO:
-- Identificar splits de pago en estado 'Liberado' que tienen LiquidacionId nulo
-- pertenecientes a vendedores que ya cuentan con liquidaciones 'Completadas'.
--
-- RIESGO ASOCIADO:
-- Al ejecutar GenerarLiquidacionAsync bajo el modelo 1 a N, el sistema consolidará
-- todos los splits con LiquidacionId IS NULL. Si existen splits históricos que nunca
-- fueron enlazados a una liquidación (debido a la limitación previa donde una liquidación
-- solo vinculaba 1 split y no N), el sistema generará un nuevo desembolso por montos
-- pasados. Esta consulta permite auditar y decidir si dichos splits deben marcarse
-- manualmente o incluirse en una liquidación retroactiva.
-- ============================================================================

SELECT 
    v.vendedor_id,
    v.total_liquidaciones_completadas,
    v.ultima_liquidacion,
    s."Id" AS split_id,
    s."OrdenId" AS orden_id,
    o."NumeroOrden" AS numero_orden,
    o."Estado" AS estado_orden,
    s."MontoTotal" AS monto_total_orden,
    s."MontoVendedor" AS monto_vendedor,
    s."MontoMarketplace" AS comision_marketplace,
    s."FechaProcesado" AS fecha_procesado_split
FROM finance.splits_pago s
INNER JOIN finance.ordenes o ON o."Id" = s."OrdenId"
INNER JOIN (
    -- Vendedores que ya tienen al menos una liquidación completada
    SELECT 
        l."VendedorId" AS vendedor_id,
        COUNT(l."Id") AS total_liquidaciones_completadas,
        MAX(l."FechaLiquidacion") AS ultima_liquidacion
    FROM finance.liquidaciones l
    WHERE l."Estado" = 'Completada'
    GROUP BY l."VendedorId"
) v ON v.vendedor_id = o."VendedorId"
WHERE s."LiquidacionId" IS NULL
  AND o."Estado" = 'Entregada'
ORDER BY v.vendedor_id, s."FechaProcesado" ASC;
