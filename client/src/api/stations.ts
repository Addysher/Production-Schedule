import { apiClient } from './client'
import type { StationDto, CapacityOverrideDto } from '../types/api'

export interface UpsertStationRequest {
  name: string
  componentType: string
  prdPath: string | null
  mpsPath: string | null
  isActive?: boolean
  displayOrder: number
  eqaPerHour: number | null
  archivePath: string | null
  factoryId: number | null
}

// Overtime (a different start and/or end, "HH:mm:ss") or closed, for one line on one day.
export interface DayChange {
  overrideDate: string
  isClosed: boolean
  startTime: string | null
  endTime: string | null
  note: string | null
}

export const stationsApi = {
  getAll: () => apiClient.get<StationDto[]>('/stations').then((r) => r.data),
  create: (request: Omit<UpsertStationRequest, 'isActive'>) =>
    apiClient.post<StationDto>('/stations', request).then((r) => r.data),
  update: (id: number, request: UpsertStationRequest) =>
    apiClient.put<StationDto>(`/stations/${id}`, request).then((r) => r.data),
  remove: (id: number) => apiClient.delete(`/stations/${id}`),
  getCapacityOverrides: (stationId: number) =>
    apiClient.get<CapacityOverrideDto[]>(`/stations/${stationId}/capacity-overrides`).then((r) => r.data),
  createCapacityOverride: (stationId: number, change: DayChange) =>
    apiClient.post<CapacityOverrideDto>(`/stations/${stationId}/capacity-overrides`, change).then((r) => r.data),
  // A public holiday or site shutdown: the same change on every active line.
  createForAllLines: (change: DayChange) =>
    apiClient.post<CapacityOverrideDto[]>('/stations/capacity-overrides/all-lines', change).then((r) => r.data),
  deleteCapacityOverride: (stationId: number, overrideId: number) =>
    apiClient.delete(`/stations/${stationId}/capacity-overrides/${overrideId}`),
}
