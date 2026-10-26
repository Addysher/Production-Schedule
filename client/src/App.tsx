import { useEffect, useState, type ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import { jobsApi } from './api/jobs'
import { Layout } from './components/Layout'
import { LoginView } from './views/LoginView'
import { DashboardView } from './views/DashboardView'
import { CalendarView } from './views/CalendarView'
import { JobsView } from './views/JobsView'
import { QuotesView } from './views/QuotesView'
import { ReportsView } from './views/ReportsView'
import { SettingsView } from './views/SettingsView'
import { KioskPickerView, KioskView } from './views/KioskView'
import type { AuthState } from './auth/AuthContext'

// A Kiosk account only ever sees its own factory's kiosk page.
const kioskPath = (user: AuthState) => (user.factoryId ? `/kiosk/${user.factoryId}` : '/kiosk')
const homePath = (user: AuthState | null) => (!user ? '/login' : user.role === 'Kiosk' ? kioskPath(user) : '/dashboard')

// Runs GET /api/jobs/status-refresh once per session before any authenticated view renders (§7).
function useStatusRefreshOnMount(enabled: boolean) {
  const [ready, setReady] = useState(false)

  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    jobsApi
      .statusRefresh()
      .catch(() => {})
      .finally(() => {
        if (!cancelled) setReady(true)
      })
    return () => {
      cancelled = true
    }
  }, [enabled])

  return ready
}

function ProtectedRoute({ children, adminOnly }: { children: ReactNode; adminOnly?: boolean }) {
  const { user } = useAuth()
  const location = useLocation()
  const statusRefreshReady = useStatusRefreshOnMount(!!user)

  if (!user) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }
  if (user.role === 'Kiosk') {
    return <Navigate to={kioskPath(user)} replace />
  }
  if (adminOnly && !user.isAdmin) {
    return <Navigate to="/dashboard" replace />
  }
  if (!statusRefreshReady) {
    return (
      <div style={{ display: 'flex', minHeight: '100vh', alignItems: 'center', justifyContent: 'center', color: 'var(--text-muted)' }}>
        Loading…
      </div>
    )
  }
  return <Layout>{children}</Layout>
}

// After signing in: the page that sent you to the login screen, if any.
function BackToWhereYouWere({ fallback }: { fallback: string }) {
  const from = (useLocation().state as { from?: { pathname?: string } } | null)?.from?.pathname
  return <Navigate to={from && from !== '/login' ? from : fallback} replace />
}

// The kiosk is full screen (no sidebar or header) and open to any signed-in user. A Kiosk login is tied
// to one factory: it's always taken to that factory's display, never the list or another factory's.
function KioskRoute({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const location = useLocation()
  if (!user) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }
  // (A kiosk login without a factory stays on /kiosk, which says so.)
  if (user.role === 'Kiosk' && location.pathname !== kioskPath(user)) {
    return <Navigate to={kioskPath(user)} replace />
  }
  return <>{children}</>
}

export default function App() {
  const { user } = useAuth()

  return (
    <Routes>
      <Route path="/login" element={user ? <BackToWhereYouWere fallback={homePath(user)} /> : <LoginView />} />
      <Route path="/kiosk" element={<KioskRoute><KioskPickerView /></KioskRoute>} />
      <Route path="/kiosk/:factoryId" element={<KioskRoute><KioskView /></KioskRoute>} />
      <Route
        path="/dashboard"
        element={
          <ProtectedRoute>
            <DashboardView />
          </ProtectedRoute>
        }
      />
      <Route
        path="/calendar"
        element={
          <ProtectedRoute>
            <CalendarView />
          </ProtectedRoute>
        }
      />
      <Route
        path="/jobs"
        element={
          <ProtectedRoute>
            <JobsView />
          </ProtectedRoute>
        }
      />
      <Route
        path="/quotes"
        element={
          <ProtectedRoute>
            <QuotesView />
          </ProtectedRoute>
        }
      />
      <Route
        path="/reports"
        element={
          <ProtectedRoute>
            <ReportsView />
          </ProtectedRoute>
        }
      />
      <Route
        path="/settings"
        element={
          <ProtectedRoute adminOnly>
            <SettingsView />
          </ProtectedRoute>
        }
      />
      <Route path="*" element={<Navigate to={homePath(user)} replace />} />
    </Routes>
  )
}
