import type { JobStatus, SawBundleStatus } from '../types/api'

// What each job status means, what moves a job into it and what happens next (§5.0). One place, used by
// the status pill's tooltip, the Jobs page's status guide and each job's "next step".
export interface StatusInfo {
  status: JobStatus
  meaning: string
  setBy: string
  next: string
}

export const STATUS_GUIDE: StatusInfo[] = [
  {
    status: 'scheduled',
    meaning: 'A quote: the design is not final yet. It can be pencilled in, or get a firm place if it has a booked date, but it cannot be cut or made.',
    setBy: 'SCH import of a Quote design.',
    next: 'Re-importing the job as a FinalDesign moves it to Ready for production.',
  },
  {
    status: 'ready',
    meaning: 'The final design is in. The job waits for its saw files and its approval to cut.',
    setBy: 'SCH import of a FinalDesign (or the quote re-imported as one).',
    next: 'Once it has a booked delivery date and its saw files (MME) are in, an Admin approves it for cutting.',
  },
  {
    status: 'approved',
    meaning: 'Approved for cutting. The saw-file bundle has gone to the saw.',
    setBy: 'An Admin: Approve for cutting.',
    next: 'When Batchcut finishes the bundle (its DONE folder), the job becomes Cut and ready.',
  },
  {
    status: 'cut',
    meaning: 'Timber is cut. The job waits for its turn on the line; its MPS file is sent to the line beforehand.',
    setBy: 'The Batchcut watcher, when the bundle reaches the DONE folder.',
    next: 'The first "trusses made" entry in the line’s PRD file moves it to In production.',
  },
  {
    status: 'in-production',
    meaning: 'Being made on the line. Progress comes from the PRD files.',
    setBy: 'The PRD reader, on the first member made.',
    next: 'When every member is made, the job becomes Completed.',
  },
  {
    status: 'completed',
    meaning: 'Every member is made.',
    setBy: 'The PRD reader (or the status check when the app loads), once all members are done.',
    next: 'Mark it delivered when it leaves. Nightly maintenance archives finished jobs after the retention period.',
  },
]

// Flags shown alongside a status; they don't replace it.
export const FLAG_GUIDE: { flag: string; meaning: string }[] = [
  { flag: 'Late', meaning: 'Its due date (booked, else expected) has passed and it is not completed. Shown instead of the status colour.' },
  { flag: 'At risk', meaning: 'Planned to finish within 24 h of its due date (Settings: At-risk buffer).' },
  { flag: 'TBA', meaning: 'No booked delivery date: pencilled in by its expected date. It cannot be cut until it is booked.' },
  { flag: 'Frozen', meaning: 'Booked work planned inside the freeze window. The scheduler and drag-and-drop leave it where it is.' },
  { flag: 'Creep', meaning: 'Running more than the creep threshold behind its plan (PRD against the schedule). Approving it re-plans the line.' },
]

export const statusInfo = (status: JobStatus) => STATUS_GUIDE.find((s) => s.status === status)

export type NextStep = { text: string; action?: 'approve' | 'deliver'; waiting?: boolean }

// The one thing a job is waiting for right now, in plain words.
export function nextStep(job: {
  status: JobStatus
  isTba: boolean
  sawBundleStatus: SawBundleStatus
  sawFileCount?: number
  memberCount?: number
  delivered?: boolean
  completedEqA?: number | null
  totalEqA?: number | null
  isArchived?: boolean
}): NextStep {
  if (job.isArchived) return { text: job.delivered ? 'Archived — delivered' : 'Archived', waiting: true }
  switch (job.status) {
    case 'scheduled':
      return { text: job.isTba ? 'Needs the final design and a booked date' : 'Booked — needs the final design', waiting: true }
    case 'ready':
      if (job.isTba) return { text: 'Needs a booked delivery date to be cut', waiting: true }
      if (job.sawBundleStatus === 'waiting') return { text: 'Saw files complete — approve for cutting', action: 'approve' }
      if (job.sawBundleStatus === 'bundling')
        return { text: `Collecting saw files (${job.sawFileCount ?? 0} of ${job.memberCount ?? '?'}) — can be approved anyway`, action: 'approve' }
      return { text: 'Waiting for saw files (MME)', waiting: true }
    case 'approved':
      return { text: 'At the saw — waiting for Batchcut to finish', waiting: true }
    case 'cut':
      return { text: 'Waiting to start on the line', waiting: true }
    case 'in-production':
      return { text: job.totalEqA ? `Being made — ${Math.round(job.completedEqA ?? 0)} of ${Math.round(job.totalEqA)} Eq` : 'Being made', waiting: true }
    case 'completed':
      return job.delivered ? { text: 'Delivered' } : { text: 'Made — mark delivered when it leaves', action: 'deliver' }
  }
}
