/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';

export interface BloqueoInventarioResult {
  exitoso: boolean;
  yaProcesada: boolean;
  productoId: string;
  stockFisicoId: string;
  cantidadProcesada: number;
  cantidadBloqueadaDespues: number;
  cantidadDisponibleDespues: number;
  mensaje: string;
}

export interface CambiarBloqueoInput {
  stockFisicoId: string;
  cantidad: number;
  operacionId: string;
  motivo: string;
}

export const bloqueosApi = {
  bloquear: (input: CambiarBloqueoInput) =>
    apiClient.post<BloqueoInventarioResult>('/api/inventario/bloqueos/bloquear', input),

  desbloquear: (input: CambiarBloqueoInput) =>
    apiClient.post<BloqueoInventarioResult>('/api/inventario/bloqueos/desbloquear', input),
};
