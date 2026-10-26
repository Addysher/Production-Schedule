import type { LineProgressDto } from '../types/api'

// The feedback loop on a line (v3.0): what today's plan had made by now against what the PRD files say was
// made. Behind by more than 10% of the plan shows red.
export function LineProgress({ progress, large = false }: { progress: LineProgressDto | undefined; large?: boolean }) {
  if (!progress || (progress.plannedByNow === 0 && progress.madeToday === 0)) return null
  const gap = progress.madeToday - progress.plannedByNow
  const behind = gap < 0 && -gap > Math.max(0.5, progress.plannedByNow * 0.1)
  const fontSize = large ? 16 : 12
  return (
    <div style={{ fontSize, marginTop: 4, color: 'var(--text-muted)' }} data-testid={`line-progress-${progress.stationId}`}>
      Plan by now {progress.plannedByNow.toFixed(1)} · made {progress.madeToday.toFixed(1)}{' '}
      <span style={{ fontWeight: 700, color: behind ? '#dc2626' : '#16a34a' }}>
        {behind ? `${(-gap).toFixed(1)} Eq behind` : gap >= 0.05 ? `${gap.toFixed(1)} Eq ahead` : 'on plan'}
      </span>
    </div>
  )
}
