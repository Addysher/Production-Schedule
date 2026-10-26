import type { JobStatus } from '../types/api'

export interface StatusStyle {
  label: string
  block: string
  border: string
  text: string
  badgeBg: string
  badgeText: string
}

const STATUS_STYLES: Record<JobStatus, StatusStyle> = {
  scheduled: {
    label: 'Quote stage', // stored as 'scheduled': a quote, not yet a final design
    block: 'var(--status-scheduled-block)',
    border: 'var(--status-scheduled-border)',
    text: 'var(--status-scheduled-text)',
    badgeBg: 'var(--status-scheduled-badge)',
    badgeText: 'var(--status-scheduled-badge-text)',
  },
  ready: {
    label: 'Ready for production',
    block: 'var(--status-ready-block)',
    border: 'var(--status-ready-border)',
    text: 'var(--status-ready-text)',
    badgeBg: 'var(--status-ready-badge)',
    badgeText: 'var(--status-ready-badge-text)',
  },
  approved: {
    label: 'Approved',
    block: 'var(--status-approved-block)',
    border: 'var(--status-approved-border)',
    text: 'var(--status-approved-text)',
    badgeBg: 'var(--status-approved-badge)',
    badgeText: 'var(--status-approved-badge-text)',
  },
  cut: {
    label: 'Cut and ready',
    block: 'var(--status-cut-block)',
    border: 'var(--status-cut-border)',
    text: 'var(--status-cut-text)',
    badgeBg: 'var(--status-cut-badge)',
    badgeText: 'var(--status-cut-badge-text)',
  },
  'in-production': {
    label: 'In production',
    block: 'var(--status-inproduction-block)',
    border: 'var(--status-inproduction-border)',
    text: 'var(--status-inproduction-text)',
    badgeBg: 'var(--status-inproduction-badge)',
    badgeText: 'var(--status-inproduction-badge-text)',
  },
  completed: {
    label: 'Completed',
    block: 'var(--status-completed-block)',
    border: 'var(--status-completed-border)',
    text: 'var(--status-completed-text)',
    badgeBg: 'var(--status-completed-badge)',
    badgeText: 'var(--status-completed-badge-text)',
  },
}

const LATE_STYLE: StatusStyle = {
  label: 'Late',
  block: 'var(--status-late-block)',
  border: 'var(--status-late-border)',
  text: 'var(--status-late-text)',
  badgeBg: 'var(--status-late-badge)',
  badgeText: 'var(--status-late-badge-text)',
}

// Late overrides any other status when the due date has passed and the job isn't Completed (§5.0).
export function resolveStatusStyle(status: JobStatus, isLate: boolean): StatusStyle {
  if (isLate && status !== 'completed') {
    return LATE_STYLE
  }
  return STATUS_STYLES[status] ?? STATUS_STYLES.scheduled
}
