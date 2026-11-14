-- F268: consulta de inventario de STAGING y su restauracion QA.
-- El archivo conserva las comillas de identificadores que PowerShell 5.1 elimina de psql -c.
-- SOLO SELECT; nunca modifica datos ni objetos.
SELECT (SELECT COUNT(*) FROM pg_tables WHERE schemaname IN ('core','catalog','finance'))::text
       || '|' || (SELECT COUNT(*) FROM core."__EFMigrationsHistory")::text
       || '|' || (SELECT COUNT(*) FROM catalog."__EFMigrationsHistory")::text
       || '|' || (SELECT COUNT(*) FROM finance."__EFMigrationsHistory")::text;
