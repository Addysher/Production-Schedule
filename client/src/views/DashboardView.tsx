import { localIsoDate } from '../utils/dates'
import { useEffect, useState } from 'react'
import { jobsApi } from '../api/jobs'
import { scheduleApi } from '../api/schedule'
import { stationsApi } from '../api/stations'
import { logsApi } from '../api/logs'
import { reportsApi } from '../api/reports'
import { useAuth } from '../auth/AuthContext'
import type { JobSummaryDto, JobDetailDto, ScheduleItemDto, StationDto, UpdateJobRequest, StationProgressDto, LineProgressDto } from '../types/api'
import { LineProgress } from '../components/LineProgress'
import type { MaintenanceLogDto } from '../types/logs'
import { StatusPill } from '../components/StatusPill'
import { TbaTag } from '../components/TbaTag'
import { CreepBadge } from '../components/CreepBadge'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'
import { ScheduleManuallyModal } from '../components/ScheduleManuallyModal'
import { isMovableStatus } from '../auth/permissions'
import { JobEditModal } from '../components/JobEditModal'
import { errorText } from '../components/Modal'
import { approveForCuttingWithConfirm, canApproveForCutting } from '../components/sawFiles'
import { signalJobsChanged, startAutoRefresh } from '../utils/appEvents'

function Tile({
  label,
  value,
  accent,
  onClick,
  active,
  title,
}: {
  label: string
  value: number
  accent?: string
  onClick?: () => void
  active?: boolean
  title?: string
}) {
  return (
    <div
      className="card"
      onClick={onClick}
      title={title}
      style={{
        flex: 1,
        minWidth: 140,
        cursor: onClick ? 'pointer' : undefined,
        outline: active ? `2px solid ${accent ?? 'var(--brand-primary)'}` : undefined,
      }}
    >
      <div style={{ fontSize: 28, fontWeight: 700, color: accent ?? 'var(--brand-primary)' }}>{value}</div>
      <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>{label}</div>
    </div>
  )
}

