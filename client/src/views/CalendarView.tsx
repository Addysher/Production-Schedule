import { useEffect, useMemo, useState } from 'react'
import { scheduleApi } from '../api/schedule'
import { stationsApi } from '../api/stations'
import { jobsApi } from '../api/jobs'
import { reportsApi } from '../api/reports'
import { useAuth } from '../auth/AuthContext'
import type { CalendarSettingsDto, JobDetailDto, JobSummaryDto, ScheduleItemDto, ScheduleSegmentDto, StationDto } from '../types/api'
import { localIsoDate } from '../utils/dates'
import { CalendarBlock } from '../components/CalendarBlock'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'
import { StationModal } from '../components/StationModal'
import { ScheduleManuallyModal } from '../components/ScheduleManuallyModal'

const ROW_HEIGHT = 52
const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const DEFAULT_CALENDAR: CalendarSettingsDto = { shiftStartHour: 6, shiftEndHour: 18, workingDays: [1, 2, 3, 4, 5], breaks: [] }

const hoursOf = (hhmm: string) => {
  const [h, m] = hhmm.split(':').map(Number)
  return h + m / 60
}

function startOfWeek(date: Date) {
  const d = new Date(date)
  const day = d.getDay()
  const diff = day === 0 ? -6 : 1 - day // Monday-start week
  d.setDate(d.getDate() + diff)
  d.setHours(0, 0, 0, 0)
  return d
}

interface Block {
  item: ScheduleItemDto
  segment: ScheduleSegmentDto
}

