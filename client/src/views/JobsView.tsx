import { useEffect, useRef, useState } from 'react'
import { jobsApi } from '../api/jobs'
import { useAuth } from '../auth/AuthContext'
import { isMovableStatus } from '../auth/permissions'
import type { JobDetailDto, JobStatus, JobSummaryDto, UpdateJobRequest } from '../types/api'
import { StatusPill } from '../components/StatusPill'
import { TbaTag } from '../components/TbaTag'
import { LockIcon } from '../components/CalendarBlock'
import { resolveStatusStyle } from '../components/statusStyles'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { JobEditModal, updateRequestFrom } from '../components/JobEditModal'
import { errorText } from '../components/Modal'
import { approveForCuttingWithConfirm, canApproveForCutting } from '../components/sawFiles'
import { nextStep } from '../components/jobStatusInfo'
import { StatusGuide } from '../components/StatusGuide'
import { signalJobsChanged } from '../utils/appEvents'
import { ScheduleManuallyModal } from '../components/ScheduleManuallyModal'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'

const STATUSES: JobStatus[] = ['scheduled', 'ready', 'approved', 'cut', 'in-production', 'completed']
const SHOW_GUIDE_KEY = 'jobs.showStatusGuide'

const formatDue = (iso: string | null) =>
  iso ? new Date(`${iso.slice(0, 10)}T00:00`).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' }) : '—'

const small = { padding: '3px 8px', fontSize: 12 } as const

