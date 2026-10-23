import { useEffect, useState } from 'react'
import { jobsApi } from '../api/jobs'
import { useAuth } from '../auth/AuthContext'
import { isMovableStatus } from '../auth/permissions'
import type { JobDetailDto, JobStatus, JobSummaryDto, UpdateJobRequest } from '../types/api'
import { StatusPill } from '../components/StatusPill'
import { resolveStatusStyle } from '../components/statusStyles'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { JobEditModal } from '../components/JobEditModal'
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

  async function refresh() {
    setJobs(await jobsApi.getAll())
  }

  useEffect(() => {
    refresh().catch(() => {})
  }, [])

  const countFor = (s: JobStatus) => jobs.filter((j) => j.status === s && !(j.isLate && s !== 'completed')).length
  const lateCount = jobs.filter((j) => j.isLate).length
  const componentTypes = Array.from(new Set(jobs.map((j) => j.componentType).filter((c): c is string => !!c)))

  const filtered = jobs.filter((j) => {
    if (statusFilter === 'late' ? !j.isLate : statusFilter && j.status !== statusFilter) return false
    if (componentFilter && j.componentType !== componentFilter) return false
    if (dueFrom && (!j.dueDate || j.dueDate.slice(0, 10) < dueFrom)) return false
    if (dueTo && (!j.dueDate || j.dueDate.slice(0, 10) > dueTo)) return false
    return true
  })

  const detailOf = async (jobRef: string) => {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    return summary ? jobsApi.getById(summary.id) : null
  }

  async function remove(jobRef: string, id: number) {
    if (!confirm(`Delete job ${jobRef}? This cannot be undone.`)) return
    await jobsApi.remove(id)
    await refresh()
  }

  async function saveEdit(request: UpdateJobRequest) {
    if (!editingJob) return
    await jobsApi.update(editingJob.id, request)
    await refresh()
  }

  function toggleSelected(jobRef: string) {
    const next = new Set(selected)
    if (next.has(jobRef)) next.delete(jobRef)
    else next.add(jobRef)
    setSelected(next)
  }

  async function bulkApproveForCutting() {
    setBusy(true)
    try {
      for (const jobRef of selected) {
        if (jobs.find((j) => j.jobRef === jobRef)?.status === 'ready') {
          await jobsApi.approveForCutting(jobRef).catch(() => {})
        }
      }
      setSelected(new Set())
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  async function bulkMarkDelivered() {
    setBusy(true)
    try {
      for (const jobRef of selected) {
        const detail = await detailOf(jobRef)
        if (detail) {
          await jobsApi.update(detail.id, {
            supervisorName: detail.supervisorName,
            supervisorNumber: detail.supervisorNumber,
            bookedDeliveryDate: detail.bookedDeliveryDate,
            invoiceNumber: detail.invoiceNumber,
            delivered: true,
          })
        }
      }
      setSelected(new Set())
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  const canPin = (job: { status: JobStatus; componentType: string | null }) =>
    !!user?.canReschedule && isMovableStatus(job.status) && !!job.componentType

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
          <h2 style={{ marginTop: 0 }}>Jobs</h2>

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
                      <input type="checkbox" checked={selected.has(j.jobRef)} onChange={() => toggleSelected(j.jobRef)} />
                    </td>
                    <td>{j.jobRef}</td>
                    <td>{j.jobRef}</td>
                    <td>{j.stationName ?? '—'}</td>
                    <td>{j.dueDate ?? '—'}</td>
                    <td>{j.totalEqA ?? '—'}</td>
                    <td>
                      <StatusPill status={j.status} isLate={j.isLate} />
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6 }}>
                        <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={async () => setSelectedJob(await detailOf(j.jobRef))}>
                          View
                        </button>
                        {user?.isAdmin && (
                          <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={async () => setEditingJob(await detailOf(j.jobRef))}>
                            Edit
                          </button>
                        )}
                        {canPin(j) && (
                          <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={async () => setPinningJob(await detailOf(j.jobRef))}>
                            Schedule
                          </button>
                        )}
                        {user?.isAdmin && (
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
              <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Component Type</div>
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

            {user?.isAdmin && (
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
            user?.isAdmin && selectedJob.status === 'ready'
              ? async () => {
                  await jobsApi.approveForCutting(selectedJob.jobRef)
                  setSelectedJob(null)
                  await refresh()
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
            refresh()
          }}
        />
      )}
    </div>
  )
}
