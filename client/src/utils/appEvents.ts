// Tiny in-tab signals between parts of the shell that don't share state: a view saying "jobs changed"
// (so the sidebar's late count follows), and the header asking a side panel to open.
const target = new EventTarget()

const JOBS_CHANGED = 'jobs-changed'
const OPEN_PANEL = 'open-panel'

export function signalJobsChanged() {
  target.dispatchEvent(new Event(JOBS_CHANGED))
}

export function onJobsChanged(handler: () => void): () => void {
  target.addEventListener(JOBS_CHANGED, handler)
  return () => target.removeEventListener(JOBS_CHANGED, handler)
}

export function requestPanelOpen(viewKey: string) {
  target.dispatchEvent(new CustomEvent(OPEN_PANEL, { detail: viewKey }))
}

export function onPanelOpenRequested(viewKey: string, handler: () => void): () => void {
  const listener = (e: Event) => {
    if ((e as CustomEvent<string>).detail === viewKey) handler()
  }
  target.addEventListener(OPEN_PANEL, listener)
  return () => target.removeEventListener(OPEN_PANEL, listener)
}

// Re-runs `load` every `ms` while the tab is visible, and straight away when it becomes visible again.
export function startAutoRefresh(load: () => void, ms = 60000): () => void {
  const tick = () => {
    if (document.visibilityState === 'visible') load()
  }
  const timer = setInterval(tick, ms)
  document.addEventListener('visibilitychange', tick)
  return () => {
    clearInterval(timer)
    document.removeEventListener('visibilitychange', tick)
  }
}