// The less-used actions for a row, behind a "More" button.
function RowMenu({ items }: { items: { label: string; onClick: () => void; danger?: boolean }[] }) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false)
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [open])
  if (items.length === 0) return null
  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <button className="btn btn-secondary" style={small} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>
        More ▾
      </button>
      {open && (
        <div
          role="menu"
          className="card"
          style={{ position: 'absolute', right: 0, top: '100%', marginTop: 4, padding: 4, zIndex: 20, minWidth: 170, display: 'flex', flexDirection: 'column', boxShadow: '0 6px 18px rgba(0,0,0,0.15)' }}
        >
          {items.map((item) => (
            <button
              key={item.label}
              role="menuitem"
              className="btn btn-secondary"
              style={{ ...small, border: 'none', textAlign: 'left', justifyContent: 'flex-start', color: item.danger ? '#dc2626' : undefined }}
              onClick={() => {
                setOpen(false)
                item.onClick()
              }}
            >
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

// Jobs page (§5.5): every job with what it's waiting for, and the one action that moves it on.
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
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [showArchived, setShowArchived] = useState(false)
  const [showGuide, setShowGuide] = useState(() => {
    try {
      return localStorage.getItem(SHOW_GUIDE_KEY) === 'true'
    } catch {
      return false
    }
  })

  function toggleGuide() {
    setShowGuide(!showGuide)
    try {
      localStorage.setItem(SHOW_GUIDE_KEY, String(!showGuide))
    } catch {
      // not remembered — fine
    }
  }

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

  const term = search.trim().toLowerCase()
  const filtered = visible
    .filter((j) => {
      if (statusFilter === 'late' ? !j.isLate : statusFilter && j.status !== statusFilter) return false
      if (componentFilter && j.componentType !== componentFilter) return false
      if (dueFrom && (!j.dueDate || j.dueDate.slice(0, 10) < dueFrom)) return false
      if (dueTo && (!j.dueDate || j.dueDate.slice(0, 10) > dueTo)) return false
      if (term && !`${j.jobRef} ${j.clientName ?? ''}`.toLowerCase().includes(term)) return false
      return true
    })
    .sort((a, b) => (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999') || a.jobRef.localeCompare(b.jobRef))

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

  async function approve(jobRef: string) {
    setBusy(true)
    try {
      if (await approveForCuttingWithConfirm(jobRef)) setNotice(`${jobRef} approved for cutting.`)
    } catch (e) {
      setNotice(errorText(e, `Could not approve ${jobRef}.`))
    } finally {
      setBusy(false)
    }
    await reloadAfterChange()
  }

  async function markDelivered(job: JobSummaryDto) {
    setBusy(true)
    try {
      const detail = await jobsApi.getById(job.id)
      await jobsApi.update(detail.id, { ...updateRequestFrom(detail), delivered: true })
    } catch (e) {
      setNotice(errorText(e, `Could not mark ${job.jobRef} delivered.`))
    } finally {
      setBusy(false)
    }
    await reloadAfterChange()
  }

  function toggleSelected(jobRef: string) {
    const next = new Set(selected)
    if (next.has(jobRef)) next.delete(jobRef)
    else next.add(jobRef)
    setSelected(next)
  }

  // Bulk actions only touch selected rows that are shown: a filter or search hides the rest from them.
  const selectedShown = filtered.filter((j) => selected.has(j.jobRef)).map((j) => j.jobRef)
  const hiddenSelected = selected.size - selectedShown.length
  const allShownSelected = filtered.length > 0 && selectedShown.length === filtered.length
  const toggleAllShown = () => {
    const next = new Set(selected)
    for (const j of filtered) {
      if (allShownSelected) next.delete(j.jobRef)
      else next.add(j.jobRef)
    }
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
      for (const jobRef of selectedShown) {
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
    if (skipped) parts.push(`${skipped} skipped (not at that step)`)
    if (problems.length) parts.push(`${problems.length} failed — ${problems.join('; ')}`)
    setNotice(parts.join(', ') + '.')
  }

  const bulkApproveForCutting = () =>
    bulk('Approve for cutting', async (jobRef) => {
      // Only complete bundles in bulk; an incomplete one needs its own confirmation.
      const job = jobs.find((j) => j.jobRef === jobRef)
      if (job?.status !== 'ready' || job.isTba || job.sawBundleStatus !== 'waiting') return 'skipped'
      await jobsApi.approveForCutting(jobRef)
      return 'done'
    })

  const bulkMarkDelivered = () =>
    bulk('Mark delivered', async (jobRef) => {
      const summary = jobs.find((j) => j.jobRef === jobRef)
      if (!summary || summary.status !== 'completed' || summary.delivered) return 'skipped'
      const detail = await jobsApi.getById(summary.id)
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

  // The button for the step a job is at, when the signed-in user can take it.
  function primaryAction(j: JobSummaryDto, step: ReturnType<typeof nextStep>) {
    if (j.isArchived || !user?.isAdmin) return null
    if (step.action === 'approve' && canApproveForCutting(j))
      return (
        <button className="btn btn-primary" style={small} disabled={busy} onClick={() => approve(j.jobRef)}>
          Approve for cutting
        </button>
      )
    if (step.action === 'deliver')
      return (
        <button className="btn btn-primary" style={small} disabled={busy} onClick={() => markDelivered(j)}>
          Mark delivered
        </button>
      )
    return null
  }

  function menuItems(j: JobSummaryDto) {
    if (j.isArchived) return []
    const items: { label: string; onClick: () => void; danger?: boolean }[] = []
    if (user?.isAdmin) items.push({ label: 'Edit details', onClick: async () => setEditingJob(await detailOf(j.jobRef)) })
    if (canPin(j))
      items.push({ label: j.isManuallyScheduled ? 'Move / unpin' : 'Pin to a line and day', onClick: async () => setPinningJob(await detailOf(j.jobRef)) })
    if (user?.isAdmin) items.push({ label: 'Delete job', onClick: () => remove(j.jobRef, j.id), danger: true })
    return items
  }

  return (
    <div>
      <div style={{ display: 'flex', gap: 16 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <h2 style={{ marginTop: 0 }}>{showArchived ? 'Archived jobs' : 'Jobs'}</h2>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search job or client" aria-label="Search jobs" style={{ width: 200 }} />
              <button className="btn btn-secondary" onClick={toggleGuide} aria-expanded={showGuide}>
                {showGuide ? 'Hide status guide' : 'How statuses work'}
              </button>
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
          </div>
          {showGuide && <StatusGuide />}
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

          {user?.isAdmin && selectedShown.length > 0 && (
            <div className="card" style={{ marginBottom: 12, display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, flexWrap: 'wrap' }}>
              <b>{selectedShown.length} selected</b>
              {hiddenSelected > 0 && <span style={{ color: 'var(--text-muted)' }}>({hiddenSelected} more hidden by the filters — not included)</span>}
              <button className="btn btn-secondary" style={small} disabled={busy} onClick={bulkApproveForCutting} title="Ready, booked jobs whose saw files are complete">
                Approve for cutting
              </button>
              <button className="btn btn-secondary" style={small} disabled={busy} onClick={bulkMarkDelivered}>
                Mark delivered
              </button>
              <button className="btn btn-secondary" style={small} disabled={busy} onClick={() => setSelected(new Set())}>
                Clear selection
              </button>
            </div>
          )}

          <div className="card">
            <table>
              <thead>
                <tr>
                  <th>{user?.isAdmin && !showArchived && <input type="checkbox" checked={allShownSelected} onChange={toggleAllShown} aria-label="Select all shown" />}</th>
                  <th>Job Reference</th>
                  <th>Client</th>
                  <th>Product</th>
                  <th>Line</th>
                  <th>Due</th>
                  <th>EqA</th>
                  <th>Status · next step</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((j) => {
                  const step = nextStep(j)
                  return (
                    <tr key={j.jobRef}>
                      <td>{user?.isAdmin && !j.isArchived && <input type="checkbox" checked={selected.has(j.jobRef)} onChange={() => toggleSelected(j.jobRef)} aria-label={`Select ${j.jobRef}`} />}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        <b>{j.jobRef}</b>
                        {j.isFrozen && (
                          <span title="Inside the freeze window — the schedule leaves it where it is" style={{ marginLeft: 6, display: 'inline-flex', opacity: 0.7 }}>
                            <LockIcon />
                          </span>
                        )}
                      </td>
                      <td>{j.clientName ?? '—'}</td>
                      <td>{j.componentType ?? '—'}</td>
                      <td>{j.stationName ?? '—'}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        {formatDue(j.dueDate)}
                        {j.isTba ? <TbaTag /> : null}
                        {j.isAtRisk && !j.isLate && (
                          <span title="Planned to finish within the at-risk buffer of its due date" style={{ marginLeft: 6, fontSize: 10, fontWeight: 700, padding: '0 4px', borderRadius: 3, background: '#f59e0b', color: '#1f2937' }}>
                            AT RISK
                          </span>
                        )}
                      </td>
                      <td>{j.totalEqA ?? '—'}</td>
                      <td>
                        <StatusPill status={j.status} isLate={j.isLate} />
                        <div style={{ fontSize: 12, marginTop: 3, color: step.waiting ? 'var(--text-muted)' : 'var(--text-primary)', fontWeight: step.action ? 600 : 400 }}>{step.text}</div>
                      </td>
                      <td>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                          {primaryAction(j, step)}
                          <button className="btn btn-secondary" style={small} onClick={async () => setSelectedJob(await detailOf(j.jobRef))}>
                            View
                          </button>
                          <RowMenu items={menuItems(j)} />
                        </div>
                      </td>
                    </tr>
                  )
                })}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={9} style={{ textAlign: 'center', color: 'var(--text-faint)' }}>
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
            {(statusFilter || componentFilter || dueFrom || dueTo || search) && (
              <button
                className="btn btn-secondary"
                onClick={() => {
                  setStatusFilter('')
                  setComponentFilter('')
                  setDueFrom('')
                  setDueTo('')
                  setSearch('')
                }}
              >
                Clear filters
              </button>
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
