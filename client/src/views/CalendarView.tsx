import { useEffect, useMemo, useState } from 'react'
import { scheduleApi } from '../api/schedule'
import { stationsApi } from '../api/stations'
import { jobsApi } from '../api/jobs'
import { settingsApi } from '../api/admin'
import { useAuth } from '../auth/AuthContext'
import type { JobDetailDto, JobSummaryDto, ScheduleItemDto, StationDto } from '../types/api'
import { CalendarBlock } from '../components/CalendarBlock'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'
import { AddStationModal } from '../components/AddStationModal'

const ROW_HEIGHT = 52
const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

function startOfWeek(date: Date) {
  const d = new Date(date)
  const day = d.getDay()
  const diff = day === 0 ? -6 : 1 - day // Monday-start week
  d.setDate(d.getDate() + diff)
  d.setHours(0, 0, 0, 0)
  return d
}

export function CalendarView() {
  const { user } = useAuth()
  const [mode, setMode] = useState<'day' | 'week'>('day')
  const [anchorDate, setAnchorDate] = useState(() => new Date())
  const [stations, setStations] = useState<StationDto[]>([])
  const [items, setItems] = useState<ScheduleItemDto[]>([])
  const [jobs, setJobs] = useState<JobSummaryDto[]>([])
  const [shiftStartHour, setShiftStartHour] = useState(6)
  const [shiftEndHour, setShiftEndHour] = useState(18)
  const [workingDays, setWorkingDays] = useState([1, 2, 3, 4, 5])
  const [selectedJob, setSelectedJob] = useState<JobDetailDto | null>(null)
  const [showAddStation, setShowAddStation] = useState(false)

  async function refresh() {
    const [stationList, scheduleItems, jobList, settings] = await Promise.all([
      stationsApi.getAll(),
      scheduleApi.getCurrent(),
      jobsApi.getAll(),
      settingsApi.getAll(),
    ])
    setStations(stationList.filter((s) => s.isActive).sort((a, b) => a.displayOrder - b.displayOrder))
    setItems(scheduleItems)
    setJobs(jobList)
    const findVal = (key: string, fallback: string) => settings.find((s) => s.key === key)?.value ?? fallback
    setShiftStartHour(Number(findVal('ShiftStartHour', '6')))
    setShiftEndHour(Number(findVal('ShiftEndHour', '18')))
    setWorkingDays(
      findVal('WorkingDays', '1,2,3,4,5')
        .split(',')
        .filter(Boolean)
        .map(Number),
    )
  }

  useEffect(() => {
    refresh().catch(() => {})
  }, [])

  const isLateFor = (jobRef: string) => jobs.find((j) => j.jobRef === jobRef)?.isLate ?? false

  async function openJob(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (summary) setSelectedJob(await jobsApi.getById(summary.id))
  }

  const hours = useMemo(
    () => Array.from({ length: shiftEndHour - shiftStartHour }, (_, i) => shiftStartHour + i),
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

  function itemsForStationDay(stationId: number, date: Date) {
    const iso = date.toISOString().slice(0, 10)
    return items.filter((i) => i.stationId === stationId && i.scheduleDate.slice(0, 10) === iso)
  }

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
              <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--brand-primary)' }}>
                <button
                  className="btn"
                  style={{
                    background: mode === 'day' ? 'var(--brand-primary)' : '#fff',
                    color: mode === 'day' ? '#fff' : 'var(--brand-primary)',
                    borderRadius: 0,
                  }}
                  onClick={() => setMode('day')}
                >
                  Day
                </button>
                <button
                  className="btn"
                  style={{
                    background: mode === 'week' ? 'var(--brand-primary)' : '#fff',
                    color: mode === 'week' ? '#fff' : 'var(--brand-primary)',
                    borderRadius: 0,
                  }}
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
                      <div
                        key={h}
                        style={{
                          height: ROW_HEIGHT,
                          width: 64,
                          fontSize: 11,
                          color: '#94a3b8',
                          padding: '4px 8px',
                          borderBottom: '1px solid #f1f5f9',
                        }}
                      >
                        {String(h).padStart(2, '0')}:00
                      </div>
                    ))
                  : weekDays.map((d) => (
                      <div
                        key={d.toISOString()}
                        style={{
                          height: 90,
                          width: 90,
                          fontSize: 11,
                          color: '#94a3b8',
                          padding: '4px 8px',
                          borderBottom: '1px solid #f1f5f9',
                        }}
                      >
                        {DAY_NAMES[d.getDay()]} {d.getDate()}
                      </div>
                    ))}
              </div>

              {stations.map((station) => (
                <div key={station.id} style={{ width: 220, borderRight: '1px solid #e2e8f0', flexShrink: 0 }}>
                  <div
                    style={{
                      height: 40,
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      fontWeight: 700,
                      fontSize: 13,
                      color: 'var(--brand-primary)',
                      borderBottom: '1px solid #e2e8f0',
                    }}
                  >
                    {station.name}
                  </div>

                  {mode === 'day' ? (
                    <div style={{ position: 'relative', height: hours.length * ROW_HEIGHT }}>
                      {hours.map((h) => (
                        <div key={h} style={{ position: 'absolute', top: (h - shiftStartHour) * ROW_HEIGHT, width: '100%', height: ROW_HEIGHT, borderBottom: '1px solid #f1f5f9' }} />
                      ))}
                      {itemsForStationDay(station.id, anchorDate).map((item) => {
                        const start = new Date(item.plannedStart)
                        const end = new Date(item.plannedEnd)
                        const startOffsetH = start.getHours() + start.getMinutes() / 60 - shiftStartHour
                        const durationH = Math.max((end.getTime() - start.getTime()) / 3600000, 0.5)
                        return (
                          <div
                            key={item.id}
                            style={{ position: 'absolute', top: startOffsetH * ROW_HEIGHT, left: 2, right: 2, height: durationH * ROW_HEIGHT - 2 }}
                          >
                            <CalendarBlock item={item} isLate={isLateFor(item.jobRef)} onDoubleClick={() => openJob(item.jobRef)} />
                          </div>
                        )
                      })}
                    </div>
                  ) : (
                    <div>
                      {weekDays.map((d) => (
                        <div key={d.toISOString()} style={{ height: 90, borderBottom: '1px solid #f1f5f9', padding: 2, overflow: 'auto' }}>
                          {itemsForStationDay(station.id, d).map((item) => (
                            <CalendarBlock key={item.id} item={item} isLate={isLateFor(item.jobRef)} onDoubleClick={() => openJob(item.jobRef)} compact />
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
              {jobs
                .filter((j) => j.status === 'ready' && !j.stationId)
                .map((j) => (
                  <div key={j.jobRef} style={{ fontSize: 12, marginBottom: 4, cursor: 'pointer' }} onClick={() => openJob(j.jobRef)}>
                    {j.jobRef}
                  </div>
                ))}
              {jobs.filter((j) => j.status === 'ready' && !j.stationId).length === 0 && (
                <div style={{ fontSize: 12, color: '#94a3b8' }}>None</div>
              )}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Creep alerts</div>
              {items
                .filter((i) => i.creepDetected && !i.creepApprovedAt)
                .map((i) => (
                  <div key={i.id} style={{ fontSize: 12, marginBottom: 4 }}>
                    {i.jobRef}
                  </div>
                ))}
              {items.filter((i) => i.creepDetected && !i.creepApprovedAt).length === 0 && (
                <div style={{ fontSize: 12, color: '#94a3b8' }}>None</div>
              )}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Station EqA today</div>
              {stations.map((s) => {
                const today = new Date().toISOString().slice(0, 10)
                const total = items
                  .filter((i) => i.stationId === s.id && i.scheduleDate.slice(0, 10) === today)
                  .reduce((sum, i) => sum + i.eqaCompleted, 0)
                return (
                  <div key={s.id} style={{ fontSize: 12, marginBottom: 4 }}>
                    {s.name}: {total.toFixed(1)} / {s.dailyEqATarget ?? '—'}
                  </div>
                )
              })}
            </div>
          </div>
        </CollapsibleSidePanel>
      </div>

      {selectedJob && <JobDetailPopup job={selectedJob} onClose={() => setSelectedJob(null)} />}
      {showAddStation && (
        <AddStationModal
          onClose={() => setShowAddStation(false)}
          onCreated={() => {
            setShowAddStation(false)
            refresh()
          }}
        />
      )}
    </div>
  )
}
