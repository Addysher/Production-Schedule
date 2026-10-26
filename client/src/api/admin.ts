import { apiClient } from './client'
import type {
  JobTypeDto,
  OperatorDto,
  ShiftDto,
  UserDto,
  SettingDto,
  ProductDto,
  BatchcutWatchPathDto,
} from '../types/api'

export const jobTypesApi = {
  getAll: () => apiClient.get<JobTypeDto[]>('/jobtypes').then((r) => r.data),
  create: (dto: Omit<JobTypeDto, 'id'>) => apiClient.post<JobTypeDto>('/jobtypes', dto).then((r) => r.data),
  update: (id: number, dto: Omit<JobTypeDto, 'id'>) =>
    apiClient.put<JobTypeDto>(`/jobtypes/${id}`, dto).then((r) => r.data),
  remove: (id: number) => apiClient.delete(`/jobtypes/${id}`),
}

export const operatorsApi = {
  getAll: () => apiClient.get<OperatorDto[]>('/operators').then((r) => r.data),
  create: (dto: Omit<OperatorDto, 'id'>) => apiClient.post<OperatorDto>('/operators', dto).then((r) => r.data),
  update: (id: number, dto: Omit<OperatorDto, 'id'>) =>
    apiClient.put<OperatorDto>(`/operators/${id}`, dto).then((r) => r.data),
  remove: (id: number) => apiClient.delete(`/operators/${id}`),
}

export const shiftsApi = {
  getAll: () => apiClient.get<ShiftDto[]>('/shifts').then((r) => r.data),
  create: (dto: Omit<ShiftDto, 'id' | 'breaks'> & { breaks: Omit<ShiftDto['breaks'][number], 'id'>[] }) =>
    apiClient.post<ShiftDto>('/shifts', dto).then((r) => r.data),
  update: (
    id: number,
    dto: Omit<ShiftDto, 'id' | 'breaks'> & { breaks: Omit<ShiftDto['breaks'][number], 'id'>[] },
  ) => apiClient.put<ShiftDto>(`/shifts/${id}`, dto).then((r) => r.data),
  remove: (id: number) => apiClient.delete(`/shifts/${id}`),
}

export const usersApi = {
  getAll: () => apiClient.get<UserDto[]>('/users').then((r) => r.data),
  create: (username: string, password: string, role: string) =>
    apiClient.post<UserDto>('/users', { username, password, role }).then((r) => r.data),
  update: (id: number, role: string, isActive: boolean) =>
    apiClient.put<UserDto>(`/users/${id}`, { role, isActive }).then((r) => r.data),
  resetPassword: (id: number, newPassword: string) =>
    apiClient.put(`/users/${id}/reset-password`, { newPassword }),
  remove: (id: number) => apiClient.delete(`/users/${id}`),
}

export const settingsApi = {
  getAll: () => apiClient.get<SettingDto[]>('/settings').then((r) => r.data),
  update: (key: string, value: string) =>
    apiClient.put<SettingDto>(`/settings/${key}`, { value }).then((r) => r.data),
}

export const productsApi = {
  getAll: () => apiClient.get<ProductDto[]>('/products').then((r) => r.data),
  create: (dto: Omit<ProductDto, 'id'>) => apiClient.post<ProductDto>('/products', dto).then((r) => r.data),
  update: (id: number, dto: Omit<ProductDto, 'id'>) => apiClient.put<ProductDto>(`/products/${id}`, dto).then((r) => r.data),
  remove: (id: number) => apiClient.delete(`/products/${id}`),
}

export const batchcutWatchPathsApi = {
  getAll: () => apiClient.get<BatchcutWatchPathDto[]>('/admin/batchcut-watch-paths').then((r) => r.data),
  create: (dto: Omit<BatchcutWatchPathDto, 'id'>) =>
    apiClient.post<BatchcutWatchPathDto>('/admin/batchcut-watch-paths', dto).then((r) => r.data),
  update: (id: number, dto: Omit<BatchcutWatchPathDto, 'id'>) =>
    apiClient.put<BatchcutWatchPathDto>(`/admin/batchcut-watch-paths/${id}`, dto).then((r) => r.data),
  remove: (id: number) => apiClient.delete(`/admin/batchcut-watch-paths/${id}`),
}
