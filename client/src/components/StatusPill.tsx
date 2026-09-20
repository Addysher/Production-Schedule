import type { JobStatus } from '../types/api'
import { resolveStatusStyle } from './statusStyles'

export function StatusPill({ status, isLate }: { status: JobStatus; isLate: boolean }) {
  const style = resolveStatusStyle(status, isLate)
  return (
    <span
      style={{
        display: 'inline-block',
        padding: '2px 10px',
        borderRadius: 999,
        fontSize: 12,
        fontWeight: 600,
        background: style.badgeBg,
        color: style.badgeText,
        whiteSpace: 'nowrap',
      }}
    >
      {style.label}
    </span>
  )
}
