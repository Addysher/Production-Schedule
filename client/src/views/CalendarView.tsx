import { useEffect, useMemo, useRef, useState, type DragEvent } from 'react'
import { scheduleApi } from '../api/schedule'
import { stationsApi } from '../api/stations'
import { jobsApi } from '../api/jobs'
import { reportsApi } from '../api/reports'
import { useAuth } from '../auth/AuthContext'
import { isMovableStatus } from '../auth/permissions'
import type { CapacityOverrideDto, CalendarSettingsDto, JobDetailDto, JobSummaryDto, ScheduleItemDto, ScheduleSegmentDto, StationDto, UpdateJobRequest } from '../types/api'
import { localIsoDate } from '../utils/dates'
import { CalendarBlock, LockIcon } from '../components/CalendarBlock'
import { JobDetailPopup } from '../components/JobDetailPopup'
import { CollapsibleSidePanel } from '../components/CollapsibleSidePanel'
import { StationModal } from '../components/StationModal'
import { ScheduleManuallyModal } from '../components/ScheduleManuallyModal'
import { JobEditModal } from '../components/JobEditModal'
import { DayChangeModal, describeDayChange } from '../components/DayChangeModal'
import { errorText } from '../components/Modal'
import { approveForCuttingWithConfirm, canApproveForCutting } from '../components/sawFiles'
import { signalJobsChanged, startAutoRefresh } from '../utils/appEvents'

const ROW_HEIGHT = 52
const DAY_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'] // indexed by Date.getDay()
const MONTH_HEADINGS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'] // the month grid starts on Monday
const DEFAULT_CALENDAR: CalendarSettingsDto = { shiftStartHour: 6, shiftEndHour: 18, workingDays: [1, 2, 3, 4, 5], breaks: [], freezeUntil: null, dayStart: '06:00', dayEnd: '18:00' }

// Hatching for days inside the freeze window.
const FROZEN_BG = 'repeating-linear-gradient(135deg, transparent, transparent 7px, var(--divider-faint) 7px, var(--divider-faint) 9px)'
// A line closed for the day (shutdown / public holiday): greyed out.
const CLOSED_BG = 'repeating-linear-gradient(45deg, color-mix(in srgb, #ef4444 6%, transparent), color-mix(in srgb, #ef4444 6%, transparent) 8px, transparent 8px, transparent 16px)'
const NOW_LINE_REFRESH_MS = 15 * 60 * 1000 // §request: the "now" line updates on a 15-minute interval

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

function startOfCalendarMonth(date: Date) {
  const first = new Date(date.getFullYear(), date.getMonth(), 1)
  return startOfWeek(first)
}

function sameDay(a: Date, b: Date) {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}

interface Block {
  item: ScheduleItemDto
  segment: ScheduleSegmentDto
}

// Dragging a block onto a different station/day calls the same schedule-manually endpoint the
// "Schedule manually" modal uses, so the server's own component-type check is still the authority —
// the client only pre-checks it for a quicker, clearer error than a round trip would give.
interface DragPayload {
  jobRef: string
  componentType: string | null
  stationId: number
}

