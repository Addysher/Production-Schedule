import { useState } from 'react'
import { stationsApi, type DayChange } from '../api/stations'
import type { CapacityOverrideDto, StationDto } from '../types/api'
import { localIsoDate } from '../utils/dates'
import { Field, Modal, errorText, fromTimeOnly, toTimeOnly } from './Modal'
import { TimeInput } from './TimeInput'

type Kind = 'normal' | 'overtime' | 'closed'

// A one-off change to one line's day, set from the Calendar (v3.0): overtime hours (which can also open a
// normally non-working day) or closed (shutdown, public holiday — optionally for every line at once).
// Saving re-plans the schedule.
export function DayChangeModal({
  station,
  date,
  existing,
  dayStart,
  dayEnd,
  onClose,
  onSaved,
}: {
  station: StationDto
  date: Date
  existing: CapacityOverrideDto | undefined
  dayStart: string // the normal day, "HH:mm"
  dayEnd: string
  onClose: () => void
  onSaved: () => void
}) {
  const [kind, setKind] = useState<Kind>(existing ? (existing.isClosed ? 'closed' : 'overtime') : 'overtime')
  const [start, setStart] = useState(existing?.startTime ? fromTimeOnly(existing.startTime) : dayStart)
  const [end, setEnd] = useState(existing?.endTime ? fromTimeOnly(existing.endTime) : dayEnd === '24:00' ? '00:00' : dayEnd)
  const [note, setNote] = useState(existing?.note ?? '')
  const [allLines, setAllLines] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const day = localIsoDate(date)

  async function save() {
    setSaving(true)
    setError(null)
    try {
      if (kind === 'normal') {
        if (existing) await stationsApi.deleteCapacityOverride(station.id, existing.id)
      } else {
        const change: DayChange = {
          overrideDate: day,
          isClosed: kind === 'closed',
          startTime: kind === 'overtime' ? toTimeOnly(start) : null,
          endTime: kind === 'overtime' ? toTimeOnly(end) : null,
          note: note.trim() || null,
        }
        if (allLines) await stationsApi.createForAllLines(change)
        else if (existing) await stationsApi.updateCapacityOverride(station.id, existing.id, change)
        else await stationsApi.createCapacityOverride(station.id, change)
      }
      onSaved()
    } catch (e) {
      setError(errorText(e, 'Could not save the day change.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      title={`${station.name} — ${date.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' })}`}
      onClose={onClose}
      width={460}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', gap: 14, fontSize: 13 }}>
          {(
            [
              ['normal', 'Normal day'],
              ['overtime', 'Overtime hours'],
              ['closed', 'Closed'],
            ] as const
          ).map(([value, label]) => (
            <label key={value} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <input type="radio" name="day-change" checked={kind === value} onChange={() => setKind(value)} />
              {label}
            </label>
          ))}
        </div>
        {kind === 'overtime' && (
          <Field label="This line works" hint={`The normal day is ${dayStart}–${dayEnd}. Breaks still apply.`}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <TimeInput value={start} onChange={setStart} label="Overtime start" />
              <span>to</span>
              <TimeInput value={end} onChange={setEnd} label="Overtime end" />
            </div>
          </Field>
        )}
        {kind !== 'normal' && (
          <>
            <Field label="Note">
              <input style={{ width: '100%' }} value={note} onChange={(e) => setNote(e.target.value)} placeholder={kind === 'closed' ? 'e.g. Easter Monday, maintenance' : 'e.g. Catch-up'} />
            </Field>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
              <input type="checkbox" checked={allLines} onChange={(e) => setAllLines(e.target.checked)} />
              Apply to all lines (public holiday / site shutdown)
            </label>
          </>
        )}
        {kind === 'normal' && <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>{existing ? 'Saving removes this line\'s change for the day.' : 'No change for this day.'}</div>}
        <div style={{ fontSize: 11, color: 'var(--text-faint)' }}>Saving re-plans the schedule.</div>
      </div>
      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}

// How a change reads in a Calendar cell.
export function describeDayChange(c: CapacityOverrideDto): string {
  if (c.isClosed) return `Closed${c.note ? ` — ${c.note}` : ''}`
  const s = c.startTime ? fromTimeOnly(c.startTime) : 'start'
  const e = c.endTime ? (fromTimeOnly(c.endTime) === '00:00' ? '24:00' : fromTimeOnly(c.endTime)) : 'end'
  return `OT ${s}–${e}${c.note ? ` — ${c.note}` : ''}`
}
