import type { ReactNode } from 'react'

// Shared overlay + card for the settings/edit dialogs. Clicking outside closes it.
export function Modal({
  title,
  onClose,
  width = 440,
  children,
  footer,
}: {
  title: string
  onClose: () => void
  width?: number
  children: ReactNode
  footer?: ReactNode
}) {
  return (
    <div
      onClick={onClose}
      style={{ position: 'fixed', inset: 0, background: 'rgba(15,23,42,0.45)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 60 }}
    >
      <div onClick={(e) => e.stopPropagation()} className="card" style={{ width, maxHeight: '88vh', overflow: 'auto' }}>
        <div style={{ fontSize: 16, fontWeight: 700, marginBottom: 16 }}>{title}</div>
        {children}
        {footer && <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>{footer}</div>}
      </div>
    </div>
  )
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return (
    <label style={{ display: 'block' }}>
      <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>{label}</div>
      {children}
      {hint && <div style={{ fontSize: 11, color: 'var(--text-faint)', marginTop: 3 }}>{hint}</div>}
    </label>
  )
}

// The server's TimeOnly JSON expects HH:mm:ss; <input type="time"> yields HH:mm.
export const toTimeOnly = (value: string) => (value.length === 5 ? `${value}:00` : value)
export const fromTimeOnly = (value: string) => value.slice(0, 5)

// The API answers a refused request with a plain sentence; model-binding failures come back as
// ProblemDetails instead, whose first field error is the useful part.
export function errorText(e: unknown, fallback: string): string {
  const data = (e as { response?: { data?: unknown } }).response?.data
  if (typeof data === 'string') return data.length > 0 && data.length < 300 ? data : fallback
  const problem = data as { title?: string; errors?: Record<string, string[]> } | undefined
  const first = problem?.errors ? Object.values(problem.errors).flat()[0] : undefined
  return first ?? problem?.title ?? fallback
}
