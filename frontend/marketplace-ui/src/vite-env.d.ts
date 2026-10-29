/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_URL?: string;
  readonly VITE_CHECKOUT_HABILITADO?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
