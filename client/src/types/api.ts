// Kiosk: a factory-floor display account that only sees the kiosk page.
export type Role = 'Admin' | 'Supervisor' | 'Viewer' | 'Kiosk'

export interface LoginResponse {
  token: string
  username: string
  role: Role
  factoryId: number | null // a Kiosk login's factory
}

export interface StationDto {
  id: number
  name: string
  componentType: string
  prdPath: string | null // DataOut: PaperlessJig's .prd files
  mpsPath: string | null // DataIn: MPS files from the pool
  isActive: boolean
  displayOrder: number
  eqaPerHour: number | null // what the line makes per working hour
  todayTargetEqa: number | null // eqaPerHour × today's working hours (with any one-off change)
  todayChange: string | null // today's one-off change, e.g. "Closed — Easter Monday" or "06:00–17:00 — Overtime"
  archivePath: string | null
  factoryId: number | null
}

// v3.0: groups stations for the kiosk display; scheduling ignores it.
export interface FactoryDto {
  id: number
  name: string
  isActive: boolean
  stationIds: number[]
}

// A one-off change to a line's working day: overtime hours (start and/or end), or closed.
export interface CapacityOverrideDto {
  id: number
  stationId: number
  overrideDate: string
  isClosed: boolean
  startTime: string | null // "HH:mm:ss"
  endTime: string | null
  note: string | null
}

export interface JobTypeDto {
  id: number
  name: string
  componentType: string | null
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
  factoryId: number | null // Kiosk accounts: the factory whose display this login shows
  factoryName: string | null
}

export interface SettingDto {
  key: string
  value: string
  updatedAt: string
}

// What the factory makes. Its name is what stations, job types and jobs call 'componentType'.
export interface ProductDto {
  id: number
  name: string
  poolPath: string | null
  archivePath: string | null
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

// §5.11: where the job's saw-file bundle is — null: no files yet; bundling: still collecting (some
// members missing); waiting: complete, ready to approve; cutting: approved and with the saw.
export type SawBundleStatus = null | 'bundling' | 'waiting' | 'cutting'

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
  isFrozen: boolean
  sawBundleStatus: SawBundleStatus
  isArchived: boolean
  isTba: boolean // no booked delivery date: pencilled in, not produced until booked
  delivered: boolean
  sawFileCount: number
  memberCount: number
  completedEqA: number | null
  isManuallyScheduled: boolean
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
  schComponentType: string | null
  isFrozen: boolean
  clientRefNumber: string | null
  sawBundleStatus: SawBundleStatus
  sawFileCount: number
  memberCount: number
  isTba: boolean
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
  jobsFrozen: number
  jobsPencilled: number
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
  isFrozen: boolean
  isTba: boolean
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
  freezeUntil: string | null // end of the freeze window; null = off
  dayStart: string // exact working day, "HH:mm" (shiftStartHour/shiftEndHour are the whole hours the grid spans)
  dayEnd: string
}

export interface ProductionRowDto {
  stationId: number
  stationName: string
  date: string
  eqa: number
}

// The feedback loop: per line today, the Eq the plan had made by now vs what the PRD files say was made.
export interface LineProgressDto {
  stationId: number
  plannedByNow: number
  madeToday: number
  plannedToday: number
}

// Dashboard: the member being made on a running job, and the one after it (§7).
export interface StationProgressDto {
  stationId: number
  jobRef: string
  currentMemberId: string | null
  nextMemberId: string | null
  membersDone: number
  membersTotal: number
}

export interface CompletionRowDto {
  date: string
  completed: number
  completedLate: number
  latePercent: number
  lateJobRefs: string[]
}

export interface NotificationSummaryDto {
  newJobs: number
  errors: number
  serverNow: string
}
