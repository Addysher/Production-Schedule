import { localIsoDate } from '../utils/dates'
import { useEffect, useState } from 'react'
import { jobsApi } from '../api/jobs'
import { scheduleApi } from '../api/schedule'
import { stationsApi } from '../api/stations'
import { logsApi } from '../api/logs'
import { reportsApi } from '../api/reports'
import { useAuth } from '../auth/AuthContext'
import type { JobSummaryDto, JobDetailDto, ScheduleItemDto, StationDto, CapacityOverrideDto } from '../types/api'
import type { MaintenanceLogDto } from '../types/logs'
import { StatusPill } from '../components/StatusPill'
import { CreepBadge } from '../components/CreepBadge'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'
import { ScheduleManuallyModal } from '../components/ScheduleManuallyModal'

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
  const [overridesByStation, setOverridesByStation] = useState<Record<number, CapacityOverrideDto[]>>({})
  const [logs, setLogs] = useState<MaintenanceLogDto[]>([])
  const [producedToday, setProducedToday] = useState<Record<number, number>>({})
  const [selectedJob, setSelectedJob] = useState<JobDetailDto | null>(null)
  const [pinningJob, setPinningJob] = useState<JobDetailDto | null>(null)
  const [onlyBehindSchedule, setOnlyBehindSchedule] = useState(false)

  async function refresh() {
    const [jobList, scheduleItems, stationList, recentLogs, produced] = await Promise.all([
      jobsApi.getAll(),
      scheduleApi.getCurrent(),
      stationsApi.getAll(),
      logsApi.getMaintenanceLogs(50),
      reportsApi.production(localIsoDate(new Date()), localIsoDate(new Date())),
    ])
    setJobs(jobList)
    setItems(scheduleItems)
    setStations(stationList.filter((s) => s.isActive))
    setLogs(recentLogs)
    setProducedToday(Object.fromEntries(produced.map((r) => [r.stationId, r.eqa])))

    const today = localIsoDate(new Date())
    const overrideEntries = await Promise.all(
      stationList
        .filter((s) => s.isActive)
        .map(async (s) => [s.id, (await stationsApi.getCapacityOverrides(s.id)).filter((o) => o.overrideDate.slice(0, 10) === today)] as const),
    )
    setOverridesByStation(Object.fromEntries(overrideEntries))
  }

  useEffect(() => {
    refresh().catch(() => {})
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

  async function openJob(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (!summary) return
    setSelectedJob(await jobsApi.getById(summary.id))
  }

  async function approveCreep(id: number) {
    await scheduleApi.approveCreep(id)
    await refresh()
  }

  async function approveForCutting(jobRef: string) {
    await jobsApi.approveForCutting(jobRef)
    await refresh()
  }

  async function pinJob(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (!summary) return
    setPinningJob(await jobsApi.getById(summary.id))
  }

  return (
    <div>
      <div style={{ display: 'flex', gap: 16 }}>
        <div style={{ flex: 1 }}>
          <h2 style={{ marginTop: 0 }}>Dashboard</h2>

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
              label="Jobs late"
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
              const next = stationItems
                .filter((i) => !i.actualStart)
                .sort((a, b) => a.plannedStart.localeCompare(b.plannedStart))[0]
              const hasLate = stationItems.some((i) => jobs.find((j) => j.jobRef === i.jobRef)?.isLate)
              // §7: actual vs target, with a capacity-override indicator when one applies today.
              const todayOverride = overridesByStation[station.id]?.[0]
              const target = todayOverride?.eqATarget ?? station.dailyEqATarget

              return (
                <div
                  key={station.id}
                  className="card"
                  style={{ border: hasLate ? '2px solid var(--status-late-border)' : undefined }}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                    <span style={{ fontWeight: 700 }}>{station.name}</span>
                    {todayOverride && (
                      <span
                        title={`Capacity override for today: ${todayOverride.eqATarget} Eq (normally ${station.dailyEqATarget ?? 'unset'})`}
                        style={{
                          fontSize: 10,
                          fontWeight: 700,
                          padding: '1px 6px',
                          borderRadius: 999,
                          background: 'var(--badge-new-bg)',
                          color: 'var(--badge-new-text)',
                        }}
                      >
                        OVERRIDE
                      </span>
                    )}
                  </div>
                  <div style={{ fontSize: 24, fontWeight: 700, color: 'var(--brand-primary)' }} data-testid={`station-eq-${station.id}`}>
                    {totalEqAToday.toFixed(1)}
                    <span style={{ fontSize: 14, color: 'var(--text-muted)', fontWeight: 600 }}> / {target ?? '—'}</span>
                    <span style={{ fontSize: 12, color: 'var(--text-faint)', fontWeight: 400 }}> Eq today</span>
                  </div>
                  <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 8 }}>
                    Current: {current ? `${current.jobRef} — ${current.componentType ?? ''}` : '—'}
                  </div>
                  <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
                    Next: {next ? `${next.jobRef} — ${next.componentType ?? ''}` : '—'}
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
                    </td>
                    <td style={{ fontSize: 12 }}>
                      {[j.isLate && 'Past due', j.isBehindSchedule && 'Behind schedule', j.isAtRisk && 'At risk'].filter(Boolean).join(' · ') || '—'}
                    </td>
                    <td>
                      <div style={{ display: 'flex', gap: 6 }}>
                        {user?.isAdmin && j.status === 'ready' && (
                          <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 11 }} onClick={() => approveForCutting(j.jobRef)}>
                            Approve
                          </button>
                        )}
                        {user?.isAdmin && (j.status === 'ready' || j.status === 'approved') && !!j.componentType && (
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
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Active capacity overrides</div>
              {Object.entries(overridesByStation).flatMap(([stationId, overrides]) =>
                overrides.map((o) => {
                  const station = stations.find((s) => s.id === Number(stationId))
                  return (
                    <div key={o.id} style={{ fontSize: 12, marginBottom: 4 }}>
                      {station?.name}: {o.eqATarget} Eq
                    </div>
                  )
                }),
              )}
              {Object.values(overridesByStation).every((o) => o.length === 0) && (
                <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>None today</div>
              )}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Creep alerts</div>
              {creepAlerts.map((i) => (
                <div key={i.id} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
                  <span style={{ fontSize: 12 }}>
                    {i.jobRef} <CreepBadge />
                  </span>
                  {user?.isAdmin && (
                    <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 11 }} onClick={() => approveCreep(i.id)}>
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

      {selectedJob && <JobDetailPopup job={selectedJob} onClose={() => setSelectedJob(null)} />}
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
