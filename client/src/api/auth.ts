import { apiClient } from './client'
import type { LoginResponse, Role } from '../types/api'

export const authApi = {
  login: (username: string, password: string) =>
    apiClient.post<LoginResponse>('/auth/login', { username, password }).then((r) => r.data),
  logout: () => apiClient.post('/auth/logout'),
  me: () => apiClient.get<{ username: string; role: Role; factoryId: number | null }>('/auth/me').then((r) => r.data),
}
