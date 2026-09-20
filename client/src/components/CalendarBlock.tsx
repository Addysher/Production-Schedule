import type { ScheduleItemDto, ScheduleSegmentDto } from '../types/api'
import { resolveStatusStyle } from './statusStyles'
import { CreepBadge } from './CreepBadge'

export function CalendarBlock({
  item,
  segment,
  isLate,
  onDoubleClick,
  compact,
}: {
  item: ScheduleItemDto
  segment: ScheduleSegmentDto
  isLate: boolean
  onDoubleClick: () => void
  compact?: boolean
}) {
  const style = resolveStatusStyle(item.jobStatus, isLate)
  const start = new Date(segment.start)
  const end = new Date(segment.end)
  const timeLabel = start.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  const durationMins = Math.round((end.getTime() - start.getTime()) / 60000)

  // A job spread over several days is drawn once per day; say which day of how many this is.
  const days = Array.from(new Set(item.segments.map((s) => s.date.slice(0, 10)))).sort()
  const dayNumber = days.indexOf(segment.date.slice(0, 10)) + 1

  return (
    <div
      onDoubleClick={onDoubleClick}
      title={`${item.jobRef} — ${item.componentType ?? ''}${days.length > 1 ? ` (day ${dayNumber} of ${days.length})` : ''}`}
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
        height: compact ? undefined : '100%',
        boxSizing: 'border-box',
      }}
    >
      <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
        {isLate && item.jobStatus !== 'completed' && (
          <span style={{ fontSize: 10, fontWeight: 700, letterSpacing: 0.5 }}>LATE</span>
        )}
        <span style={{ fontWeight: 700 }}>{item.jobRef}</span>
        {days.length > 1 && (
          <span style={{ fontSize: 10, opacity: 0.75 }}>
            day {dayNumber}/{days.length}
          </span>
        )}
        {item.creepDetected && <CreepBadge approved={!!item.creepApprovedAt} />}
      </div>
      {!compact && <div>{item.componentType}</div>}
      <div style={{ opacity: 0.8 }}>
        {timeLabel} &middot; {durationMins}m
      </div>
    </div>
  )
}