export function CalendarView() {
  const { user } = useAuth()
  const [mode, setMode] = useState<'day' | 'week' | 'month'>('day')
  // Week view normally shows working days only; weekends can be shown to plan overtime on them.
  const [showWeekends, setShowWeekends] = useState(() => {
    try {
      return localStorage.getItem('calendar.showWeekends') === 'true'
    } catch {
      return false
    }
  })
  const [anchorDate, setAnchorDate] = useState(() => new Date())
  const [stations, setStations] = useState<StationDto[]>([])
  const [items, setItems] = useState<ScheduleItemDto[]>([])
  const [jobs, setJobs] = useState<JobSummaryDto[]>([])
  const [calendar, setCalendar] = useState<CalendarSettingsDto>(DEFAULT_CALENDAR)
  const [producedToday, setProducedToday] = useState<Record<number, number>>({})
  const [selectedJob, setSelectedJob] = useState<JobDetailDto | null>(null)
  const [pinning, setPinning] = useState<JobDetailDto | null>(null)
  const [editing, setEditing] = useState<JobDetailDto | null>(null)
  // One-off changes to a line's day (overtime / closed), set right here on the Calendar (v3.0).
  const [dayChanges, setDayChanges] = useState<CapacityOverrideDto[]>([])
  const [editingDay, setEditingDay] = useState<{ station: StationDto; date: Date } | null>(null)
  const [showAddStation, setShowAddStation] = useState(false)
  const [recalculating, setRecalculating] = useState(false)
  const [dragError, setDragError] = useState<string | null>(null)
  const [dropTarget, setDropTarget] = useState<string | null>(null)
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), NOW_LINE_REFRESH_MS)
    return () => clearInterval(timer)
  }, [])

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
    await loadDayChanges()
  }

  // Day changes around the date being viewed; only the newest request's answer is kept.
  const anchorRef = useRef(anchorDate)
  anchorRef.current = anchorDate
  const dayChangesRequest = useRef(0)
  async function loadDayChanges() {
    const a = anchorRef.current
    const from = new Date(a.getFullYear(), a.getMonth(), a.getDate() - 45)
    const to = new Date(a.getFullYear(), a.getMonth(), a.getDate() + 60)
    const request = ++dayChangesRequest.current
    const changes = await stationsApi.getDayChanges(localIsoDate(from), localIsoDate(to))
    if (request === dayChangesRequest.current) setDayChanges(changes)
  }

  useEffect(() => {
    loadDayChanges().catch((e) => setDragError(errorText(e, 'Could not load the line day changes.')))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [anchorDate])

  const dayChangeFor = (stationId: number, d: Date) => dayChanges.find((c) => c.stationId === stationId && c.overrideDate.slice(0, 10) === localIsoDate(d))
  const canEditDays = !!user?.canReschedule // Admin and Supervisor

  // The chip on a line's day: what's changed, and (for Admin/Supervisor) the way in to change it.
  // (A render helper rather than a component, so chips aren't remounted on every Calendar render.)
  function dayChip(station: StationDto, date: Date) {
    const change = dayChangeFor(station.id, date)
    if (!change && !canEditDays) return null
    return (
      <button
        type="button"
        onClick={() => canEditDays && setEditingDay({ station, date })}
        title={change ? describeDayChange(change) : 'Set overtime or close this line for the day'}
        style={{
          display: 'block',
          width: '100%',
          textAlign: 'left',
          border: change ? `1px solid ${change.isClosed ? '#ef4444' : '#f59e0b'}` : '1px dashed var(--border-subtle)',
          background: change ? (change.isClosed ? 'color-mix(in srgb, #ef4444 12%, transparent)' : 'color-mix(in srgb, #f59e0b 14%, transparent)') : 'transparent',
          color: change ? 'var(--text-primary)' : 'var(--text-faint)',
          borderRadius: 4,
          fontSize: 10,
          fontWeight: change ? 700 : 400,
          padding: '1px 5px',
          marginBottom: 3,
          cursor: canEditDays ? 'pointer' : 'default',
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {change ? describeDayChange(change) : '+ day change'}
      </button>
    )
  }

  // After a change made here: reload, and let the sidebar's late count follow.
  async function reloadAfterChange() {
    signalJobsChanged()
    await refresh().catch((e) => setDragError(errorText(e, 'Could not refresh the calendar.')))
  }

  useEffect(() => {
    const load = () => refresh().catch((e) => setDragError(errorText(e, 'Could not load the calendar.')))
    load()
    return startAutoRefresh(load)
  }, [])

  const headerHeight = mode === 'day' ? 62 : 40
  const { workingDays } = calendar
  // The hour grid covers the normal day, widened to any work planned outside it (a line's overtime).
  const segmentHours = items.flatMap((i) =>
    i.segments.map((s) => {
      const start = new Date(s.start)
      const end = new Date(s.end)
      const endHour = end.getHours() + end.getMinutes() / 60
      return { from: start.getHours(), to: Math.ceil(endHour === 0 && end > start ? 24 : endHour) }
    }),
  )
  const shiftStartHour = Math.min(calendar.shiftStartHour, ...segmentHours.map((h) => h.from))
  const shiftEndHour = Math.max(calendar.shiftEndHour, ...segmentHours.map((h) => h.to))
  // Days with planned work, so a normally non-working day opened by overtime still shows in Week view.
  const daysWithWork = new Set(items.flatMap((i) => i.segments.map((s) => s.date.slice(0, 10))))
  const isLateFor = (jobRef: string) => jobs.find((j) => j.jobRef === jobRef)?.isLate ?? false
  const isAtRiskFor = (jobRef: string) => jobs.find((j) => j.jobRef === jobRef)?.isAtRisk ?? false

  async function openJob(jobRef: string) {
    const summary = jobs.find((j) => j.jobRef === jobRef)
    if (!summary) return
    try {
      setSelectedJob(await jobsApi.getById(summary.id))
    } catch (e) {
      setDragError(errorText(e, `Could not load ${jobRef}.`))
    }
  }

  async function saveEdit(request: UpdateJobRequest) {
    if (!editing) return
    await jobsApi.update(editing.id, request)
    await reloadAfterChange()
  }

  // Months differ in length: stepping from the 31st must land in the next month, not skip it.
  function step(direction: 1 | -1) {
    const a = anchorDate
    if (mode === 'month') setAnchorDate(new Date(a.getFullYear(), a.getMonth() + direction, 1))
    else setAnchorDate(new Date(a.getFullYear(), a.getMonth(), a.getDate() + direction * (mode === 'day' ? 1 : 7)))
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
    }).filter((d) => showWeekends || workingDays.includes(d.getDay()) || daysWithWork.has(localIsoDate(d)) || dayChanges.some((c) => c.overrideDate.slice(0, 10) === localIsoDate(d)))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [anchorDate, workingDays, items, showWeekends, dayChanges])

  // Always the full Mon-Sun grid, padded into neighbouring months — a traditional month calendar,
  // not filtered to working days, so non-working days still show as (empty) cells in context.
  const monthDays = useMemo(() => {
    const start = startOfCalendarMonth(anchorDate)
    return Array.from({ length: 42 }, (_, i) => {
      const d = new Date(start)
      d.setDate(d.getDate() + i)
      return d
    })
  }, [anchorDate])

  // One block per job per day it has work planned — a job spread over three days shows on all three.
  const blocks = useMemo<Block[]>(() => items.flatMap((item) => item.segments.map((segment) => ({ item, segment }))), [items])

  function blocksForStationDay(stationId: number, date: Date): Block[] {
    const iso = localIsoDate(date)
    return blocks.filter((b) => b.item.stationId === stationId && b.segment.date.slice(0, 10) === iso)
  }

  function blocksForDay(date: Date): Block[] {
    const iso = localIsoDate(date)
    const seen = new Set<string>() // one chip per job per day, even if it spans several stations/segments
    return blocks.filter((b) => {
      if (b.segment.date.slice(0, 10) !== iso) return false
      if (seen.has(b.item.jobRef)) return false
      seen.add(b.item.jobRef)
      return true
    })
  }

  const queuedUnscheduled = jobs.filter((j) => j.status === 'ready' && !j.stationId)
  const creepAlerts = items.filter((i) => i.creepDetected && !i.creepApprovedAt)
  const canPin = (job: JobDetailDto) => !!user?.canReschedule && isMovableStatus(job.status) && !!job.componentType && !job.isFrozen
  const canDrag = (item: ScheduleItemDto) => !!user?.canReschedule && isMovableStatus(item.jobStatus) && !item.isFrozen

  // Freeze window: today through the end of the window can't take moves in or out.
  const freezeUntil = calendar.freezeUntil ? new Date(calendar.freezeUntil) : null
  const lastFrozenDay = freezeUntil ? new Date(freezeUntil.getTime() - 86400000) : null
  const isFrozenDay = (d: Date) => {
    if (!freezeUntil) return false
    const dayStart = new Date(d.getFullYear(), d.getMonth(), d.getDate())
    const todayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate())
    return dayStart >= todayStart && dayStart < freezeUntil
  }

  async function recalculate() {
    setRecalculating(true)
    try {
      const result = await scheduleApi.generate()
      await reloadAfterChange()
      window.alert(
        `Recalculated: ${result.jobsScheduled} job(s) scheduled` +
          (result.jobsFrozen ? `, ${result.jobsFrozen} frozen in place` : '') +
          (result.jobsPencilled ? `, ${result.jobsPencilled} pencilled in (TBA)` : '') +
          (result.jobsSkipped ? `, ${result.jobsSkipped} skipped (no matching station/capacity)` : '') +
          '.',
      )
    } catch (e) {
      setDragError(errorText(e, 'Recalculate failed.'))
    } finally {
      setRecalculating(false)
    }
  }

  function onBlockDragStart(e: DragEvent, item: ScheduleItemDto) {
    const payload: DragPayload = { jobRef: item.jobRef, componentType: item.componentType, stationId: item.stationId }
    e.dataTransfer.setData('application/json', JSON.stringify(payload))
    e.dataTransfer.effectAllowed = 'move'
  }

  // Highlights the cell under the pointer while a job is being dragged.
  const dropZone = (key: string) => ({
    onDragOver: (e: DragEvent) => {
      e.preventDefault()
      setDropTarget(key)
    },
    style: dropTarget === key ? { outline: '2px dashed var(--brand-primary)', outlineOffset: -2 } : undefined,
  })

  function onDropOnDay(e: DragEvent, date: Date) {
    e.preventDefault()
    setDropTarget(null)
    const raw = e.dataTransfer.getData('application/json')
    if (!raw) return
    const payload = JSON.parse(raw) as DragPayload
    const station = stations.find((s) => s.id === payload.stationId)
    if (station) void moveJob(payload, station, date)
  }

  async function onDropOnStationDay(e: DragEvent, station: StationDto, date: Date) {
    e.preventDefault()
    setDropTarget(null)
    const raw = e.dataTransfer.getData('application/json')
    if (!raw) return
    await moveJob(JSON.parse(raw) as DragPayload, station, date)
  }

  async function moveJob(payload: DragPayload, station: StationDto, date: Date) {
    if (freezeUntil && lastFrozenDay && new Date(date.getFullYear(), date.getMonth(), date.getDate()) < freezeUntil) {
      setDragError(`${date.toDateString()} is inside the freeze window (through ${lastFrozenDay.toDateString()}) — drop it on a later day.`)
      return
    }
    if (payload.componentType !== station.componentType) {
      setDragError(`'${station.name}' handles '${station.componentType}', but ${payload.jobRef} is '${payload.componentType ?? 'unknown'}'.`)
      return
    }
    try {
      await jobsApi.scheduleManually(payload.jobRef, station.id, localIsoDate(date))
      await reloadAfterChange()
    } catch (err) {
      setDragError(errorText(err, `Could not move ${payload.jobRef}.`))
    }
  }

  return (
    <div>
      <div style={{ display: 'flex', gap: 16 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
            <h2 style={{ margin: 0 }}>Calendar</h2>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <button className="btn btn-secondary" onClick={() => step(-1)}>
                &larr;
              </button>
              <span style={{ fontSize: 13, fontWeight: 600, minWidth: 140, textAlign: 'center' }}>
                {mode === 'day'
                  ? anchorDate.toDateString()
                  : mode === 'week'
                    ? `Week of ${startOfWeek(anchorDate).toDateString()}`
                    : anchorDate.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}
              </span>
              <button className="btn btn-secondary" onClick={() => step(1)}>
                &rarr;
              </button>
              <button className="btn btn-secondary" onClick={() => setAnchorDate(new Date())}>
                Today
              </button>
              {mode === 'week' && (
                <label style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 12 }}>
                  <input
                    type="checkbox"
                    checked={showWeekends}
                    onChange={(e) => {
                      setShowWeekends(e.target.checked)
                      try {
                        localStorage.setItem('calendar.showWeekends', String(e.target.checked))
                      } catch {
                        // not remembered this time
                      }
                    }}
                  />
                  Show weekends
                </label>
              )}
              <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--brand-primary)' }}>
                {(['day', 'week', 'month'] as const).map((m) => (
                  <button
                    key={m}
                    className="btn"
                    style={{ background: mode === m ? 'var(--brand-primary)' : 'var(--card-bg)', color: mode === m ? '#fff' : 'var(--brand-primary)', borderRadius: 0 }}
                    onClick={() => setMode(m)}
                  >
                    {m[0].toUpperCase() + m.slice(1)}
                  </button>
                ))}
              </div>
              {user?.isAdmin && (
                <button className="btn btn-secondary" disabled={recalculating} onClick={recalculate} title="Re-run the scheduler to tidy up gaps left by manual moves">
                  {recalculating ? 'Recalculating…' : 'Recalculate'}
                </button>
              )}
            </div>
          </div>

          {lastFrozenDay && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: 'var(--text-muted)', marginBottom: 10 }}>
              <LockIcon size={12} /> Frozen through {lastFrozenDay.toDateString()} — jobs starting before then can&rsquo;t be moved, and nothing can be dropped there (Settings → FreezeWindowDays).
            </div>
          )}

          {dragError && (
            <div className="card" style={{ marginBottom: 12, borderLeft: '3px solid #ef4444', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontSize: 13, color: '#9f1239' }}>{dragError}</span>
              <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => setDragError(null)}>
                Dismiss
              </button>
            </div>
          )}

          {mode === 'month' ? (
            <div className="card" style={{ padding: 0, overflow: 'hidden' }}>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)' }}>
                {MONTH_HEADINGS.map((n) => (
                  <div key={n} style={{ padding: '8px 10px', fontSize: 11, fontWeight: 700, color: 'var(--text-faint)', borderBottom: '1px solid var(--border-subtle)' }}>
                    {n}
                  </div>
                ))}
                {monthDays.map((d) => {
                  const inMonth = d.getMonth() === anchorDate.getMonth()
                  const dayBlocks = blocksForDay(d)
                  const shown = dayBlocks.slice(0, 4)
                  const more = dayBlocks.length - shown.length
                  return (
                    <div
                      key={d.toISOString()}
                      data-drop={`m-${localIsoDate(d)}`}
                      onDragOver={dropZone(`m-${d.toISOString()}`).onDragOver}
                      onDrop={(e) => onDropOnDay(e, d)}
                      style={{
                        minHeight: 110,
                        padding: 6,
                        borderBottom: '1px solid var(--divider-faint)',
                        borderRight: '1px solid var(--divider-faint)',
                        opacity: inMonth ? 1 : 0.4,
                        background: sameDay(d, new Date()) ? 'var(--sidebar-nav-active-bg)' : isFrozenDay(d) ? FROZEN_BG : undefined,
                        ...dropZone(`m-${d.toISOString()}`).style,
                      }}
                    >
                      <div style={{ fontSize: 11, fontWeight: 700, marginBottom: 4, color: inMonth ? undefined : 'var(--text-faint)', display: 'flex', alignItems: 'center', gap: 4 }}>
                        {d.getDate()}
                        {isFrozenDay(d) && <LockIcon size={10} />}
                      </div>
                      {shown.map(({ item, segment }, idx) => (
                        <div
                          key={`${item.id}-${idx}`}
                          draggable={canDrag(item)}
                          onDragStart={(e) => onBlockDragStart(e, item)}
                          onDragEnd={() => setDropTarget(null)}
                          style={{ cursor: canDrag(item) ? 'grab' : undefined }}
                        >
                          <CalendarBlock item={item} segment={segment} isLate={isLateFor(item.jobRef)} isAtRisk={isAtRiskFor(item.jobRef)} onDoubleClick={() => openJob(item.jobRef)} compact />
                        </div>
                      ))}
                      {more > 0 && <div style={{ fontSize: 11, color: 'var(--text-faint)', cursor: 'pointer' }} onClick={() => { setAnchorDate(d); setMode('day') }}>+{more} more</div>}
                    </div>
                  )
                })}
              </div>
            </div>
          ) : (
            <div className="card" style={{ overflowX: 'auto', padding: 0 }}>
              <div style={{ display: 'flex', minWidth: 'max-content' }}>
                {/* Sticky label column */}
                <div style={{ position: 'sticky', left: 0, background: 'var(--card-bg)', zIndex: 2, borderRight: '1px solid var(--border-subtle)' }}>
                  <div style={{ height: headerHeight, borderBottom: '1px solid var(--border-subtle)' }} />
                  {mode === 'day'
                    ? hours.map((h) => (
                        <div key={h} style={{ height: ROW_HEIGHT, width: 64, fontSize: 11, color: 'var(--text-faint)', padding: '4px 8px', borderBottom: '1px solid var(--divider-faint)' }}>
                          {String(h).padStart(2, '0')}:00
                        </div>
                      ))
                    : weekDays.map((d) => (
                        <div key={d.toISOString()} style={{ height: 90, width: 90, fontSize: 11, color: 'var(--text-faint)', padding: '4px 8px', borderBottom: '1px solid var(--divider-faint)' }}>
                          {DAY_NAMES[d.getDay()]} {d.getDate()}
                          {isFrozenDay(d) && (
                            <div style={{ display: 'flex', alignItems: 'center', gap: 3, marginTop: 4 }}>
                              <LockIcon size={10} /> frozen
                            </div>
                          )}
                        </div>
                      ))}
                </div>

                {stations.map((station) => (
                  <div key={station.id} style={{ width: 220, borderRight: '1px solid var(--border-subtle)', flexShrink: 0 }}>
                    <div
                      style={{ height: headerHeight, boxSizing: 'border-box', display: 'flex', flexDirection: 'column', alignItems: 'stretch', justifyContent: 'center', gap: 2, padding: '0 4px', fontWeight: 700, fontSize: 13, color: 'var(--brand-primary)', borderBottom: '1px solid var(--border-subtle)' }}
                    >
                      <div style={{ textAlign: 'center' }}>{station.name}</div>
                      {/* Day view: the line's day change sits in the header, so every column's hour grid lines up with the labels */}
                      {mode === 'day' && dayChip(station, anchorDate)}
                    </div>

                    {mode === 'day' ? (
                      <div
                        style={{
                          position: 'relative',
                          height: hours.length * ROW_HEIGHT,
                          background: dayChangeFor(station.id, anchorDate)?.isClosed ? CLOSED_BG : isFrozenDay(anchorDate) ? FROZEN_BG : undefined,
                          ...dropZone(`d-${station.id}`).style,
                        }}
                        onDragOver={dropZone(`d-${station.id}`).onDragOver}
                        onDrop={(e) => onDropOnStationDay(e, station, anchorDate)}
                      >
                        {hours.map((h) => (
                          <div key={h} style={{ position: 'absolute', top: (h - shiftStartHour) * ROW_HEIGHT, width: '100%', height: ROW_HEIGHT, borderBottom: '1px solid var(--divider-faint)' }} />
                        ))}
                        {[
                          // Breaks, plus any part of the first/last hour outside the working day (e.g. 14:30–15:00).
                          ...calendar.breaks.map((b) => ({ key: `b${b.start}`, from: hoursOf(b.start), to: hoursOf(b.end), label: 'Break', title: `Break ${b.start}–${b.end}` })),
                          { key: 'before', from: shiftStartHour, to: hoursOf(calendar.dayStart), label: '', title: `Work starts at ${calendar.dayStart}` },
                          { key: 'after', from: hoursOf(calendar.dayEnd), to: shiftEndHour, label: '', title: `Work ends at ${calendar.dayEnd}` },
                        ]
                          .filter((o) => o.to > o.from && o.from >= shiftStartHour && o.to <= shiftEndHour)
                          .map((o) => (
                            <div
                              key={o.key}
                              title={o.title}
                              style={{
                                position: 'absolute',
                                top: (o.from - shiftStartHour) * ROW_HEIGHT,
                                height: (o.to - o.from) * ROW_HEIGHT,
                                left: 0,
                                right: 0,
                                background: 'repeating-linear-gradient(45deg, var(--divider-faint), var(--divider-faint) 6px, var(--border-subtle) 6px, var(--border-subtle) 12px)',
                                opacity: 0.85,
                                fontSize: 10,
                                color: 'var(--text-muted)',
                                padding: '1px 6px',
                                // Above the job blocks, so work that runs through a break is easy to spot.
                                zIndex: 2,
                                pointerEvents: 'none',
                              }}
                            >
                              {o.label}
                            </div>
                          ))}
                        {blocksForStationDay(station.id, anchorDate).map(({ item, segment }, idx) => {
                          const start = new Date(segment.start)
                          const end = new Date(segment.end)
                          const startOffsetH = start.getHours() + start.getMinutes() / 60 - shiftStartHour
                          const durationH = Math.max((end.getTime() - start.getTime()) / 3600000, 0.5)
                          return (
                            <div
                              key={`${item.id}-${idx}`}
                              draggable={canDrag(item)}
                              onDragStart={(e) => onBlockDragStart(e, item)}
                              onDragEnd={() => setDropTarget(null)}
                              style={{
                                position: 'absolute',
                                top: startOffsetH * ROW_HEIGHT,
                                left: 2,
                                right: 2,
                                height: durationH * ROW_HEIGHT - 2,
                                zIndex: 1,
                                cursor: canDrag(item) ? 'grab' : undefined,
                              }}
                            >
                              <CalendarBlock item={item} segment={segment} isLate={isLateFor(item.jobRef)} isAtRisk={isAtRiskFor(item.jobRef)} onDoubleClick={() => openJob(item.jobRef)} />
                            </div>
                          )
                        })}
                        {sameDay(anchorDate, now) && now.getHours() + now.getMinutes() / 60 >= shiftStartHour && now.getHours() + now.getMinutes() / 60 <= shiftEndHour && (
                          <div className="now-line" style={{ top: (now.getHours() + now.getMinutes() / 60 - shiftStartHour) * ROW_HEIGHT }} title={`Now — ${now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`} />
                        )}
                      </div>
                    ) : (
                      <div>
                        {weekDays.map((d) => (
                          <div
                            key={d.toISOString()}
                            style={{
                              height: 90,
                              borderBottom: '1px solid var(--divider-faint)',
                              padding: 2,
                              overflow: 'auto',
                              background: dayChangeFor(station.id, d)?.isClosed ? CLOSED_BG : isFrozenDay(d) ? FROZEN_BG : undefined,
                              ...dropZone(`w-${station.id}-${d.toISOString()}`).style,
                            }}
                            data-drop={`w-${station.id}-${localIsoDate(d)}`}
                            onDragOver={dropZone(`w-${station.id}-${d.toISOString()}`).onDragOver}
                            onDrop={(e) => onDropOnStationDay(e, station, d)}
                          >
                            {dayChip(station, d)}
                            {blocksForStationDay(station.id, d).map(({ item, segment }, idx) => (
                              <div key={`${item.id}-${idx}`} draggable={canDrag(item)} onDragStart={(e) => onBlockDragStart(e, item)} onDragEnd={() => setDropTarget(null)} style={{ cursor: canDrag(item) ? 'grab' : undefined }}>
                                <CalendarBlock item={item} segment={segment} isLate={isLateFor(item.jobRef)} isAtRisk={isAtRiskFor(item.jobRef)} onDoubleClick={() => openJob(item.jobRef)} compact />
                              </div>
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
          )}
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
              {queuedUnscheduled.length === 0 && <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>None</div>}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Creep alerts</div>
              {creepAlerts.map((i) => (
                <div key={i.id} style={{ fontSize: 12, marginBottom: 4 }}>
                  {i.jobRef}
                </div>
              ))}
              {creepAlerts.length === 0 && <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>None</div>}
            </div>

            <div>
              <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 8 }}>Station EqA today</div>
              {stations.map((s) => {
                const done = producedToday[s.id] ?? 0
                const pct = s.todayTargetEqa ? Math.min((done / s.todayTargetEqa) * 100, 100) : 0
                return (
                  <div key={s.id} style={{ fontSize: 12, marginBottom: 8 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                      <span>{s.name}</span>
                      <span>
                        {done.toFixed(1)} / {s.todayTargetEqa ?? '—'}
                      </span>
                    </div>
                    {s.todayTargetEqa ? (
                      <div style={{ height: 6, background: 'var(--border-subtle)', borderRadius: 3, marginTop: 3 }}>
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
          onEdit={user?.isAdmin ? () => { setEditing(selectedJob); setSelectedJob(null) } : undefined}
          onApproveForCutting={
            user?.isAdmin && canApproveForCutting(selectedJob)
              ? async () => {
                  if (!(await approveForCuttingWithConfirm(selectedJob.jobRef))) return
                  setSelectedJob(null)
                  await reloadAfterChange()
                }
              : undefined
          }
        />
      )}
      {pinning && (
        <ScheduleManuallyModal
          job={pinning}
          onClose={() => setPinning(null)}
          onDone={() => {
            setPinning(null)
            reloadAfterChange()
          }}
        />
      )}
      {editing && <JobEditModal job={editing} onClose={() => setEditing(null)} onSave={saveEdit} />}
      {editingDay && (
        <DayChangeModal
          station={editingDay.station}
          date={editingDay.date}
          existing={dayChangeFor(editingDay.station.id, editingDay.date)}
          dayStart={calendar.dayStart}
          dayEnd={calendar.dayEnd}
          onClose={() => setEditingDay(null)}
          onSaved={() => {
            setEditingDay(null)
            reloadAfterChange()
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
