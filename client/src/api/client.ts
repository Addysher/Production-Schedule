import axios from 'axios'

// Token is held in memory only (§7 Step 7) — not persisted to localStorage, so a hard refresh
// requires logging in again. A module-level variable (rather than reading from React state on
// every request) keeps the interceptor synchronous and simple.
let authToken: string | null = null

export function setAuthToken(token: string | null) {
  authToken = token
}

export const apiClient = axios.create({
  baseURL: '/api',
})

apiClient.interceptors.request.use((config) => {
  if (authToken) {
    config.headers.Authorization = `Bearer ${authToken}`
  }
  return config
})

export type UnauthorizedHandler = () => void
let onUnauthorized: UnauthorizedHandler | null = null

export function setUnauthorizedHandler(handler: UnauthorizedHandler | null) {
  onUnauthorized = handler
}

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401 && onUnauthorized) {
      onUnauthorized()
    }
    return Promise.reject(error)
  },
)
