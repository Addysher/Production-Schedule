import axios from 'axios'

// The token itself lives here only for the life of this module; AuthContext is what actually persists
// it (to sessionStorage, restored on load) across a reload. A module-level variable — rather than
// reading from React state on every request — keeps the interceptor synchronous and simple.
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