export function DashboardView() {
  const { user } = useAuth()
  const [jobs, setJobs] = useState<JobSummaryDto[]>([])
  const [items, setItems] = useState<ScheduleItemDto[]>([])
  const [stations, setStations] = useState<StationDto[]>([])
  const [logs, setLogs] = useState<MaintenanceLogDto[]>([])
  const [producedToday, setProducedToday] = useState<Record<number, number>>({})
  const [progress, setProgress] = useState<StationProgressDto[]>([])
  const [lineProgress, setLineProgress] = useState<LineProgressDto[]>([])
  const [selectedJob, setSelectedJob] = useState<JobDetailDto | null>(null)
  const [pinningJob, setPinningJob] = useState<JobDetailDto | null>(null)
  const [editingJob, setEditingJob] = useState<JobDetailDto | null>(null)
  const [onlyBehindSchedule, setOnlyBehindSchedule] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

  async function refresh() {
    const [jobList, scheduleItems, stationList, recentLogs, produced, running, linesNow] = await Promise.all([
      jobsApi.getAll(),
      scheduleApi.getCurrent(),
      stationsApi.getAll(),
      logsApi.getMaintenanceLogs(50),
      reportsApi.production(localIsoDate(new Date()), localIsoDate(new Date())),
      scheduleApi.stationProgress(),
      scheduleApi.lineProgress(),
    ])
    setProgress(running)
    setLineProgress(linesNow)
    setJobs(jobList)
    setItems(scheduleItems)
    setStations(stationList.filter((s) => s.isActive))
    setLogs(recentLogs)
    setProducedToday(Object.fromEntries(produced.map((r) => [r.stationId, r.eqa])))
  }

  // After a change made here: reload, and let the rest of the shell (the sidebar's late count) follow.
  async function reloadAfterChange() {
    signalJobsChanged()
    await refresh().catch((e) => setNotice(errorText(e, 'Could not refresh the dashboard.')))
  }

  useEffect(() => {
    const load = () => refresh().catch((e) => setNotice(errorText(e, 'Could not load the dashboard.')))
    load()
    return startAutoRefresh(load)
  }, [])

  const runningCount = jobs.filter((j) => j.status === 'in-production').length
  const onTimeCount = jobs.filter((j) => !j.isLate && j.status !== 'completed').length
  const creepAlerts = items.filter((i) => i.creepDetected && !i.creepApprovedAt)
  // "At risk": tight margin between the schedule's own estimated completion and the booked delivery
  // date (JobStatusService.IsAtRisk — configurable via Settings -> AtRiskBufferMinutes).
  const atRiskCount = jobs.filter((j) => j.isAtRisk).length
  // "Jobs late" here means production has slipped past its own scheduled completion — distinct from
  // isLate's customer-due-date sense (JobStatusService.IsBehindSchedule).
  const behindScheduleCount = jobs.filter((j) => j.isBehindSchedule).length

  const urgentJobs = jobs
    .filter((j) => (onlyBehindSchedule ? j.isBehindSchedule : j.isLate || j.isAtRisk || j.isBehindSchedule))
    .sort((a, b) => (a.dueDate ?? '').localeCompare(b.dueDate ?? ''))

  async function detailOf(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (!summary) return null
    try {
      return await jobsApi.getById(summary.id)
    } catch (e) {
      setNotice(errorText(e, `Could not load ${jobRef}.`))
      return null
    }
  }

  const openJob = async (jobRef: string) => setSelectedJob(await detailOf(jobRef))
  const pinJob = async (jobRef: string) => setPinningJob(await detailOf(jobRef))

  async function approveCreep(id: number, jobRef: string) {
    try {
      await scheduleApi.approveCreep(id)
    } catch (e) {
      setNotice(errorText(e, `Could not approve the creep on ${jobRef}.`))
    }
    await reloadAfterChange()
  }

  async function approveForCutting(jobRef: string) {
    setNotice(null)
    try {
      await approveForCuttingWithConfirm(jobRef)
    } catch (e) {
      setNotice(errorText(e, `Could not approve ${jobRef} for cutting.`))
    }
    await reloadAfterChange()
  }

  async function saveEdit(request: UpdateJobRequest) {
    if (!editingJob) return
    await jobsApi.update(editingJob.id, request)
    await reloadAfterChange()
  }

  const canPin = (job: { status: JobSummaryDto['status']; componentType: string | null; isFrozen: boolean }) =>
    !!user?.canReschedule && isMovableStatus(job.status) && !!job.componentType && !job.isFrozen

  return (
    <div>
      <div style={{ display: 'flex', gap: 16 }}>
        <div style={{ flex: 1 }}>
          <h2 style={{ marginTop: 0 }}>Dashboard</h2>
          {notice && (
            <div className="card" style={{ marginBottom: 12, display: 'flex', justifyContent: 'space-between', gap: 12, fontSize: 13, color: '#dc2626' }}>
              <span>{notice}</span>
              <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => setNotice(null)}>
                Dismiss
              </button>
            </div>
          )}

          <div style={{ display: 'flex', gap: 12, marginBottom: 20, flexWrap: 'wrap' }}>
            <Tile label="Jobs currently running" value={runningCount} />
            <Tile label="Jobs on time" value={onTimeCount} accent="#16a34a" />
            <Tile
              label="Jobs at risk"
              value={atRiskCount}
              accent="#f59e0b"
              title="Scheduled completion is within the at-risk buffer of the booked delivery date (Settings -> AtRiskBufferMinutes)"
            />
            <Tile
              label="Behind schedule"
              value={behindScheduleCount}
              accent="#ef4444"
              onClick={() => setOnlyBehindSchedule((v) => !v)}
              active={onlyBehindSchedule}
              title="Past the schedule's own estimated completion — production has slipped, which can push the due date too"
            />
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: 12, marginBottom: 24 }}>
            {stations.map((station) => {
              const stationItems = items.filter((i) => i.stationId === station.id)
              const totalEqAToday = producedToday[station.id] ?? 0
              const current = stationItems.find((i) => i.actualStart && !i.actualEnd)
              const running = current ? progress.find((p) => p.stationId === station.id && p.jobRef === current.jobRef) : undefined
              const next = stationItems
                .filter((i) => !i.actualStart)
                .sort((a, b) => a.plannedStart.localeCompare(b.plannedStart))[0]
              const hasLate = stationItems.some((i) => jobs.find((j) => j.jobRef === i.jobRef)?.isLate)
              // §7: made today vs the line's target (EqA/h × today's hours), flagged when today's hours are changed.
              const target = station.todayTargetEqa

              return (
                <div
                  key={station.id}
                  className="card"
                  style={{ border: hasLate ? '2px solid var(--status-late-border)' : undefined }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                    <span style={{ fontWeight: 700 }}>{station.name}</span>
                    {station.todayChange && (
                      <span
                        title={`Today: ${station.todayChange}`}
                        style={{
                          fontSize: 10,
                          fontWeight: 700,
                          padding: '1px 6px',
                          borderRadius: 999,
                          background: 'var(--badge-new-bg)',
                          color: 'var(--badge-new-text)',
                        }}
                      >
                        {station.todayChange.startsWith('Closed') ? 'CLOSED' : 'OVERTIME'}
                      </span>
                    )}
                  </div>
                  <div style={{ fontSize: 24, fontWeight: 700, color: 'var(--brand-primary)' }} data-testid={`station-eq-${station.id}`}>
                    {totalEqAToday.toFixed(1)}
                    <span style={{ fontSize: 14, color: 'var(--text-muted)', fontWeight: 600 }}> / {target ?? '—'}</span>
                    <span style={{ fontSize: 12, color: 'var(--text-faint)', fontWeight: 400 }}> Eq today</span>
                  </div>
                  <LineProgress progress={lineProgress.find((p) => p.stationId === station.id)} />
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 8 }}>
                    Current: {current ? `${current.jobRef} — ${current.componentType ?? ''}` : '—'}
                  </div>
                  {running && (
                    <div style={{ fontSize: 12, color: 'var(--text-muted)', paddingLeft: 10 }} data-testid={`station-member-${station.id}`}>
                      Member {running.currentMemberId ?? '—'}
                      {running.nextMemberId ? `, then ${running.nextMemberId}` : ''} ({running.membersDone}/{running.membersTotal} done)
                    </div>
                  )}
                  <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                    Next job: {next ? `${next.jobRef} — ${next.componentType ?? ''}` : '—'}
                  </div>
                </div>
              )
            })}
          </div>

          <h3>Urgent jobs</h3>
          <div className="card">
            <table>
              <thead>
                <tr>
                  <th>Job Reference</th>
                  <th>Structure Name</th>
                  <th>Station</th>
                  <th>Due Date</th>
                  <th>Status</th>
                  <th>Why</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {urgentJobs.map((j) => (
                  <tr key={j.jobRef}>
                    <td style={{ cursor: 'pointer' }} onClick={() => openJob(j.jobRef)}>
                      {j.jobRef}
                    </td>
                    <td style={{ cursor: 'pointer' }} onClick={() => openJob(j.jobRef)}>
                      {j.jobRef}
                    </td>
                    <td>{j.stationName ?? '—'}</td>
                    <td>{j.dueDate ?? '—'}</td>
                    <td>
                      <StatusPill status={j.status} isLate={j.isLate} />
                      {j.isTba && <TbaTag />}
                    </td>
                    <td style={{ fontSize: 12 }}>
                      {[j.isLate && 'Past due', j.isBehindSchedule && 'Behind schedule', j.isAtRisk && 'At risk'].filter(Boolean).join(' · ') || '—'}
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6 }}>
                        {user?.isAdmin && canApproveForCutting(j) && (
                          <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 11 }} onClick={() => approveForCutting(j.jobRef)}>
                            Approve
                          </button>
                        )}
                        {canPin(j) && (
                          <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 11 }} onClick={() => pinJob(j.jobRef)}>
                            Reschedule
                          </button>
                        )}
                        <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 11 }} onClick={() => openJob(j.jobRef)}>
                          View
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {urgentJobs.length === 0 && (
                  <tr>
                    <td colSpan={7} style={{ color: 'var(--text-faint)', textAlign: 'center' }}>
                      No urgent jobs
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>

        <CollapsibleSidePanel viewKey="dashboard" title="Details">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Line changes today</div>
              {stations
                .filter((s) => s.todayChange)
                .map((s) => (
                  <div key={s.id} style={{ fontSize: 12, marginBottom: 4 }}>
                    {s.name}: {s.todayChange}
                  </div>
                ))}
              {stations.every((s) => !s.todayChange) && <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>None today</div>}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Creep alerts</div>
              {creepAlerts.map((i) => (
                <div key={i.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                  <span style={{ fontSize: 12 }}>
                    {i.jobRef} <CreepBadge />
                  </span>
                  {user?.isAdmin && (
                    <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 11 }} onClick={() => approveCreep(i.id, i.jobRef)}>
                      Approve
                    </button>
                  )}
                </div>
              ))}
              {creepAlerts.length === 0 && <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>None</div>}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Needs attention</div>
              {logs
                .filter((l) => l.outcome !== 'success')
                .slice(0, 5)
                .map((l) => (
                  <div key={l.id} style={{ fontSize: 12, marginBottom: 8, paddingLeft: 8, borderLeft: `3px solid ${l.outcome === 'error' ? '#ef4444' : '#f59e0b'}` }}>
                    <div style={{ fontWeight: 600 }}>
                      {l.taskName}
                      {l.jobRef ? ` · ${l.jobRef}` : ''} — {l.outcome}
                    </div>
                    {l.message && <div style={{ color: '#475569' }}>{l.message}</div>}
                    <div style={{ color: 'var(--text-faint)' }}>{new Date(l.runAt).toLocaleString()}</div>
                  </div>
                ))}
              {logs.every((l) => l.outcome === 'success') && <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>Nothing needs attention</div>}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Recent maintenance</div>
              {logs.slice(0, 5).map((l) => (
                <div key={l.id} style={{ fontSize: 12, marginBottom: 6 }}>
                  <div style={{ fontWeight: 600 }}>
                    {l.taskName} — {l.outcome}
                  </div>
                  <div style={{ color: 'var(--text-faint)' }}>{new Date(l.runAt).toLocaleString()}</div>
                </div>
              ))}
              {logs.length === 0 && <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>None yet</div>}
            </div>
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
