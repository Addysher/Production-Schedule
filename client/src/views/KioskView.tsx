import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { factoriesApi, settingsApi } from '../api/admin'
import { applyTheme } from '../theme/applyTheme'
import { jobsApi } from '../api/jobs'
import { reportsApi } from '../api/reports'
import { scheduleApi } from '../api/schedule'
import { stationsApi } from '../api/stations'
import { useAuth } from '../auth/AuthContext'
import { errorText } from '../components/Modal'
import { StatusPill } from '../components/StatusPill'
import type { FactoryDto, JobSummaryDto, ScheduleItemDto, StationDto, StationProgressDto } from '../types/api'
import { startAutoRefresh } from '../utils/appEvents'
import { localIsoDate } from '../utils/dates'

// v3.0 kiosk: a read-only, full-screen view of one factory's stations for a screen on the floor —
// what's running, what's next today, and how each line is doing against its target. Refreshes itself.

function KioskShell({ title, children }: { title: string; children: React.ReactNode }) {
  const { user, logout } = useAuth()
  const [now, setNow] = useState(() => new Date())
  // Outside the main layout, so apply the site's colours here too.
  useEffect(() => {
    settingsApi
      .getAll()
      .then(applyTheme)
      .catch(() => {
        // keep the default colours
      })
  }, [])
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 15000)
    return () => clearInterval(timer)
  }, [])

  return (
    <div style={{ minHeight: '100vh', background: 'var(--page-bg)', color: 'var(--text-primary)', padding: '20px 28px', boxSizing: 'border-box' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 18, gap: 16 }}>
        <div style={{ fontSize: 34, fontWeight: 800, color: 'var(--brand-primary)' }}>{title}</div>
        <div style={{ display: 'flex', alignItems: 'baseline', gap: 18 }}>
          <div style={{ fontSize: 30, fontWeight: 700 }}>{now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</div>
          <div style={{ fontSize: 18, color: 'var(--text-muted)' }}>{now.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' })}</div>
          {user?.role === 'Kiosk' ? (
            <button className="btn btn-secondary" onClick={logout}>
              Log out
            </button>
          ) : (
            <Link className="btn btn-secondary" to="/dashboard" style={{ textDecoration: 'none' }}>
              Exit kiosk
            </Link>
          )}
        </div>
      </div>
      {children}
    </div>
  )
}

export function KioskPickerView() {
  const [factories, setFactories] = useState<FactoryDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    factoriesApi
      .getAll()
      .then((all) => setFactories(all.filter((f) => f.isActive)))
      .catch((e) => setError(errorText(e, 'Could not load the factories.')))
  }, [])

  return (
    <KioskShell title="Choose a factory">
      {error && <div style={{ color: '#dc2626', fontSize: 18 }}>{error}</div>}
      {factories?.length === 0 && (
        <div style={{ fontSize: 20, color: 'var(--text-muted)' }}>No factories are set up yet. An Admin can add them in Settings → Factories.</div>
      )}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 16 }}>
        {factories?.map((f) => (
          <Link key={f.id} to={`/kiosk/${f.id}`} className="card" style={{ textDecoration: 'none', color: 'var(--text-primary)', fontSize: 26, fontWeight: 700, padding: 28 }}>
            {f.name}
            <div style={{ fontSize: 15, fontWeight: 500, color: 'var(--text-muted)', marginTop: 6 }}>{f.stationIds.length} station(s)</div>
          </Link>
        ))}
      </div>
    </KioskShell>
  )
}

type Snapshot = {
  factory: FactoryDto
  stations: StationDto[]
  items: ScheduleItemDto[]
  jobs: JobSummaryDto[]
  progress: StationProgressDto[]
  produced: Record<number, number>
  targets: Record<number, number | null>
  loadedAt: Date
}

