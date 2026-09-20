import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { authApi } from '../api/auth'
import { setAuthToken, setUnauthorizedHandler } from '../api/client'
import type { Role } from '../types/api'

interface AuthState {
  username: string
  role: Role
  isAdmin: boolean
}

interface AuthContextValue {
  user: AuthState | null
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthState | null>(null)

  const logout = useCallback(() => {
    setAuthToken(null)
    setUser(null)
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    const response = await authApi.login(username, password)
    setAuthToken(response.token)
    setUser({ username: response.username, role: response.role, isAdmin: response.role === 'Admin' })
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
