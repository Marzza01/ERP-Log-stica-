import { apiClient } from './client';
import type { DashboardKpis, ModeloFinanciero } from '../types';

export const kpisApi = {
  getDashboard: (diasPeriodo = 30) =>
    apiClient.get<DashboardKpis>(`/api/kpis-dashboard?diasPeriodo=${diasPeriodo}`),

  getModeloFinanciero: () =>
    apiClient.get<ModeloFinanciero>('/api/kpis-dashboard/modelo-financiero'),
};
