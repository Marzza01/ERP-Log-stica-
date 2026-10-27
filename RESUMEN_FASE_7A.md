# Resumen — Fase 7A

La primera limpieza de Fase 7 elimina los fallbacks funcionales del Grupo A sin tocar reglas de inventario ni base de datos.

Resultado objetivo:
- UI logística trabaja únicamente contra la API real.
- Las acciones fallidas ya no mutan el estado local como si hubieran tenido éxito.
- El acceso al frontend ya no se autoautentica.
- Los fallos quedan visibles y reintentables.
- Se preservan las fronteras con Finanzas, Contabilidad, Liquidaciones y OMS.
- `CodigoEAN` no se modifica.

Siguiente bloque propuesto: Fase 7B — limpieza de UI compartida (Dashboard/Topbar), estados vacíos y revisión de bundle Vite, sin invadir módulos de otros equipos.