export function KioskView() {
  const { factoryId } = useParams()
  const [data, setData] = useState<Snapshot | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const load = async () => {
      try {
        const today = localIsoDate(new Date())
        const [factories, stations, items, jobs, progress, produced] = await Promise.all([
          factoriesApi.getAll(),
          stationsApi.getAll(),
          scheduleApi.getCurrent(),
          jobsApi.getAll(),
          scheduleApi.stationProgress(),
          reportsApi.production(today, today),
        ])
        const factory = factories.find((f) => String(f.id) === factoryId)
        if (!factory) {
          setError('This factory no longer exists.')
          return
        }
        const mine = stations.filter((s) => s.isActive && factory.stationIds.includes(s.id)).sort((a, b) => a.displayOrder - b.displayOrder)
        setData({
          factory,
          stations: mine,
          items,
          jobs,
          progress,
          produced: Object.fromEntries(produced.map((r) => [r.stationId, r.eqa])),
          targets: Object.fromEntries(mine.map((s) => [s.id, s.todayTargetEqa])),
          loadedAt: new Date(),
        })
        setError(null)
      } catch (e) {
        // Keep showing the last good picture; just say it's stale.
        setError(errorText(e, 'Could not refresh — showing the last update.'))
      }
    }
    load()
    return startAutoRefresh(load, 60000)
  }, [factoryId])

  const todayIso = localIsoDate(new Date())
  const jobByRef = useMemo(() => new Map((data?.jobs ?? []).map((j) => [j.jobRef, j])), [data])

  if (!data) {
    return <KioskShell title="Loading…">{error && <div style={{ color: '#dc2626', fontSize: 18 }}>{error}</div>}</KioskShell>
  }

  return (
    <KioskShell title={data.factory.name}>
      {data.stations.length === 0 && (
        <div style={{ fontSize: 20, color: 'var(--text-muted)' }}>No stations are assigned to this factory yet (Settings → Stations → Factory).</div>
      )}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        {data.stations.map((station) => {
          const made = data.produced[station.id] ?? 0
          const target = data.targets[station.id]
          const pct = target ? Math.min(100, (made / target) * 100) : 0
          const running = data.items.find((i) => i.stationId === station.id && i.actualStart && !i.actualEnd)
          const progress = running ? data.progress.find((p) => p.jobRef === running.jobRef) : undefined
          const today = data.items
            .filter((i) => i.stationId === station.id && i !== running)
            .flatMap((i) => i.segments.filter((s) => s.date.slice(0, 10) === todayIso).map((s) => ({ item: i, segment: s })))
            .filter(({ item }) => !item.actualEnd)
            .sort((a, b) => a.segment.start.localeCompare(b.segment.start))

          return (
            <div key={station.id} className="card" style={{ display: 'grid', gridTemplateColumns: 'minmax(220px, 1fr) minmax(280px, 1.4fr) minmax(320px, 2fr)', gap: 24, alignItems: 'start' }}>
              <div>
                <div style={{ fontSize: 26, fontWeight: 800 }}>{station.name}</div>
                <div style={{ fontSize: 15, color: 'var(--text-muted)' }}>{station.componentType}</div>
                <div style={{ fontSize: 30, fontWeight: 800, color: 'var(--brand-primary)', marginTop: 10 }}>
                  {made.toFixed(1)}
                  <span style={{ fontSize: 18, color: 'var(--text-muted)', fontWeight: 600 }}> / {target ?? '—'} Eq</span>
                </div>
                {target ? (
                  <div style={{ height: 10, borderRadius: 5, background: 'var(--divider-faint)', marginTop: 6, overflow: 'hidden' }}>
                    <div style={{ width: `${pct}%`, height: '100%', background: pct >= 100 ? '#16a34a' : 'var(--brand-primary)' }} />
                  </div>
                ) : null}
              </div>

              <div>
                <div style={{ fontSize: 13, textTransform: 'uppercase', fontWeight: 700, color: 'var(--text-faint)', marginBottom: 6 }}>Now</div>
                {running ? (
                  <>
                    <div style={{ fontSize: 26, fontWeight: 800 }}>{running.jobRef}</div>
                    <div style={{ fontSize: 16, color: 'var(--text-muted)' }}>{running.clientName ?? ''}</div>
                    {progress && (
                      <div style={{ fontSize: 20, marginTop: 6 }}>
                        Member <b>{progress.currentMemberId ?? '—'}</b>
                        {progress.nextMemberId && <span style={{ color: 'var(--text-muted)' }}> → {progress.nextMemberId}</span>}
                        <div style={{ fontSize: 15, color: 'var(--text-muted)' }}>
                          {progress.membersDone} of {progress.membersTotal} done
                        </div>
                      </div>
                    )}
                  </>
                ) : (
                  <div style={{ fontSize: 20, color: 'var(--text-muted)' }}>Nothing running</div>
                )}
              </div>

              <div>
                <div style={{ fontSize: 13, textTransform: 'uppercase', fontWeight: 700, color: 'var(--text-faint)', marginBottom: 6 }}>Up next today</div>
                {today.length === 0 && <div style={{ fontSize: 18, color: 'var(--text-muted)' }}>Nothing else planned today</div>}
                {today.slice(0, 5).map(({ item, segment }) => {
                  const job = jobByRef.get(item.jobRef)
                  return (
                    <div key={`${item.id}-${segment.start}`} style={{ display: 'flex', alignItems: 'center', gap: 12, fontSize: 18, padding: '5px 0', borderBottom: '1px solid var(--divider-faint)' }}>
                      <span style={{ width: 60, fontWeight: 700 }}>{new Date(segment.start).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                      <span style={{ fontWeight: 700, minWidth: 110 }}>{item.jobRef}</span>
                      <span style={{ flex: 1, color: 'var(--text-muted)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{item.clientName ?? ''}</span>
                      <span style={{ color: 'var(--text-muted)' }}>{segment.plannedEqA.toFixed(1)} Eq</span>
                      <StatusPill status={item.jobStatus} isLate={job?.isLate ?? false} />
                    </div>
                  )
                })}
                {today.length > 5 && <div style={{ fontSize: 15, color: 'var(--text-muted)', marginTop: 4 }}>+ {today.length - 5} more</div>}
              </div>
            </div>
          )
        })}
      </div>
      <div style={{ fontSize: 13, color: 'var(--text-faint)', marginTop: 14 }}>
        Updated {data.loadedAt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
        {error && <span style={{ color: '#dc2626' }}> — {error}</span>}
      </div>
    </KioskShell>
  )
}
