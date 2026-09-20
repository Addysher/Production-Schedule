import { apiClient } from './client'
import type { ScheduleGenerationResult, ScheduleItemDto } from '../types/api'

export const scheduleApi = {
  getCurrent: () => apiClient.get<ScheduleItemDto[]>('/schedule/current').then((r) => r.data),
  generate: () => apiClient.post<ScheduleGenerationResult>('/schedule/generate').then((r) => r.data),
  exportUrl: (stationId?: number, dateFrom?: string, dateTo?: string) => {
    const params = new URLSearchParams()
    if (stationId) params.set('stationId', String(stationId))
    if (dateFrom) params.set('dateFrom', dateFrom)
    if (dateTo) params.set('dateTo', dateTo)
    const qs = params.toString()
    return `/api/schedule/export${qs ? `?${qs}` : ''}`
  },
  approveCreep: (scheduleItemId: number) => apiClient.post(`/schedule-items/${scheduleItemId}/approve-creep`),
}
