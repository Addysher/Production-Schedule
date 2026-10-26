import type { ScheduleItemDto, ScheduleSegmentDto } from '../types/api'
import { resolveStatusStyle } from './statusStyles'
import { CreepBadge } from './CreepBadge'
import { TbaTag } from './TbaTag'

export function LockIcon({ size = 11 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <rect x="4" y="11" width="16" height="10" rx="2" />
      <path d="M8 11V7a4 4 0 0 1 8 0v4" />
    </svg>
  )
}

const LINE = { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' } as const

export function CalendarBlock({
  item,
  segment,
  isLate,
  isAtRisk = false,
  onDoubleClick,
  compact,
}: {
  item: ScheduleItemDto
  segment: ScheduleSegmentDto
  isLate: boolean
  isAtRisk?: boolean
  onDoubleClick: () => void
  compact?: boolean
}) {
  const style = resolveStatusStyle(item.jobStatus, isLate)
  const start = new Date(segment.start)
  const end = new Date(segment.end)
  const time = (d: Date) => d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const eqa = Math.round(item.plannedEqA * 10) / 10

  return (
    <div
      onDoubleClick={onDoubleClick}
      title={`${item.jobRef} — ${item.componentType ?? ''} — ${item.clientName ?? 'no client'} — ${eqa} Eq
${time(start)}–${time(end)}`}
      style={{
        // Pencilled-in (TBA) work has its own colour and a dashed outline, whatever its status.
        background: item.isTba ? 'var(--pencil-block)' : style.block,
        borderLeft: item.isTba ? '3px dashed var(--pencil-border)' : `3px solid ${style.border}`,
        outline: item.isTba ? '1.5px dashed var(--pencil-border)' : undefined,
        outlineOffset: -1,
        color: item.isTba ? 'var(--pencil-text)' : style.text,
        borderRadius: 4,
        padding: compact ? '3px 6px' : '6px 8px',
        fontSize: 12,
        cursor: 'pointer',
        marginBottom: 3,
        overflow: 'hidden',
        height: compact ? undefined : '100%',
        boxSizing: 'border-box',
      }}
    >
      <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
        {isLate && item.jobStatus !== 'completed' && (
          <span style={{ fontSize: 10, fontWeight: 700, letterSpacing: 0.5 }}>LATE</span>
        )}
        {!isLate && isAtRisk && (
          <span
            title="Planned to finish within the at-risk buffer of its due date"
            style={{ fontSize: 10, fontWeight: 700, letterSpacing: 0.5, padding: '0 4px', borderRadius: 3, background: '#f59e0b', color: '#1f2937' }}
          >
            AT RISK
          </span>
        )}
        <span style={{ fontWeight: 700 }}>{item.jobRef}</span>
        {item.isTba && <TbaTag />}
        {item.creepDetected && <CreepBadge approved={!!item.creepApprovedAt} />}
        {item.isFrozen && (
          <span title="Inside the freeze window — can't be moved" style={{ display: 'inline-flex', opacity: 0.8 }}>
            <LockIcon />
          </span>
        )}
      </div>
      {/* The card: job reference (above), product, client and the job's EqA */}
      <div style={LINE}>
        {item.componentType ?? '—'} &middot; {eqa} Eq
      </div>
      <div style={{ ...LINE, opacity: 0.85 }}>{item.clientName ?? '—'}</div>
    </div>
  )
}
