import { apiClient } from './client'
import type { StationDto, CapacityOverrideDto } from '../types/api'

export interface UpsertStationRequest {
  name: string
  componentType: string
  prdPath: string | null
  mpsPath: string | null
  isActive?: boolean
  displayOrder: number
  dailyEqATarget: number | null
  archivePath: string | null
  factoryId: number | null
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
  createCapacityOverride: (stationId: number, overrideDate: string, eqATarget: number) =>
    apiClient
      .post<CapacityOverrideDto>(`/stations/${stationId}/capacity-overrides`, { overrideDate, eqATarget })
      .then((r) => r.data),
  deleteCapacityOverride: (stationId: number, overrideId: number) =>
    apiClient.delete(`/stations/${stationId}/capacity-overrides/${overrideId}`),
}
