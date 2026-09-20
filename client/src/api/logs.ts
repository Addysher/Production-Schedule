import { apiClient } from './client'
import type { MaintenanceLogDto, BatchcutEventDto } from '../types/logs'

export const logsApi = {
  getMaintenanceLogs: (take = 5) =>
    apiClient.get<MaintenanceLogDto[]>('/maintenance-logs', { params: { take } }).then((r) => r.data),
  getBatchcutEvents: (take = 50) =>
    apiClient.get<BatchcutEventDto[]>('/admin/batchcut-events', { params: { take } }).then((r) => r.data),
}
