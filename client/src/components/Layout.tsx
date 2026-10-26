import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { onJobsChanged, requestPanelOpen } from '../utils/appEvents'
import { useAuth } from '../auth/AuthContext'
import { jobsApi } from '../api/jobs'
import { notificationsApi } from '../api/reports'
import { settingsApi } from '../api/admin'
import { applyTheme, cycleThemeOverride, getThemeOverride, onThemeChanged, type ThemeMode } from '../theme/applyTheme'

const THEME_ICON: Record<ThemeMode, string> = { system: '\u{1F4BB}', light: '☀️', dark: '\u{1F319}' }
const THEME_LABEL: Record<ThemeMode, string> = { system: 'Auto', light: 'Light', dark: 'Dark' }

const navItems = [
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/calendar', label: 'Calendar' },
  { to: '/jobs', label: 'Jobs' },
  { to: '/quotes', label: 'Quotes' },
  { to: '/reports', label: 'Reports' },
  { to: '/kiosk', label: 'Kiosk display' },
]

const POLL_MS = 30000

// §5.1 / §5.12: "[2 new jobs · 1 error]". Each kind keeps its own "last seen" marker (the server's own clock
// from the previous response, so no client/server clock skew), and clicking one kind acknowledges only that kind.
function useNotifications(username: string | undefined) {
  const [counts, setCounts] = useState({ newJobs: 0, errors: 0 })
  const latest = useRef<string | null>(null)
  const jobsKey = `notifications.jobs.${username}`
  const errorsKey = `notifications.errors.${username}`

  useEffect(() => {
    if (!username) return
    let cancelled = false

    async function poll() {
      try {
        const read = (k: string) => {
          try {
            return localStorage.getItem(k) ?? undefined
          } catch {
            return undefined
          }
        }
        const write = (k: string, v: string) => {
          try {
            localStorage.setItem(k, v)
          } catch {
            // no storage: the badge just restarts from "now" on each visit
          }
        }
        const jobsSince = read(jobsKey)
        const errorsSince = read(errorsKey)
        const [forJobs, forErrors] = await Promise.all([notificationsApi.get(jobsSince), notificationsApi.get(errorsSince)])
        if (cancelled) return
        latest.current = forErrors.serverNow
        if (!jobsSince) write(jobsKey, forJobs.serverNow) // first ever look: start counting from now
        if (!errorsSince) write(errorsKey, forErrors.serverNow)
        setCounts({ newJobs: jobsSince ? forJobs.newJobs : 0, errors: errorsSince ? forErrors.errors : 0 })
      } catch {
        // transient failure — try again next tick
      }
    }

    poll()
    const timer = setInterval(poll, POLL_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [username, jobsKey, errorsKey])

  const acknowledge = useCallback(
    (kind: 'jobs' | 'errors') => {
      if (latest.current) {
        try {
          localStorage.setItem(kind === 'jobs' ? jobsKey : errorsKey, latest.current)
        } catch {
          // ignore
        }
      }
      setCounts((c) => (kind === 'jobs' ? { ...c, newJobs: 0 } : { ...c, errors: 0 }))
    },
    [jobsKey, errorsKey],
  )

  return { counts, acknowledge }
}

export function Layout({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [lateCount, setLateCount] = useState(0)
  const [themeOverride, setThemeOverride] = useState<ThemeMode>(() => getThemeOverride())
  const { counts, acknowledge } = useNotifications(user?.username)

  // Kept current as the day goes on (jobs become late at midnight, or stop being late when edited).
  useEffect(() => {
    const load = () =>
      jobsApi
        .getAll()
        .then((jobs) => setLateCount(jobs.filter((j) => j.isLate).length))
        .catch(() => {
          // transient failure — keep the last count
        })
    load()
    const timer = setInterval(load, POLL_MS)
    const unsubscribe = onJobsChanged(load)
    return () => {
      clearInterval(timer)
      unsubscribe()
    }
  }, [])

  // Applied for every signed-in user (Admin or Viewer render the same themed shell) — see
  // theme/applyTheme.ts and Settings -> System Settings -> Appearance for where this is edited.
  useEffect(() => {
    settingsApi
      .getAll()
      .then((settings) => {
        applyTheme(settings)
        setThemeOverride(getThemeOverride())
      })
      .catch(() => {
        // transient failure — the shell just keeps whatever theme was last applied (or the CSS defaults)
      })
  }, [])

  function toggleTheme() {
    setThemeOverride(cycleThemeOverride())
  }

  // Keeps the header's own label in sync when the theme changes from elsewhere in the same tab — e.g.
  // an admin picking a new default on the Settings -> Appearance page.
  useEffect(() => onThemeChanged(() => setThemeOverride(getThemeOverride())), [])

  const navStyle = (isActive: boolean) => ({
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '10px 12px',
    borderRadius: 6,
    textDecoration: 'none',
    fontSize: 14,
    fontWeight: 600,
    color: isActive ? 'var(--sidebar-nav-active-text)' : 'var(--sidebar-nav-inactive)',
    background: isActive ? 'var(--sidebar-nav-active-bg)' : 'transparent',
    borderLeft: isActive ? '3px solid var(--sidebar-nav-active-text)' : '3px solid transparent',
  })

  function showErrors() {
    try {
      localStorage.setItem('sidePanel.dashboard.open', 'true') // the "Needs attention" list lives in the Dashboard's side panel
    } catch {
      // ignore
    }
    acknowledge('errors')
    navigate('/dashboard')
    requestPanelOpen('dashboard')
  }

  return (
    <div style={{ display: 'flex', minHeight: '100vh' }}>
      <aside style={{ width: 220, background: 'var(--sidebar-bg)', color: '#fff', display: 'flex', flexDirection: 'column', flexShrink: 0 }}>
        <div style={{ padding: '20px 16px', fontWeight: 700, fontSize: 16, letterSpacing: 0.5 }}>Production Scheduler</div>
        <nav style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 2, padding: '0 8px' }}>
          {navItems.map((item) => (
            <NavLink key={item.to} to={item.to} style={({ isActive }) => navStyle(isActive)}>
              {item.label}
              {item.to === '/jobs' && counts.newJobs > 0 && (
                <span
                  data-testid="nav-new-jobs"
                  style={{ background: 'var(--badge-new-bg)', color: 'var(--badge-new-text)', borderRadius: 999, fontSize: 11, fontWeight: 700, padding: '1px 8px' }}
                >
                  {counts.newJobs}
                </span>
              )}
            </NavLink>
          ))}
          {user?.isAdmin && (
            <NavLink key="/settings" to="/settings" style={({ isActive }) => navStyle(isActive)}>
              Settings
            </NavLink>
          )}
        </nav>
        {lateCount > 0 && (
          <div style={{ margin: 12, padding: '8px 12px', borderRadius: 6, background: 'var(--alert-late-bg)', color: 'var(--alert-late-text)', fontSize: 13, fontWeight: 600 }}>
            {lateCount} late
          </div>
        )}
      </aside>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <header
          style={{
            height: 56,
            background: 'var(--card-bg)',
            borderBottom: '1px solid var(--topbar-border)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'flex-end',
            padding: '0 20px',
            gap: 12,
          }}
        >
          {(counts.newJobs > 0 || counts.errors > 0) && (
            <div data-testid="notification-badge" style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
              {counts.newJobs > 0 && (
                <button
                  onClick={() => {
                    acknowledge('jobs')
                    navigate('/jobs')
                  }}
                  style={{ border: 'none', borderRadius: 999, padding: '4px 12px', fontSize: 12, fontWeight: 700, background: 'var(--badge-new-bg)', color: 'var(--badge-new-text)' }}
                >
                  {counts.newJobs} new {counts.newJobs === 1 ? 'job' : 'jobs'}
                </button>
              )}
              {counts.newJobs > 0 && counts.errors > 0 && <span style={{ color: 'var(--text-faint)' }}>&middot;</span>}
              {counts.errors > 0 && (
                <button
                  onClick={showErrors}
                  style={{ border: 'none', borderRadius: 999, padding: '4px 12px', fontSize: 12, fontWeight: 700, background: '#ef4444', color: '#fff' }}
                >
                  {counts.errors} {counts.errors === 1 ? 'error' : 'errors'}
                </button>
              )}
            </div>
          )}
          <span style={{ fontSize: 13, color: 'var(--text-muted)' }}>
            {user?.username} &middot; {user?.role}
          </span>
          <button
            type="button"
            onClick={toggleTheme}
            title={`Theme: ${THEME_LABEL[themeOverride]} (click to change)`}
            className="btn btn-secondary"
            style={{ display: 'flex', alignItems: 'center', gap: 6 }}
          >
            <span aria-hidden="true">{THEME_ICON[themeOverride]}</span>
            {THEME_LABEL[themeOverride]}
          </button>
          <button
            className="btn btn-secondary"
            onClick={() => {
              logout()
              navigate('/login')
            }}
          >
            Log out
          </button>
        </header>
        <main style={{ flex: 1, padding: 24, overflow: 'auto' }}>{children}</main>
      </div>
    </div>
  )
}
