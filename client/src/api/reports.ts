import { apiClient } from './client'
import type { CompletionRowDto, ProductionRowDto, NotificationSummaryDto } from '../types/api'

export const reportsApi = {
  production: (dateFrom: string, dateTo: string) =>
    apiClient.get<ProductionRowDto[]>('/reports/production', { params: { dateFrom, dateTo } }).then((r) => r.data),
  completions: (dateFrom: string, dateTo: string) =>
    apiClient.get<CompletionRowDto[]>('/reports/completions', { params: { dateFrom, dateTo } }).then((r) => r.data),
}

export const notificationsApi = {
  get: (since?: string) =>
    apiClient.get<NotificationSummaryDto>('/notifications', { params: since ? { since } : {} }).then((r) => r.data),
}
