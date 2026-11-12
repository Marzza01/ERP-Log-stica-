-- MARKETPLACE ERP — Conciliación OMS/WMS, SOLO LECTURA (2026-11-08)
-- Ejecutar con permisos de lectura en una BD de QA previamente autorizada.
-- NO se debe automatizar ningún cambio a partir de esta consulta.
-- Estado catalog."PaquetesDespacho": 2 = ListoParaDespacho, 3 = Despachado.
-- Estado catalog."PickLists": 0 = Pendiente, 1 = EnProceso, 2 = Completado, 3 = Cancelado.
-- Estados finance.ordenes se almacenan como texto.
-- Usa solamente SELECT; no requiere tokens, migraciones ni credenciales embebidas.

WITH hallazgos AS (
    SELECT
        'PICKLIST_DUPLICADA'::text AS codigo,
        p."OrdenId" AS orden_id,
        MIN(p."NumeroOrden")::text AS numero_orden,
        ('PickLists existentes: ' || count(*))::text AS detalle
    FROM catalog."PickLists" AS p
    WHERE p."Estado" <> 3
    GROUP BY p."OrdenId"
    HAVING count(*) > 1

    UNION ALL

    SELECT
        'PICKLIST_OMS_SIN_AVANCE',
        p."OrdenId",
        o."NumeroOrden",
        ('OMS=' || o."Estado" || '; PickList=' || p."NumeroPickList")
    FROM catalog."PickLists" AS p
    JOIN finance.ordenes AS o ON o."Id" = p."OrdenId"
    WHERE o."Estado" = 'Aprobada'
      AND p."Estado" <> 3

    UNION ALL

    SELECT
        'PAQUETES_MULTIPLES',
        p."OrdenId",
        MIN(p."NumeroOrden"),
        ('Paquetes creados: ' || count(*))
    FROM catalog."PaquetesDespacho" AS p
    GROUP BY p."OrdenId"
    HAVING count(*) > 1

    UNION ALL

    SELECT
        'GUIA_OMS_DIFIERE_WMS',
        p."OrdenId",
        o."NumeroOrden",
        ('OMS=' || COALESCE(NULLIF(o."GuiaEnvio", ''), '(vacía)') ||
         '; WMS=' || p."NumeroGuia")
    FROM catalog."PaquetesDespacho" AS p
    JOIN finance.ordenes AS o ON o."Id" = p."OrdenId"
    WHERE COALESCE(o."GuiaEnvio", '') IS DISTINCT FROM p."NumeroGuia"

    UNION ALL

    SELECT
        'DESPACHADO_WMS_OMS_NO_ENVIADO',
        p."OrdenId",
        o."NumeroOrden",
        ('OMS=' || o."Estado" || '; WMS=Despachado')
    FROM catalog."PaquetesDespacho" AS p
    JOIN finance.ordenes AS o ON o."Id" = p."OrdenId"
    WHERE p."Estado" = 3
      AND o."Estado" NOT IN ('Enviada', 'Entregada')

    UNION ALL

    SELECT
        'PAQUETE_HUERFANO_SIN_ORDEN_OMS',
        p."OrdenId",
        p."NumeroOrden",
        ('Guía WMS=' || p."NumeroGuia")
    FROM catalog."PaquetesDespacho" AS p
    LEFT JOIN finance.ordenes AS o ON o."Id" = p."OrdenId"
    WHERE o."Id" IS NULL
)
SELECT codigo, orden_id, numero_orden, detalle
FROM hallazgos
ORDER BY codigo, numero_orden, orden_id;

-- La presencia de una fila exige revisión manual: pueden existir operaciones
-- en curso y estados legados. La ausencia NO equivale a conciliación financiera
-- ni a garantía de idempotencia en servicios externos. No ejecutar UPDATE/DELETE.
