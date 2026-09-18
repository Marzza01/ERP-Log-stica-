import { apiClient } from './client';
import type { PartidaContable } from '../types';

export interface LibroDiarioResponse {
  filtro: { desde?: string; hasta?: string };
  totalPartidas: number;
  partidas: PartidaContable[];
}

export interface CuentaMayor {
  cuenta: string;
  nombreCuenta: string;
  saldoDeudor: number;
  saldoAcreedor: number;
  saldoNeto: number;
}

export interface BalanceComprobacion {
  fechaGeneracion: string;
  totalDebe: number;
  totalHaber: number;
  cuadrado: boolean;
  cuentas: CuentaMayor[];
}

export const contabilidadApi = {
  libroDiario: (desde?: string, hasta?: string) => {
    const q = new URLSearchParams();
    if (desde) q.set('desde', desde);
    if (hasta) q.set('hasta', hasta);
    return apiClient.get<LibroDiarioResponse>(`/api/contabilidad/libro-diario?${q.toString()}`);
  },

  libroMayor: (cuenta?: string) =>
    apiClient.get<any>(`/api/contabilidad/libro-mayor${cuenta ? `?cuenta=${cuenta}` : ''}`),

  balanceComprobacion: () =>
    apiClient.get<BalanceComprobacion>('/api/contabilidad/balance-comprobacion'),
};
