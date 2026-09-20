export function CreepBadge({ approved }: { approved?: boolean }) {
  return (
    <span
      title={approved ? 'Creep detected — approved for rescheduling' : 'Creep detected — awaiting approval'}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 4,
        fontSize: 11,
        fontWeight: 700,
        padding: '1px 8px',
        borderRadius: 999,
        background: approved ? '#e0f2fe' : 'rgba(245, 158, 11, 0.15)',
        color: approved ? '#075985' : '#92400e',
        border: `1px solid ${approved ? '#7dd3fc' : '#f59e0b'}`,
      }}
    >
      {approved ? '✓' : '⚠'} CREEP
    </span>
  )
}
