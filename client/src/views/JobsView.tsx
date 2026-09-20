import { useEffect, useState } from 'react'
import { jobsApi } from '../api/jobs'
import { useAuth } from '../auth/AuthContext'
import type { JobDetailDto, JobSummaryDto, UpdateJobRequest } from '../types/api'
import { StatusPill } from '../components/StatusPill'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { JobEditModal } from '../components/JobEditModal'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'

export function JobsView() {
  const { user } = useAuth()
  const [jobs, setJobs] = useState<JobSummaryDto[]>([])
  const [selectedJob, setSelectedJob] = useState<JobDetailDto | null>(null)
  const [editingJob, setEditingJob] = useState<JobDetailDto | null>(null)
  const [statusFilter, setStatusFilter] = useState('')
  const [componentFilter, setComponentFilter] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)

  async function refresh() {
    setJobs(await jobsApi.getAll())
  }

  useEffect(() => {
    refresh().catch(() => {})
  }, [])

  const onTimeCount = jobs.filter((j) => !j.isLate && j.status !== 'completed').length
  const lateCount = jobs.filter((j) => j.isLate).length

  const componentTypes = Array.from(new Set(jobs.map((j) => j.componentType).filter((c): c is string => !!c)))

  const filtered = jobs.filter(
    (j) => (!statusFilter || j.status === statusFilter) && (!componentFilter || j.componentType === componentFilter),
  )

  async function view(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (summary) setSelectedJob(await jobsApi.getById(summary.id))
  }

  async function edit(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (summary) setEditingJob(await jobsApi.getById(summary.id))
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
        const job = jobs.find((j) => j.jobRef === jobRef)
        if (job?.status === 'ready') {
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
        const job = jobs.find((j) => j.jobRef === jobRef)
        if (job) {
          const detail = await jobsApi.getById(job.id)
          await jobsApi.update(job.id, {
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

  return (
    <div>
      <div style={{ display: 'flex', gap: 16 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <h2 style={{ marginTop: 0 }}>Jobs</h2>

          <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
            <span className="btn btn-secondary" style={{ cursor: 'default' }}>
              On time: {onTimeCount}
            </span>
            <span className="btn btn-secondary" style={{ cursor: 'default', color: '#ef4444', borderColor: '#ef4444' }}>
              Late: {lateCount}
            </span>
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
                        <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={() => view(j.jobRef)}>
                          View
                        </button>
                        {user?.isAdmin && (
                          <>
                            <button className="btn btn-secondary" style={{ padding: '3px 8px', fontSize: 12 }} onClick={() => edit(j.jobRef)}>
                              Edit
                            </button>
                            <button
                              className="btn btn-danger"
                              style={{ padding: '3px 8px', fontSize: 12 }}
                              onClick={() => remove(j.jobRef, j.id)}
                            >
                              Delete
                            </button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={8} style={{ textAlign: 'center', color: '#94a3b8' }}>
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
              <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Status</div>
              <select style={{ width: '100%' }} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
                <option value="">All</option>
                <option value="scheduled">Scheduled</option>
                <option value="ready">Ready for production</option>
                <option value="approved">Approved</option>
                <option value="cut">Cut and ready</option>
                <option value="in-production">In production</option>
                <option value="completed">Completed</option>
              </select>
            </label>
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
        />
      )}
      {editingJob && <JobEditModal job={editingJob} onClose={() => setEditingJob(null)} onSave={saveEdit} />}
    </div>
  )
}
