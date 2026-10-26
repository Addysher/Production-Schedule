import { useEffect, useState } from 'react'
import { jobsApi } from '../api/jobs'
import { useAuth } from '../auth/AuthContext'
import { isMovableStatus } from '../auth/permissions'
import type { JobDetailDto, JobStatus, JobSummaryDto, UpdateJobRequest } from '../types/api'
import { StatusPill } from '../components/StatusPill'
import { TbaTag } from '../components/TbaTag'
import { resolveStatusStyle } from '../components/statusStyles'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { JobEditModal, updateRequestFrom } from '../components/JobEditModal'
import { errorText } from '../components/Modal'
import { approveForCuttingWithConfirm, canApproveForCutting } from '../components/sawFiles'
import { signalJobsChanged } from '../utils/appEvents'
import { ScheduleManuallyModal } from '../components/ScheduleManuallyModal'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'

const STATUSES: JobStatus[] = ['scheduled', 'ready', 'approved', 'cut', 'in-production', 'completed']

export function JobsView() {
  const { user } = useAuth()
  const [jobs, setJobs] = useState<JobSummaryDto[]>([])
  const [selectedJob, setSelectedJob] = useState<JobDetailDto | null>(null)
  const [editingJob, setEditingJob] = useState<JobDetailDto | null>(null)
  const [pinningJob, setPinningJob] = useState<JobDetailDto | null>(null)
  const [statusFilter, setStatusFilter] = useState<JobStatus | 'late' | ''>('')
  const [componentFilter, setComponentFilter] = useState('')
  const [dueFrom, setDueFrom] = useState('')
  const [dueTo, setDueTo] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [showArchived, setShowArchived] = useState(false)

  async function refresh() {
    setJobs(await jobsApi.getAll(showArchived))
  }

  // After a change made here: reload, and let the sidebar's late count follow.
  async function reloadAfterChange() {
    signalJobsChanged()
    await refresh().catch((e) => setNotice(errorText(e, 'Could not refresh jobs.')))
  }

  useEffect(() => {
    refresh().catch((e) => setNotice(errorText(e, 'Could not load jobs.')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showArchived])

  // Either the live jobs or (Admin) the archived ones — never both at once.
  const visible = jobs.filter((j) => j.isArchived === showArchived)

  // Each chip's count is exactly what clicking it shows, so a late job counts under its status and under Late.
  const countFor = (s: JobStatus) => visible.filter((j) => j.status === s).length
  const lateCount = visible.filter((j) => j.isLate).length
  const componentTypes = Array.from(new Set(visible.map((j) => j.componentType).filter((c): c is string => !!c)))

  const filtered = visible.filter((j) => {
    if (statusFilter === 'late' ? !j.isLate : statusFilter && j.status !== statusFilter) return false
    if (componentFilter && j.componentType !== componentFilter) return false
    if (dueFrom && (!j.dueDate || j.dueDate.slice(0, 10) < dueFrom)) return false
    if (dueTo && (!j.dueDate || j.dueDate.slice(0, 10) > dueTo)) return false
    return true
  })

  const detailOf = async (jobRef: string) => {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (!summary) return null
    try {
      return await jobsApi.getById(summary.id)
    } catch (e) {
      setNotice(errorText(e, `Could not load ${jobRef}.`))
      return null
    }
  }

  async function remove(jobRef: string, id: number) {
    if (!confirm(`Delete job ${jobRef}? This cannot be undone.`)) return
    try {
      await jobsApi.remove(id)
    } catch (e) {
      setNotice(errorText(e, `Could not delete ${jobRef}.`))
    }
    await reloadAfterChange()
  }

  async function saveEdit(request: UpdateJobRequest) {
    if (!editingJob) return
    await jobsApi.update(editingJob.id, request)
    await reloadAfterChange()
  }

  function toggleSelected(jobRef: string) {
    const next = new Set(selected)
    if (next.has(jobRef)) next.delete(jobRef)
    else next.add(jobRef)
    setSelected(next)
  }

  // Runs an action over the selected jobs, carrying on past failures and reporting them together.
  async function bulk(action: string, run: (jobRef: string) => Promise<'done' | 'skipped'>) {
    setBusy(true)
    setNotice(null)
    const problems: string[] = []
    let done = 0
    let skipped = 0
    try {
      for (const jobRef of selected) {
        try {
          if ((await run(jobRef)) === 'done') done++
          else skipped++
        } catch (e) {
          problems.push(`${jobRef}: ${errorText(e, 'failed')}`)
        }
      }
      setSelected(new Set())
      await reloadAfterChange()
    } finally {
      setBusy(false)
    }
    const parts = [`${action}: ${done} done`]
    if (skipped) parts.push(`${skipped} skipped (not applicable)`)
    if (problems.length) parts.push(`${problems.length} failed — ${problems.join('; ')}`)
    setNotice(parts.join(', ') + '.')
  }

  const bulkApproveForCutting = () =>
    bulk('Approve for cutting', async (jobRef) => {
      // Only complete bundles in bulk; an incomplete one needs its own confirmation from the job's popup.
      const job = jobs.find((j) => j.jobRef === jobRef)
      if (job?.status !== 'ready' || job.isTba || job.sawBundleStatus !== 'waiting') return 'skipped'
      await jobsApi.approveForCutting(jobRef)
      return 'done'
    })

  const bulkMarkDelivered = () =>
    bulk('Mark delivered', async (jobRef) => {
      const summary = jobs.find((j) => j.jobRef === jobRef)
      if (!summary) return 'skipped'
      const detail = await jobsApi.getById(summary.id)
      if (detail.delivered) return 'skipped'
      await jobsApi.update(detail.id, { ...updateRequestFrom(detail), delivered: true })
      return 'done'
    })

  const canPin = (job: { status: JobStatus; componentType: string | null; isFrozen: boolean }) =>
    !!user?.canReschedule && isMovableStatus(job.status) && !!job.componentType && !job.isFrozen

  const chip = (label: string, count: number, value: JobStatus | 'late' | '', color?: string) => (
    <button
      key={label}
      className="btn btn-secondary"
      onClick={() => setStatusFilter(statusFilter === value ? '' : value)}
      style={{
        color: color ?? 'var(--brand-primary)',
        borderColor: color ?? 'var(--brand-primary)',
        background: statusFilter === value && value !== '' ? 'var(--divider-faint)' : 'var(--card-bg)',
        fontWeight: statusFilter === value && value !== '' ? 800 : 600,
      }}
    >
      {label}: {count}
    </button>
  )

  return (
    <div>
      <div style={{ display: 'flex', gap: 16 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12 }}>
            <h2 style={{ marginTop: 0 }}>{showArchived ? 'Archived jobs' : 'Jobs'}</h2>
            {user?.isAdmin && (
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }} title="Finished jobs tidied away by nightly maintenance">
                <input
                  type="checkbox"
                  checked={showArchived}
                  onChange={(e) => {
                    setSelected(new Set())
                    setStatusFilter('')
                    setShowArchived(e.target.checked)
                  }}
                />
                Show archived
              </label>
            )}
          </div>
          {notice && (
            <div className="card" style={{ marginBottom: 12, display: 'flex', justifyContent: 'space-between', gap: 12, fontSize: 13 }}>
              <span>{notice}</span>
              <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => setNotice(null)}>
                Dismiss
              </button>
            </div>
          )}

          <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
            {STATUSES.map((s) => chip(resolveStatusStyle(s, false).label, countFor(s), s, resolveStatusStyle(s, false).border))}
            {chip('Late', lateCount, 'late', '#ef4444')}
          </div>

          <div className="card">
            <table>
              <thead>
                <tr>
                  <th></th>
                  <th>Job Reference</th>
                  <th>Structure Name</th>
                  <th>Station</th>
                  <th>Due Date</th>
                  <th>Prod. Value</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((j) => (
                  <tr key={j.jobRef}>
                    <td>
                      {!j.isArchived && <input type="checkbox" checked={selected.has(j.jobRef)} onChange={() => toggleSelected(j.jobRef)} />}
                    </td>
                    <td>{j.jobRef}</td>
                    <td>{j.jobRef}</td>
                    <td>{j.stationName ?? '—'}</td>
                    <td>{j.dueDate ?? '—'}</td>
                    <td>{j.totalEqA ?? '—'}</td>
                    <td>
                      <StatusPill status={j.status} isLate={j.isLate} />
                      {j.isTba && <TbaTag />}
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6 }}>
                        <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={async () => setSelectedJob(await detailOf(j.jobRef))}>
                          View
                        </button>
                        {user?.isAdmin && !j.isArchived && (
                          <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={async () => setEditingJob(await detailOf(j.jobRef))}>
                            Edit
                          </button>
                        )}
                        {canPin(j) && !j.isArchived && (
                          <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={async () => setPinningJob(await detailOf(j.jobRef))}>
                            Schedule
                          </button>
                        )}
                        {user?.isAdmin && !j.isArchived && (
                          <button className="btn btn-danger" style={{ padding: '3px 8px', fontSize: 12 }} onClick={() => remove(j.jobRef, j.id)}>
                            Delete
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={8} style={{ textAlign: 'center', color: 'var(--text-faint)' }}>
                      No jobs match the current filter
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>

        <CollapsibleSidePanel viewKey="jobs" title="Filters">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <label>
              <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Product</div>
              <select style={{ width: '100%' }} value={componentFilter} onChange={(e) => setComponentFilter(e.target.value)}>
                <option value="">All</option>
                {componentTypes.map((c) => (
                  <option key={c} value={c}>
                    {c}
                  </option>
                ))}
              </select>
            </label>
            <div>
              <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Due date</div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                <input type="date" value={dueFrom} onChange={(e) => setDueFrom(e.target.value)} aria-label="Due from" />
                <input type="date" value={dueTo} onChange={(e) => setDueTo(e.target.value)} aria-label="Due to" />
              </div>
            </div>
            {(statusFilter || componentFilter || dueFrom || dueTo) && (
              <button
                className="btn btn-secondary"
                onClick={() => {
                  setStatusFilter('')
                  setComponentFilter('')
                  setDueFrom('')
                  setDueTo('')
                }}
              >
                Clear filters
              </button>
            )}

            {user?.isAdmin && !showArchived && (
              <div>
                <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 8 }}>Bulk actions ({selected.size} selected)</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                  <button className="btn btn-secondary" disabled={selected.size === 0 || busy} onClick={bulkApproveForCutting}>
                    Approve for cutting
                  </button>
                  <button className="btn btn-secondary" disabled={selected.size === 0 || busy} onClick={bulkMarkDelivered}>
                    Mark delivered
                  </button>
                </div>
              </div>
            )}
          </div>
        </CollapsibleSidePanel>
      </div>

      {selectedJob && (
        <JobDetailPopup
          job={selectedJob}
          onClose={() => setSelectedJob(null)}
          onEdit={
            user?.isAdmin
              ? () => {
                  setEditingJob(selectedJob)
                  setSelectedJob(null)
                }
              : undefined
          }
          onApproveForCutting={
            user?.isAdmin && canApproveForCutting(selectedJob)
              ? async () => {
                  if (!(await approveForCuttingWithConfirm(selectedJob.jobRef))) return
                  setSelectedJob(null)
                  await reloadAfterChange()
                }
              : undefined
          }
          onScheduleManually={
            canPin(selectedJob)
              ? () => {
                  setPinningJob(selectedJob)
                  setSelectedJob(null)
                }
              : undefined
          }
        />
      )}
      {editingJob && <JobEditModal job={editingJob} onClose={() => setEditingJob(null)} onSave={saveEdit} />}
      {pinningJob && (
        <ScheduleManuallyModal
          job={pinningJob}
          onClose={() => setPinningJob(null)}
          onDone={() => {
            setPinningJob(null)
            reloadAfterChange()
          }}
        />
      )}
    </div>
  )
}
