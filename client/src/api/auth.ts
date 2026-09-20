import { apiClient } from './client'
import type { LoginResponse } from '../types/api'

export const authApi = {
  login: (username: string, password: string) =>
    apiClient.post<LoginResponse>('/auth/login', { username, password }).then((r) => r.data),
  logout: () => apiClient.post('/auth/logout'),
  changePassword: (currentPassword: string, newPassword: string) =>
    apiClient.put('/auth/change-password', { currentPassword, newPassword }),
}
