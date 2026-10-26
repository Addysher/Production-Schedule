import { apiClient } from './client'
import type {
  JobSummaryDto,
  JobDetailDto,
  CreateJobRequest,
  UpdateJobRequest,
  UpdateJobResponse,
} from '../types/api'

export const jobsApi = {
  getAll: (includeArchived = false) =>
    apiClient.get<JobSummaryDto[]>('/jobs', { params: includeArchived ? { includeArchived: true } : undefined }).then((r) => r.data),
  getById: (id: number) => apiClient.get<JobDetailDto>(`/jobs/${id}`).then((r) => r.data),
  statusRefresh: () => apiClient.get<{ jobsUpdated: number }>('/jobs/status-refresh').then((r) => r.data),
  create: (request: CreateJobRequest) =>
    apiClient.post<UpdateJobResponse>('/jobs', request).then((r) => r.data),
  update: (id: number, request: UpdateJobRequest) =>
    apiClient.put<UpdateJobResponse>(`/jobs/${id}`, request).then((r) => r.data),
  remove: (id: number) => apiClient.delete(`/jobs/${id}`),
  // force: send a bundle that's still missing members' saw files (the server answers 409 without it).
  approveForCutting: (jobRef: string, force = false) =>
    apiClient
      .post<JobDetailDto>(`/jobs/${jobRef}/approve-for-cutting`, null, { params: force ? { force: true } : undefined })
      .then((r) => r.data),
  scheduleManually: (jobRef: string, stationId: number, scheduleDate: string) =>
    apiClient
      .post<UpdateJobResponse>(`/jobs/${jobRef}/schedule-manually`, { stationId, scheduleDate })
      .then((r) => r.data),
  unpinSchedule: (jobRef: string) =>
    apiClient.delete<UpdateJobResponse>(`/jobs/${jobRef}/schedule-manually`).then((r) => r.data),
}
