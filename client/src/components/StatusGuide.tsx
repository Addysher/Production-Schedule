import { FLAG_GUIDE, STATUS_GUIDE } from './jobStatusInfo'
import { resolveStatusStyle } from './statusStyles'

// The job lifecycle at a glance: each status in order, what puts a job there and what moves it on.
export function StatusGuide() {
  return (
    <div className="card" style={{ marginBottom: 16, fontSize: 13 }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(190px, 1fr))', gap: 10 }}>
        {STATUS_GUIDE.map((s, i) => {
          const style = resolveStatusStyle(s.status, false)
          return (
            <div key={s.status} style={{ borderLeft: `3px solid ${style.border}`, background: style.block, color: style.text, borderRadius: 4, padding: '8px 10px' }}>
              <div style={{ fontWeight: 800, marginBottom: 4 }}>
                {i + 1}. {style.label}
              </div>
              <div style={{ marginBottom: 6 }}>{s.meaning}</div>
              <div style={{ fontSize: 12 }}>
                <b>Set by:</b> {s.setBy}
              </div>
              <div style={{ fontSize: 12, marginTop: 2 }}>
                <b>Next:</b> {s.next}
              </div>
            </div>
          )
        })}
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px 18px', marginTop: 12, fontSize: 12, color: 'var(--text-muted)' }}>
        {FLAG_GUIDE.map((f) => (
          <span key={f.flag}>
            <b style={{ color: 'var(--text-primary)' }}>{f.flag}:</b> {f.meaning}
          </span>
        ))}
      </div>
    </div>
  )
}
