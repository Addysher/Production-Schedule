import type { ScheduleItemDto } from '../types/api'
import { resolveStatusStyle } from './statusStyles'
import { CreepBadge } from './CreepBadge'

export function CalendarBlock({
  item,
  isLate,
  onDoubleClick,
  compact,
}: {
  item: ScheduleItemDto
  isLate: boolean
  onDoubleClick: () => void
  compact?: boolean
}) {
  const style = resolveStatusStyle(item.jobStatus, isLate)
  const start = new Date(item.plannedStart)
  const end = new Date(item.plannedEnd)
  const timeLabel = start.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const durationMins = Math.round((end.getTime() - start.getTime()) / 60000)

  return (
    <div
      onDoubleClick={onDoubleClick}
      title={`${item.jobRef} — ${item.componentType ?? ''}`}
      style={{
        background: style.block,
        borderLeft: `3px solid ${style.border}`,
        color: style.text,
        borderRadius: 4,
        padding: compact ? '3px 6px' : '6px 8px',
        fontSize: 12,
        cursor: 'pointer',
        marginBottom: 3,
        overflow: 'hidden',
      }}
    >
      <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
        {isLate && item.jobStatus !== 'completed' && (
          <span style={{ fontSize: 10, fontWeight: 700, letterSpacing: 0.5 }}>LATE</span>
        )}
        <span style={{ fontWeight: 700 }}>{item.jobRef}</span>
        {item.creepDetected && <CreepBadge approved={!!item.creepApprovedAt} />}
      </div>
      {!compact && <div>{item.componentType}</div>}
      <div style={{ opacity: 0.8 }}>
        {timeLabel} &middot; {durationMins}m
      </div>
    </div>
  )
}
