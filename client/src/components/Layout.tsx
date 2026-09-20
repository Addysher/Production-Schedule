import { useEffect, useState, type ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { jobsApi } from '../api/jobs'

const navItems = [
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/calendar', label: 'Calendar' },
  { to: '/jobs', label: 'Jobs' },
  { to: '/quotes', label: 'Quotes' },
  { to: '/reports', label: 'Reports' },
]

export function Layout({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [lateCount, setLateCount] = useState(0)

  useEffect(() => {
    jobsApi
      .getAll()
      .then((jobs) => setLateCount(jobs.filter((j) => j.isLate).length))
      .catch(() => setLateCount(0))
  }, [])

  return (
    <div style={{ display: 'flex', minHeight: '100vh' }}>
      <aside
        style={{
          width: 220,
          background: 'var(--sidebar-bg)',
          color: '#fff',
          display: 'flex',
          flexDirection: 'column',
          flexShrink: 0,
        }}
      >
        <div style={{ padding: '20px 16px', fontWeight: 700, fontSize: 16, letterSpacing: 0.5 }}>
          Production Scheduler
        </div>
        <nav style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 2, padding: '0 8px' }}>
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              style={({ isActive }) => ({
                display: 'block',
                padding: '10px 12px',
                borderRadius: 6,
                textDecoration: 'none',
                fontSize: 14,
                fontWeight: 600,
                color: isActive ? 'var(--sidebar-nav-active-text)' : 'var(--sidebar-nav-inactive)',
                background: isActive ? 'var(--sidebar-nav-active-bg)' : 'transparent',
                borderLeft: isActive ? '3px solid var(--sidebar-nav-active-text)' : '3px solid transparent',
              })}
            >
              {item.label}
            </NavLink>
          ))}
          {user?.isAdmin && (
            <NavLink
              key="/settings"
              to="/settings"
              style={({ isActive }) => ({
                display: 'block',
                padding: '10px 12px',
                borderRadius: 6,
                textDecoration: 'none',
                fontSize: 14,
                fontWeight: 600,
                color: isActive ? 'var(--sidebar-nav-active-text)' : 'var(--sidebar-nav-inactive)',
                background: isActive ? 'var(--sidebar-nav-active-bg)' : 'transparent',
                borderLeft: isActive ? '3px solid var(--sidebar-nav-active-text)' : '3px solid transparent',
              })}
            >
              Settings
            </NavLink>
          )}
        </nav>
        {lateCount > 0 && (
          <div
            style={{
              margin: 12,
              padding: '8px 12px',
              borderRadius: 6,
              background: 'var(--alert-late-bg)',
              color: 'var(--alert-late-text)',
              fontSize: 13,
              fontWeight: 600,
            }}
          >
            {lateCount} late
          </div>
        )}
      </aside>

      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <header
          style={{
            height: 56,
            background: '#fff',
            borderBottom: '1px solid var(--topbar-border)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'flex-end',
            padding: '0 20px',
            gap: 16,
          }}
        >
          <span style={{ fontSize: 13, color: '#64748b' }}>
            {user?.username} &middot; {user?.role}
          </span>
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
