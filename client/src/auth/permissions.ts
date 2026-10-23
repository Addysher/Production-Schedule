import type { JobStatus, Role } from '../types/api'

// Who may move jobs on the Calendar / Schedule manually — mirrors the API's [Authorize] on schedule-manually.
export const canRescheduleRole = (role: Role | undefined) => role === 'Admin' || role === 'Supervisor'

// Mirrors JobsController.ScheduleManually: only work that hasn't started production can be moved.
export const isMovableStatus = (status: JobStatus) => status === 'ready' || status === 'approved' || status === 'cut'
