import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { authApi } from '../api/auth'
import { setAuthToken, setUnauthorizedHandler } from '../api/client'
import type { Role } from '../types/api'
import { canRescheduleRole } from './permissions'

export interface AuthState {
  username: string
  role: Role
  isAdmin: boolean
  canReschedule: boolean
  factoryId: number | null // a Kiosk login only ever shows this factory's display
}

const toAuthState = (username: string, role: Role, factoryId: number | null = null): AuthState => ({
  username,
  role,
  isAdmin: role === 'Admin',
  canReschedule: canRescheduleRole(role),
  factoryId: role === 'Kiosk' ? factoryId : null,
})

interface StoredSession {
  token: string
  username: string
  role: Role
  factoryId?: number | null
}

interface AuthContextValue {
  user: AuthState | null
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

// sessionStorage (not localStorage): the session should survive a reload/navigation within the tab
// but still end when the browser/tab closes, matching "stay signed in until I close the browser or
// log out" rather than persisting indefinitely on disk.
const SESSION_KEY = 'auth.session'

// The server's JWT (480 min expiry, see JwtSettings) already carries 'exp' — decoded here only so a
// stale session isn't restored and then immediately bounced by the first API call's 401.
function jwtExpiryMs(token: string): number | null {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))
    return typeof payload.exp === 'number' ? payload.exp * 1000 : null
  } catch {
    return null
  }
}

function readStoredSession(): StoredSession | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    if (!raw) return null
    const stored = JSON.parse(raw) as StoredSession
    const expiry = jwtExpiryMs(stored.token)
    if (expiry !== null && expiry <= Date.now()) {
      sessionStorage.removeItem(SESSION_KEY)
      return null
    }
    return stored
  } catch {
    return null
  }
}

function writeStoredSession(session: StoredSession | null) {
  try {
    if (session) sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
    else sessionStorage.removeItem(SESSION_KEY)
  } catch {
    // no storage: the tab just won't survive a reload, same as before this fix
  }
}

function restoreSession(): AuthState | null {
  const stored = readStoredSession()
  if (!stored) return null
  setAuthToken(stored.token)
  return toAuthState(stored.username, stored.role, stored.factoryId ?? null)
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthState | null>(restoreSession)

  const logout = useCallback(() => {
    setAuthToken(null)
    writeStoredSession(null)
    setUser(null)
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    const response = await authApi.login(username, password)
    setAuthToken(response.token)
    writeStoredSession({ token: response.token, username: response.username, role: response.role, factoryId: response.factoryId })
    setUser(toAuthState(response.username, response.role, response.factoryId))
  }, [])

  // A 401 from any API call (expired/invalid token) drops the session back to the login screen.
  useMemo(() => setUnauthorizedHandler(() => logout()), [logout])

  const value = useMemo(() => ({ user, login, logout }), [user, login, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within AuthProvider')
  }
  return ctx
}
