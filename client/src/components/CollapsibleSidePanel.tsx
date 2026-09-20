import { useState, type ReactNode } from 'react'

function storageKey(viewKey: string) {
  return `sidePanel.${viewKey}.open`
}

export function CollapsibleSidePanel({ viewKey, title, children }: { viewKey: string; title: string; children: ReactNode }) {
  const [open, setOpen] = useState(() => {
    try {
      return localStorage.getItem(storageKey(viewKey)) === 'true'
    } catch {
      return false
    }
  })

  function toggle() {
    const next = !open
    setOpen(next)
    try {
      localStorage.setItem(storageKey(viewKey), String(next))
    } catch {
      // localStorage unavailable — panel state just won't persist this session
    }
  }

  return (
    <div style={{ display: 'flex', flexShrink: 0 }}>
      <button
        onClick={toggle}
        className="btn btn-secondary"
        style={{
          writingMode: 'vertical-rl',
          borderRadius: '6px 0 0 6px',
          padding: '12px 6px',
          alignSelf: 'flex-start',
        }}
      >
        {open ? '›' : '‹'} {title}
      </button>
      {open && (
        <div className="card" style={{ width: 280, borderRadius: '0 8px 8px 0', flexShrink: 0 }}>
          {children}
        </div>
      )}
    </div>
  )
}
