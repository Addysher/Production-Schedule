import { useEffect, useState } from 'react'
import { factoriesApi } from '../api/admin'
import { stationsApi } from '../api/stations'
import type { CapacityOverrideDto, FactoryDto, StationDto } from '../types/api'
import { Field, Modal, errorText, fromTimeOnly, toTimeOnly } from './Modal'
import { TimeInput } from './TimeInput'
import { ProductSelect } from './ProductSelect'

// Create or edit a station (a line). Editing also manages its one-off day changes: overtime hours, or
// closed for a shutdown or public holiday (v3.0, §5.15).
export function StationModal({ station, onClose, onSaved }: { station?: StationDto; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    name: station?.name ?? '',
    componentType: station?.componentType ?? '',
    prdPath: station?.prdPath ?? '',
    mpsPath: station?.mpsPath ?? '',
    archivePath: station?.archivePath ?? '',
    eqaPerHour: station?.eqaPerHour?.toString() ?? '',
    displayOrder: station?.displayOrder?.toString() ?? '0',
    isActive: station?.isActive ?? true,
    factoryId: station?.factoryId?.toString() ?? '',
  })
  const [factories, setFactories] = useState<FactoryDto[]>([])

  useEffect(() => {
    factoriesApi
      .getAll()
      .then(setFactories)
      .catch(() => setFactories([]))
  }, [])
  const [overrides, setOverrides] = useState<CapacityOverrideDto[]>([])
  const [newChange, setNewChange] = useState({ date: '', kind: 'closed' as 'closed' | 'overtime', start: '06:00', end: '17:00', note: '', allLines: false })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const loadOverrides = () => {
    if (station)
      stationsApi
        .getCapacityOverrides(station.id)
        .then(setOverrides)
        .catch((e) => setError(errorText(e, 'Could not load the day changes.')))
  }
  useEffect(() => {
    loadOverrides()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [station?.id])

  async function save() {
    if (!form.name.trim() || !form.componentType.trim()) return setError('Name and product are required.')
    setSaving(true)
    setError(null)
    const body = {
      name: form.name.trim(),
      componentType: form.componentType.trim(),
      prdPath: form.prdPath.trim() || null,
      mpsPath: form.mpsPath.trim() || null,
      archivePath: form.archivePath.trim() || null,
      eqaPerHour: form.eqaPerHour === '' ? null : Number(form.eqaPerHour),
      displayOrder: Number(form.displayOrder) || 0,
      factoryId: form.factoryId === '' ? null : Number(form.factoryId),
    }
    try {
      if (station) await stationsApi.update(station.id, { ...body, isActive: form.isActive })
      else await stationsApi.create(body)
      onSaved()
    } catch (e) {
      setError(errorText(e, 'Could not save the station.'))
    } finally {
      setSaving(false)
    }
  }

  async function addChange() {
    if (!station || !newChange.date) return
    setError(null)
    const change = {
      overrideDate: newChange.date,
      isClosed: newChange.kind === 'closed',
      startTime: newChange.kind === 'overtime' ? toTimeOnly(newChange.start) : null,
      endTime: newChange.kind === 'overtime' ? toTimeOnly(newChange.end) : null,
      note: newChange.note.trim() || null,
    }
    try {
      if (newChange.allLines) await stationsApi.createForAllLines(change)
      else await stationsApi.createCapacityOverride(station.id, change)
      setNewChange({ ...newChange, date: '', note: '' })
      loadOverrides()
    } catch (e) {
      setError(errorText(e, 'Could not add the day change.'))
    }
  }

  const describe = (o: CapacityOverrideDto) =>
    o.isClosed
      ? 'Closed'
      : `Overtime ${o.startTime ? fromTimeOnly(o.startTime) : 'normal start'}–${o.endTime ? (fromTimeOnly(o.endTime) === '00:00' ? '24:00' : fromTimeOnly(o.endTime)) : 'normal end'}`

  async function removeOverride(id: number) {
    if (!station) return
    setError(null)
    try {
      await stationsApi.deleteCapacityOverride(station.id, id)
    } catch (e) {
      setError(errorText(e, 'Could not remove the day change.'))
    }
    loadOverrides()
  }

  const set = (patch: Partial<typeof form>) => setForm({ ...form, ...patch })

  return (
    <Modal
      title={station ? `Edit ${station.name}` : 'Add station'}
      width={520}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={saving}>
            {saving ? 'Saving\u2026' : 'Save'}
          </button>
        </>
      }
    >
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
        <Field label="Name">
          <input style={{ width: '100%' }} value={form.name} onChange={(e) => set({ name: e.target.value })} autoFocus />
        </Field>
        <Field label="Product" hint="Jobs of this product can run here (Settings → Products)">
          <ProductSelect value={form.componentType} onChange={(componentType) => set({ componentType })} />
        </Field>
        <div style={{ gridColumn: '1 / -1' }}>
          <Field label="PRD folder (DataOut)" hint="Where PaperlessJig writes this station's daily .prd files; production progress is read from here">
            <input style={{ width: '100%' }} value={form.prdPath} onChange={(e) => set({ prdPath: e.target.value })} placeholder="\\server\share\Station\DataOut" />
          </Field>
        </div>
        <div style={{ gridColumn: '1 / -1' }}>
          <Field label="MPS folder (DataIn)" hint="MPS files are moved here from the product pool before production. Blank = MPS files aren't sent. Files already here stay put if you change it">
            <input style={{ width: '100%' }} value={form.mpsPath} onChange={(e) => set({ mpsPath: e.target.value })} placeholder="\\server\share\Station\DataIn" />
          </Field>
        </div>
        <div style={{ gridColumn: '1 / -1' }}>
          <Field label="Archive folder" hint="MPS files of completed jobs (at the end of the shift) and of deleted jobs are moved here">
            <input style={{ width: '100%' }} value={form.archivePath} onChange={(e) => set({ archivePath: e.target.value })} placeholder="\\server\share\Station\Archive" />
          </Field>
        </div>
        <Field label="Capacity (EqA per hour)" hint={`What the line makes per working hour; sets how long jobs take${station?.todayTargetEqa != null ? ` (today's target: ${station.todayTargetEqa} Eq)` : ''}`}>
          <input type="number" step="0.1" min="0" style={{ width: '100%' }} value={form.eqaPerHour} onChange={(e) => set({ eqaPerHour: e.target.value })} />
        </Field>
        <Field label="Factory" hint="Which factory's kiosk display shows this station">
          <select style={{ width: '100%' }} value={form.factoryId} onChange={(e) => set({ factoryId: e.target.value })}>
            <option value="">(none)</option>
            {factories
              .filter((f) => f.isActive || String(f.id) === form.factoryId)
              .map((f) => (
                <option key={f.id} value={String(f.id)}>
                  {f.name}
                </option>
              ))}
          </select>
        </Field>
        <Field label="Display order">
          <input type="number" style={{ width: '100%' }} value={form.displayOrder} onChange={(e) => set({ displayOrder: e.target.value })} />
        </Field>
        {station && (
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, gridColumn: '1 / -1' }}>
            <input type="checkbox" checked={form.isActive} onChange={(e) => set({ isActive: e.target.checked })} />
            <span style={{ fontSize: 13 }}>Active</span>
          </label>
        )}
      </div>

      {station && (
        <div style={{ marginTop: 20, borderTop: '1px solid var(--border-subtle)', paddingTop: 14 }}>
          <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 4 }}>Day changes</div>
          <div style={{ fontSize: 11, color: 'var(--text-faint)', marginBottom: 8 }}>
            One-off changes to this line's day: overtime (different hours, which can also open a weekend day) or closed (shutdown, public holiday). The schedule re-plans when you add or remove one.
          </div>
          {overrides.map((o) => (
            <div key={o.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, marginBottom: 4 }}>
              <span style={{ width: 110 }}>{o.overrideDate.slice(0, 10)}</span>
              <span style={{ flex: 1 }}>
                {describe(o)}
                {o.note ? ` — ${o.note}` : ''}
              </span>
              <button className="btn btn-danger" style={{ padding: '1px 8px', fontSize: 11 }} onClick={() => removeOverride(o.id)}>
                Remove
              </button>
            </div>
          ))}
          {overrides.length === 0 && <div style={{ fontSize: 12, color: 'var(--text-faint)', marginBottom: 6 }}>None</div>}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginTop: 8, padding: 10, border: '1px solid var(--border-subtle)', borderRadius: 6 }}>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <input type="date" value={newChange.date} onChange={(e) => setNewChange({ ...newChange, date: e.target.value })} aria-label="Day" />
              <select value={newChange.kind} onChange={(e) => setNewChange({ ...newChange, kind: e.target.value as 'closed' | 'overtime' })} aria-label="Change">
                <option value="closed">Closed</option>
                <option value="overtime">Overtime hours</option>
              </select>
              {newChange.kind === 'overtime' && (
                <>
                  <TimeInput value={newChange.start} onChange={(start) => setNewChange({ ...newChange, start })} label="Overtime start" />
                  <span>to</span>
                  <TimeInput value={newChange.end} onChange={(end) => setNewChange({ ...newChange, end })} label="Overtime end" />
                </>
              )}
            </div>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <input placeholder="Note (e.g. Easter Monday)" style={{ flex: 1, minWidth: 160 }} value={newChange.note} onChange={(e) => setNewChange({ ...newChange, note: e.target.value })} />
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12 }}>
                <input type="checkbox" checked={newChange.allLines} onChange={(e) => setNewChange({ ...newChange, allLines: e.target.checked })} />
                All lines (public holiday / site shutdown)
              </label>
              <button className="btn btn-secondary" onClick={addChange} disabled={!newChange.date}>
                Add
              </button>
            </div>
          </div>
        </div>
      )}

      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}
