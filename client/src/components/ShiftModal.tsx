import { useState } from 'react'
import { shiftsApi } from '../api/admin'
import type { ShiftDto } from '../types/api'
import { Field, Modal, errorText, fromTimeOnly, toTimeOnly } from './Modal'
import { TimeInput } from './TimeInput'

interface BreakRow {
  key: number
  start: string
  mins: string
}

let nextKey = 1

// Create or edit a shift, including its break windows (§8.6 — the scheduler never works through a break).
export function ShiftModal({ shift, onClose, onSaved }: { shift?: ShiftDto; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(shift?.name ?? '')
  const [startTime, setStartTime] = useState(fromTimeOnly(shift?.startTime ?? '06:00'))
  const [endTime, setEndTime] = useState(fromTimeOnly(shift?.endTime ?? '18:00'))
  const [isActive, setIsActive] = useState(shift?.isActive ?? true)
  const [breaks, setBreaks] = useState<BreakRow[]>(
    (shift?.breaks ?? []).map((b) => ({ key: nextKey++, start: fromTimeOnly(b.breakStart), mins: String(b.breakDurationMins) })),
  )
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const updateBreak = (key: number, patch: Partial<BreakRow>) => setBreaks(breaks.map((b) => (b.key === key ? { ...b, ...patch } : b)))

  async function save() {
    if (!name.trim()) return setError('Give the shift a name.')
    if (breaks.some((b) => !b.start || !(Number(b.mins) > 0))) return setError('Every break needs a start time and a duration in minutes.')
    const toMins = (t: string) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5))
    const start = toMins(startTime)
    const end = endTime === '00:00' ? 24 * 60 : toMins(endTime)
    if (end <= start) return setError('The shift must end after it starts (shifts past midnight are not supported).')
    const outside = breaks.find((b) => toMins(b.start) < start || toMins(b.start) + Number(b.mins) > end)
    if (outside) return setError(`The break at ${outside.start} is outside the shift (${startTime} to ${endTime === '00:00' ? '24:00' : endTime}).`)
    setSaving(true)
    setError(null)
    const body = {
      name: name.trim(),
      startTime: toTimeOnly(startTime),
      endTime: toTimeOnly(endTime),
      isActive,
      breaks: breaks.map((b) => ({ breakStart: toTimeOnly(b.start), breakDurationMins: Number(b.mins) })),
    }
    try {
      if (shift) await shiftsApi.update(shift.id, body)
      else await shiftsApi.create(body)
      onSaved()
    } catch (e) {
      setError(errorText(e, 'Could not save the shift.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      title={shift ? `Edit ${shift.name}` : 'Add shift'}
      onClose={onClose}
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
        <Field label="Name">
          <input style={{ width: '100%' }} value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </Field>
        <div style={{ display: 'flex', gap: 12 }}>
          <Field label="Start">
            <TimeInput value={startTime} onChange={setStartTime} label="Shift start" />
          </Field>
          <Field label="End">
            <TimeInput value={endTime} onChange={setEndTime} label="Shift end" midnightLabel="24 (midnight)" />
          </Field>
        </div>
        <div style={{ fontSize: 11, color: 'var(--text-faint)' }}>
          24-hour times. The working day runs from the earliest active shift's start to the latest one's end; breaks must fall inside the shift. Saving re-plans the schedule.
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          <span style={{ fontSize: 13 }}>Active</span>
        </label>

        <div>
          <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 6 }}>Breaks</div>
          {breaks.map((b) => (
            <div key={b.key} style={{ display: 'flex', gap: 8, alignItems: 'center', marginBottom: 6 }}>
              <TimeInput value={b.start} onChange={(start) => updateBreak(b.key, { start })} label="Break start" />
              <input type="number" min={1} style={{ width: 80 }} value={b.mins} onChange={(e) => updateBreak(b.key, { mins: e.target.value })} />
              <span style={{ fontSize: 12, color: 'var(--text-muted)' }}>minutes</span>
              <button className="btn btn-danger" style={{ padding: '1px 8px' }} onClick={() => setBreaks(breaks.filter((x) => x.key !== b.key))}>
                &times;
              </button>
            </div>
          ))}
          <button className="btn btn-secondary" style={{ padding: '3px 10px', fontSize: 12 }} onClick={() => setBreaks([...breaks, { key: nextKey++, start: '12:00', mins: '30' }])}>
            + Add break
          </button>
        </div>
      </div>
      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}
