/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import { apiClient } from './client';
import type { AuthResponse, LoginRequest } from '../types';

export const authApi = {
  login: (data: LoginRequest) =>
    apiClient.post<AuthResponse>('/api/auth/login', data),

  register: (data: { nombre: string; email: string; password: string; rol?: string }) =>
    apiClient.post<AuthResponse>('/api/auth/register', data),
};
