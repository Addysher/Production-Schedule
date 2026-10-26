import type { JobStatus } from '../types/api'
import { resolveStatusStyle } from './statusStyles'
import { statusInfo } from './jobStatusInfo'

export function StatusPill({ status, isLate }: { status: JobStatus; isLate: boolean }) {
  const style = resolveStatusStyle(status, isLate)
  const info = statusInfo(status)
  return (
    <span
      title={info ? `${isLate && status !== 'completed' ? `Late — due date passed. Status: ${resolveStatusStyle(status, false).label}. ` : ''}${info.meaning}
Next: ${info.next}` : undefined}
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
