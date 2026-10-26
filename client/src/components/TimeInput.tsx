// A 24-hour time picker: an hour list (00–23) and a minute list (5-minute steps). The browser's own
// time box follows the PC's regional setting, which shows AM/PM on many machines and is fiddly to set.
// Value is "HH:mm". A minute that isn't on a 5-minute step (e.g. set before) stays selectable.
const HOURS = Array.from({ length: 24 }, (_, h) => String(h).padStart(2, '0'))
const STEP_MINUTES = Array.from({ length: 12 }, (_, i) => String(i * 5).padStart(2, '0'))

export function TimeInput({
  value,
  onChange,
  label,
}: {
  value: string
  onChange: (value: string) => void
  label?: string // for screen readers, e.g. "Shift start"
}) {
  const [hh = '00', mm = '00'] = value.split(':')
  const minutes = STEP_MINUTES.includes(mm) ? STEP_MINUTES : [...STEP_MINUTES, mm].sort()

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
      <select value={hh} onChange={(e) => onChange(`${e.target.value}:${mm}`)} aria-label={label ? `${label} hour` : 'Hour'}>
        {HOURS.map((h) => (
          <option key={h} value={h}>
            {h}
          </option>
        ))}
      </select>
      <span style={{ fontWeight: 700 }}>:</span>
      <select value={mm} onChange={(e) => onChange(`${hh}:${e.target.value}`)} aria-label={label ? `${label} minute` : 'Minute'}>
        {minutes.map((m) => (
          <option key={m} value={m}>
            {m}
          </option>
        ))}
      </select>
    </span>
  )
}
