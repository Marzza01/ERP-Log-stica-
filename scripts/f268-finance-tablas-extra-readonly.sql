-- F268: dos tablas nuevas respecto a la restauración histórica P5 (37 tablas).
-- Huella estable de cada tabla, sin modificar datos ni crear objetos.
SELECT 'finance.cancelaciones_pre_envio' AS tabla,
       COUNT(*)::bigint AS filas,
       md5(COALESCE(string_agg(to_jsonb(t)::text, chr(10)
           ORDER BY (to_jsonb(t)::text) COLLATE "C"), '')) AS huella
FROM finance.cancelaciones_pre_envio t
UNION ALL
SELECT 'finance.intento_reservas_pago' AS tabla,
       COUNT(*)::bigint AS filas,
       md5(COALESCE(string_agg(to_jsonb(t)::text, chr(10)
           ORDER BY (to_jsonb(t)::text) COLLATE "C"), '')) AS huella
FROM finance.intento_reservas_pago t
ORDER BY tabla;
