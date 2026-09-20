import { apiClient } from './client'
import type { ProductionRowDto, NotificationSummaryDto } from '../types/api'

export const reportsApi = {
  production: (dateFrom: string, dateTo: string) =>
    apiClient.get<ProductionRowDto[]>('/reports/production', { params: { dateFrom, dateTo } }).then((r) => r.data),
}

export const notificationsApi = {
  get: (since?: string) =>
    apiClient.get<NotificationSummaryDto>('/notifications', { params: since ? { since } : {} }).then((r) => r.data),
}