export function CalendarView() {
  const { user } = useAuth()
  const [mode, setMode] = useState<'day' | 'week'>('day')
  const [anchorDate, setAnchorDate] = useState(() => new Date())
  const [stations, setStations] = useState<StationDto[]>([])
  const [items, setItems] = useState<ScheduleItemDto[]>([])
  const [jobs, setJobs] = useState<JobSummaryDto[]>([])
  const [calendar, setCalendar] = useState<CalendarSettingsDto>(DEFAULT_CALENDAR)
  const [producedToday, setProducedToday] = useState<Record<number, number>>({})
  const [selectedJob, setSelectedJob] = useState<JobDetailDto | null>(null)
  const [pinning, setPinning] = useState<JobDetailDto | null>(null)
  const [showAddStation, setShowAddStation] = useState(false)

  async function refresh() {
    const today = localIsoDate(new Date())
    const [stationList, scheduleItems, jobList, calendarSettings, produced] = await Promise.all([
      stationsApi.getAll(),
      scheduleApi.getCurrent(),
      jobsApi.getAll(),
      scheduleApi.getCalendarSettings(),
      reportsApi.production(today, today),
    ])
    setStations(stationList.filter((s) => s.isActive).sort((a, b) => a.displayOrder - b.displayOrder))
    setItems(scheduleItems)
    setJobs(jobList)
    setCalendar(calendarSettings)
    setProducedToday(Object.fromEntries(produced.map((r) => [r.stationId, r.eqa])))
  }

  useEffect(() => {
    refresh().catch(() => {})
  }, [])

  const { shiftStartHour, shiftEndHour, workingDays } = calendar
  const isLateFor = (jobRef: string) => jobs.find((j) => j.jobRef === jobRef)?.isLate ?? false

  async function openJob(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (summary) setSelectedJob(await jobsApi.getById(summary.id))
  }

  const hours = useMemo(
    () => Array.from({ length: Math.max(shiftEndHour - shiftStartHour, 0) }, (_, i) => shiftStartHour + i),
    [shiftStartHour, shiftEndHour],
  )

  const weekDays = useMemo(() => {
    const monday = startOfWeek(anchorDate)
    return Array.from({ length: 7 }, (_, i) => {
      const d = new Date(monday)
      d.setDate(d.getDate() + i)
      return d
    }).filter((d) => workingDays.includes(d.getDay()))
  }, [anchorDate, workingDays])

  // One block per job per day it has work planned — a job spread over three days shows on all three.
  const blocks = useMemo<Block[]>(() => items.flatMap((item) => item.segments.map((segment) => ({ item, segment }))), [items])

  function blocksForStationDay(stationId: number, date: Date): Block[] {
    const iso = localIsoDate(date)
    return blocks.filter((b) => b.item.stationId === stationId && b.segment.date.slice(0, 10) === iso)
  }

  const queuedUnscheduled = jobs.filter((j) => j.status === 'ready' && !j.stationId)
  const creepAlerts = items.filter((i) => i.creepDetected && !i.creepApprovedAt)
  const canPin = (job: JobDetailDto) => !!user?.isAdmin && (job.status === 'ready' || job.status === 'approved') && !!job.componentType

  return (
    <div>
      <div style={{ display: 'flex', gap: 16 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
            <h2 style={{ margin: 0 }}>Calendar</h2>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <button
                className="btn btn-secondary"
                onClick={() => {
                  const d = new Date(anchorDate)
                  d.setDate(d.getDate() + (mode === 'day' ? -1 : -7))
                  setAnchorDate(d)
                }}
              >
                &larr;
              </button>
              <span style={{ fontSize: 13, fontWeight: 600, minWidth: 120, textAlign: 'center' }}>
                {mode === 'day' ? anchorDate.toDateString() : `Week of ${startOfWeek(anchorDate).toDateString()}`}
              </span>
              <button
                className="btn btn-secondary"
                onClick={() => {
                  const d = new Date(anchorDate)
                  d.setDate(d.getDate() + (mode === 'day' ? 1 : 7))
                  setAnchorDate(d)
                }}
              >
                &rarr;
              </button>
              <button className="btn btn-secondary" onClick={() => setAnchorDate(new Date())}>
                Today
              </button>
              <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--brand-primary)' }}>
                <button
                  className="btn"
                  style={{ background: mode === 'day' ? 'var(--brand-primary)' : '#fff', color: mode === 'day' ? '#fff' : 'var(--brand-primary)', borderRadius: 0 }}
                  onClick={() => setMode('day')}
                >
                  Day
                </button>
                <button
                  className="btn"
                  style={{ background: mode === 'week' ? 'var(--brand-primary)' : '#fff', color: mode === 'week' ? '#fff' : 'var(--brand-primary)', borderRadius: 0 }}
                  onClick={() => setMode('week')}
                >
                  Week
                </button>
              </div>
            </div>
          </div>

          <div className="card" style={{ overflowX: 'auto', padding: 0 }}>
            <div style={{ display: 'flex', minWidth: 'max-content' }}>
              {/* Sticky label column */}
              <div style={{ position: 'sticky', left: 0, background: '#fff', zIndex: 2, borderRight: '1px solid #e2e8f0' }}>
                <div style={{ height: 40, borderBottom: '1px solid #e2e8f0' }} />
                {mode === 'day'
                  ? hours.map((h) => (
                      <div key={h} style={{ height: ROW_HEIGHT, width: 64, fontSize: 11, color: '#94a3b8', padding: '4px 8px', borderBottom: '1px solid #f1f5f9' }}>
                        {String(h).padStart(2, '0')}:00
                      </div>
                    ))
                  : weekDays.map((d) => (
                      <div key={d.toISOString()} style={{ height: 90, width: 90, fontSize: 11, color: '#94a3b8', padding: '4px 8px', borderBottom: '1px solid #f1f5f9' }}>
                        {DAY_NAMES[d.getDay()]} {d.getDate()}
                      </div>
                    ))}
              </div>

              {stations.map((station) => (
                <div key={station.id} style={{ width: 220, borderRight: '1px solid #e2e8f0', flexShrink: 0 }}>
                  <div
                    style={{ height: 40, display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 700, fontSize: 13, color: 'var(--brand-primary)', borderBottom: '1px solid #e2e8f0' }}
                  >
                    {station.name}
                  </div>

                  {mode === 'day' ? (
                    <div style={{ position: 'relative', height: hours.length * ROW_HEIGHT }}>
                      {hours.map((h) => (
                        <div key={h} style={{ position: 'absolute', top: (h - shiftStartHour) * ROW_HEIGHT, width: '100%', height: ROW_HEIGHT, borderBottom: '1px solid #f1f5f9' }} />
                      ))}
                      {calendar.breaks.map((b) => {
                        const top = (hoursOf(b.start) - shiftStartHour) * ROW_HEIGHT
                        const height = (hoursOf(b.end) - hoursOf(b.start)) * ROW_HEIGHT
                        return (
                          <div
                            key={b.start}
                            title={`Break ${b.start}–${b.end}`}
                            style={{
                              position: 'absolute',
                              top,
                              height,
                              left: 0,
                              right: 0,
                              background: 'repeating-linear-gradient(45deg, #f1f5f9, #f1f5f9 6px, #e2e8f0 6px, #e2e8f0 12px)',
                              fontSize: 10,
                              color: '#94a3b8',
                              padding: '1px 6px',
                              zIndex: 0,
                            }}
                          >
                            Break
                          </div>
                        )
                      })}
                      {blocksForStationDay(station.id, anchorDate).map(({ item, segment }, idx) => {
                        const start = new Date(segment.start)
                        const end = new Date(segment.end)
                        const startOffsetH = start.getHours() + start.getMinutes() / 60 - shiftStartHour
                        const durationH = Math.max((end.getTime() - start.getTime()) / 3600000, 0.5)
                        return (
                          <div
                            key={`${item.id}-${idx}`}
                            style={{ position: 'absolute', top: startOffsetH * ROW_HEIGHT, left: 2, right: 2, height: durationH * ROW_HEIGHT - 2, zIndex: 1 }}
                          >
                            <CalendarBlock item={item} segment={segment} isLate={isLateFor(item.jobRef)} onDoubleClick={() => openJob(item.jobRef)} />
                          </div>
                        )
                      })}
                    </div>
                  ) : (
                    <div>
                      {weekDays.map((d) => (
                        <div key={d.toISOString()} style={{ height: 90, borderBottom: '1px solid #f1f5f9', padding: 2, overflow: 'auto' }}>
                          {blocksForStationDay(station.id, d).map(({ item, segment }, idx) => (
                            <CalendarBlock key={`${item.id}-${idx}`} item={item} segment={segment} isLate={isLateFor(item.jobRef)} onDoubleClick={() => openJob(item.jobRef)} compact />
                          ))}
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              ))}

              {user?.isAdmin && (
                <div style={{ width: 100, display: 'flex', alignItems: 'flex-start', justifyContent: 'center', paddingTop: 8 }}>
                  <button className="btn btn-secondary" onClick={() => setShowAddStation(true)}>
                    + Station
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>

        <CollapsibleSidePanel viewKey="calendar" title="Details">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Unscheduled ready jobs</div>
              {queuedUnscheduled.map((j) => (
                <div key={j.jobRef} style={{ fontSize: 12, marginBottom: 4, cursor: 'pointer' }} onClick={() => openJob(j.jobRef)}>
                  {j.jobRef}
                </div>
              ))}
              {queuedUnscheduled.length === 0 && <div style={{ fontSize: 12, color: '#94a3b8' }}>None</div>}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Creep alerts</div>
              {creepAlerts.map((i) => (
                <div key={i.id} style={{ fontSize: 12, marginBottom: 4 }}>
                  {i.jobRef}
                </div>
              ))}
              {creepAlerts.length === 0 && <div style={{ fontSize: 12, color: '#94a3b8' }}>None</div>}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Station EqA today</div>
              {stations.map((s) => {
                const done = producedToday[s.id] ?? 0
                const pct = s.dailyEqATarget ? Math.min((done / s.dailyEqATarget) * 100, 100) : 0
                return (
                  <div key={s.id} style={{ fontSize: 12, marginBottom: 8 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                      <span>{s.name}</span>
                      <span>
                        {done.toFixed(1)} / {s.dailyEqATarget ?? '—'}
                      </span>
                    </div>
                    {s.dailyEqATarget ? (
                      <div style={{ height: 6, background: '#e2e8f0', borderRadius: 3, marginTop: 3 }}>
                        <div style={{ height: 6, width: `${pct}%`, background: 'var(--brand-primary)', borderRadius: 3 }} />
                      </div>
                    ) : null}
                  </div>
                )
              })}
            </div>
          </div>
        </CollapsibleSidePanel>
      </div>

      {selectedJob && (
        <JobDetailPopup
          job={selectedJob}
          onClose={() => setSelectedJob(null)}
          onScheduleManually={canPin(selectedJob) ? () => { setPinning(selectedJob); setSelectedJob(null) } : undefined}
        />
      )}
      {pinning && (
        <ScheduleManuallyModal
          job={pinning}
          onClose={() => setPinning(null)}
          onDone={() => {
            setPinning(null)
            refresh()
          }}
        />
      )}
      {showAddStation && (
        <StationModal
          onClose={() => setShowAddStation(false)}
          onSaved={() => {
            setShowAddStation(false)
            refresh()
          }}
        />
      )}
    </div>
  )
}
