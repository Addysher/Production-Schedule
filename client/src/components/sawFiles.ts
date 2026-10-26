import { jobsApi } from '../api/jobs'
import type { JobStatus, SawBundleStatus } from '../types/api'

// Approve for cutting is offered once a ready job has saw files: straight away when the bundle is
// complete, or after a confirmation when some members' files are still missing.
export const canApproveForCutting = (job: { status: JobStatus; sawBundleStatus: SawBundleStatus }) =>
  job.status === 'ready' && (job.sawBundleStatus === 'waiting' || job.sawBundleStatus === 'bundling')

// Returns false when the user declined to send an incomplete bundle.
export async function approveForCuttingWithConfirm(jobRef: string): Promise<boolean> {
  try {
    await jobsApi.approveForCutting(jobRef)
    return true
  } catch (e) {
    const response = (e as { response?: { status?: number; data?: unknown } }).response
    if (response?.status !== 409) throw e
    const question = typeof response.data === 'string' ? response.data : `The saw files for ${jobRef} are incomplete. Approve anyway?`
    if (!window.confirm(question)) return false
    await jobsApi.approveForCutting(jobRef, true)
    return true
  }
}

export function sawFilesLabel(job: { sawBundleStatus: SawBundleStatus; sawFileCount: number; memberCount: number }): string {
  switch (job.sawBundleStatus) {
    case 'bundling':
      return `Collecting — ${job.sawFileCount} of ${job.memberCount} member file(s)`
    case 'waiting':
      return `Complete (${job.sawFileCount}) — in the waiting folder`
    case 'cutting':
      return 'With the saw'
    default:
      return 'None yet'
  }
}
