export type Role = 'Admin' | 'Supervisor' | 'Viewer'

export interface LoginResponse {
  token: string
  username: string
  role: Role
}

export interface StationDto {
  id: number
  name: string
  componentType: string
  uncPath: string | null
  isActive: boolean
  displayOrder: number
  dailyEqATarget: number | null
  archivePath: string | null
}

export interface CapacityOverrideDto {
  id: number
  stationId: number
  overrideDate: string
  eqATarget: number
}

export interface JobTypeDto {
  id: number
  name: string
  componentType: string | null
  minsPerUnit: number
  isActive: boolean
}

export interface OperatorDto {
  id: number
  name: string
  shiftId: number | null
  isActive: boolean
}

export interface ShiftBreakDto {
  id: number
  breakStart: string
  breakDurationMins: number
}

export interface ShiftDto {
  id: number
  name: string
  startTime: string
  endTime: string
  isActive: boolean
  breaks: ShiftBreakDto[]
}

export interface UserDto {
  id: number
  username: string
  role: Role
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface SettingDto {
  key: string
  value: string
  updatedAt: string
}

export interface SchImportPathDto {
  id: number
  name: string
  uncPath: string
  componentType: string | null
  isActive: boolean
}

export interface BatchcutWatchPathDto {
  id: number
  name: string
  uncPath: string
  isActive: boolean
}

// Status values per §5.0 — 'late' is computed client-side from dueDate, not a stored workflow value.
export type JobStatus = 'scheduled' | 'ready' | 'approved' | 'cut' | 'in-production' | 'completed'

export interface JobSummaryDto {
  id: number
  jobRef: string
  componentType: string | null
  clientName: string | null
  stationId: number | null
  stationName: string | null
  dueDate: string | null
  totalEqA: number | null
  status: JobStatus
  isLate: boolean
  estimatedCompletionDate: string | null
  // "At risk" = tight margin between the schedule's own estimated completion and the booked delivery
  // date, not yet late. "Behind schedule" = the schedule's own estimated completion has already
  // passed and the job isn't done — a production-side slip, distinct from isLate's customer-due-date
  // sense. See JobStatusService.IsAtRisk / IsBehindSchedule.
  isAtRisk: boolean
  isBehindSchedule: boolean
}

export interface JobDetailDto {
  id: number
  jobRef: string
  componentType: string | null
  clientName: string | null
  siteAddress: string | null
  siteSuburb: string | null
  supervisorName: string | null
  supervisorNumber: string | null
  jobOrderDate: string | null
  jobOrderTime: string | null
  totalEqA: number | null
  completedEqA: number
  estimatedStartDate: string | null
  estimatedCompletionDate: string | null
  expectedDeliveryDate: string | null
  estBookedDeliveryDate: string | null
  bookedDeliveryDate: string | null
  invoiceNumber: string | null
  delivered: boolean
  status: JobStatus
  isLate: boolean
  designStatus: 'Quote' | 'FinalDesign'
  assignedStationId: number | null
  isManuallyScheduled: boolean
  manualStationId: number | null
  manualScheduleDate: string | null
}

export interface UpdateJobRequest {
  componentType?: string | null
  clientName?: string | null
  expectedDeliveryDate?: string | null
  estBookedDeliveryDate?: string | null
  clientRefNumber?: string | null
  siteAddress?: string | null
  siteSuburb?: string | null
  supervisorName?: string | null
  supervisorNumber?: string | null
  bookedDeliveryDate?: string | null
  invoiceNumber?: string | null
  delivered: boolean
}

export interface CreateJobRequest {
  jobRef: string
  componentType: string
  clientName?: string | null
  expectedDeliveryDate?: string | null
  estBookedDeliveryDate?: string | null
  clientRefNumber?: string | null
  siteAddress?: string | null
  siteSuburb?: string | null
  supervisorName?: string | null
  supervisorNumber?: string | null
  designStatus: 'Quote' | 'FinalDesign'
}

export interface ScheduleGenerationResult {
  jobsScheduled: number
  scheduleItemsWritten: number
  jobsSkipped: number
}

export interface UpdateJobResponse {
  job: JobDetailDto
  schedule: ScheduleGenerationResult
}

export interface ScheduleSegmentDto {
  date: string
  start: string
  end: string
  plannedEqA: number
}

export interface ScheduleItemDto {
  id: number
  jobRef: string
  clientName: string | null
  stationId: number
  stationName: string
  componentType: string | null
  scheduleDate: string
  plannedStart: string
  plannedEnd: string
  plannedEqA: number
  membersTotal: number
  actualStart: string | null
  actualEnd: string | null
  eqaCompleted: number
  membersCompleted: number
  creepDetected: boolean
  creepApprovedAt: string | null
  jobStatus: JobStatus
  segments: ScheduleSegmentDto[]
}

export interface QuoteLineRequest {
  jobTypeId: number
  qty: number
}

export interface QuoteEstimateRequest {
  ref: string
  lines: QuoteLineRequest[]
}

export interface QuoteLineResult {
  jobTypeId: number
  jobTypeName: string
  qty: number
  estimatedCompletion: string
}

export interface QuoteEstimateResponse {
  ref: string
  lines: QuoteLineResult[]
  fullOrderReadyAt: string
}

export interface BreakDto {
  start: string
  end: string
}

export interface CalendarSettingsDto {
  shiftStartHour: number
  shiftEndHour: number
  workingDays: number[]
  breaks: BreakDto[]
}

export interface ProductionRowDto {
  stationId: number
  stationName: string
  date: string
  eqa: number
}

export interface NotificationSummaryDto {
  newJobs: number
  errors: number
  serverNow: string
}
