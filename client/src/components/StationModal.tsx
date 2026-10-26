import { useEffect, useState } from 'react'
import { stationsApi } from '../api/stations'
import type { CapacityOverrideDto, StationDto } from '../types/api'
import { Field, Modal, errorText } from './Modal'
import { ProductSelect } from './ProductSelect'

// Create or edit a station. Editing also manages its per-day capacity overrides (§3.4 / §5.15).
export function StationModal({ station, onClose, onSaved }: { station?: StationDto; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    name: station?.name ?? '',
    componentType: station?.componentType ?? '',
    uncPath: station?.uncPath ?? '',
    archivePath: station?.archivePath ?? '',
    dailyEqATarget: station?.dailyEqATarget?.toString() ?? '',
    displayOrder: station?.displayOrder?.toString() ?? '0',
    isActive: station?.isActive ?? true,
  })
  const [overrides, setOverrides] = useState<CapacityOverrideDto[]>([])
  const [newOverride, setNewOverride] = useState({ date: '', target: '' })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const loadOverrides = () => {
    if (station)
      stationsApi
        .getCapacityOverrides(station.id)
        .then(setOverrides)
        .catch((e) => setError(errorText(e, 'Could not load the capacity overrides.')))
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
      uncPath: form.uncPath.trim() || null,
      archivePath: form.archivePath.trim() || null,
      dailyEqATarget: form.dailyEqATarget === '' ? null : Number(form.dailyEqATarget),
      displayOrder: Number(form.displayOrder) || 0,
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

  async function addOverride() {
    if (!station || !newOverride.date || newOverride.target === '') return
    setError(null)
    try {
      await stationsApi.createCapacityOverride(station.id, newOverride.date, Number(newOverride.target))
      setNewOverride({ date: '', target: '' })
      loadOverrides()
    } catch (e) {
      setError(errorText(e, 'Could not add the override (only one per day).'))
    }
  }

  async function removeOverride(id: number) {
    if (!station) return
    setError(null)
    try {
      await stationsApi.deleteCapacityOverride(station.id, id)
    } catch (e) {
      setError(errorText(e, 'Could not remove the override.'))
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
          <Field label="PRD folder (UNC path)" hint="Where PaperlessJig writes this station's daily .prd files; also where MPS files are staged">
            <input style={{ width: '100%' }} value={form.uncPath} onChange={(e) => set({ uncPath: e.target.value })} placeholder="\\server\share\Station\DataIn" />
          </Field>
        </div>
        <div style={{ gridColumn: '1 / -1' }}>
          <Field label="Archive folder" hint="Finished jobs' MPS files are moved here overnight (nightly Task 4)">
            <input style={{ width: '100%' }} value={form.archivePath} onChange={(e) => set({ archivePath: e.target.value })} placeholder="\\server\share\Station\Archive" />
          </Field>
        </div>
        <Field label="Daily EqA target" hint="Blank = no daily limit">
          <input type="number" style={{ width: '100%' }} value={form.dailyEqATarget} onChange={(e) => set({ dailyEqATarget: e.target.value })} />
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
          <div style={{ fontWeight: 700, fontSize: 13, marginBottom: 4 }}>Capacity overrides</div>
          <div style={{ fontSize: 11, color: 'var(--text-faint)', marginBottom: 8 }}>
            A one-off EqA target for a single day, replacing the daily target (e.g. overtime, or a machine down). Applied immediately; re-run the schedule to see the effect.
          </div>
          {overrides.map((o) => (
            <div key={o.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, marginBottom: 4 }}>
              <span style={{ width: 110 }}>{o.overrideDate.slice(0, 10)}</span>
              <span style={{ flex: 1 }}>{o.eqATarget} Eq</span>
              <button className="btn btn-danger" style={{ padding: '1px 8px', fontSize: 11 }} onClick={() => removeOverride(o.id)}>
                Remove
              </button>
            </div>
          ))}
          {overrides.length === 0 && <div style={{ fontSize: 12, color: 'var(--text-faint)', marginBottom: 6 }}>None</div>}
          <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            <input type="date" value={newOverride.date} onChange={(e) => setNewOverride({ ...newOverride, date: e.target.value })} />
            <input type="number" placeholder="Eq target" style={{ width: 100 }} value={newOverride.target} onChange={(e) => setNewOverride({ ...newOverride, target: e.target.value })} />
            <button className="btn btn-secondary" onClick={addOverride} disabled={!newOverride.date || newOverride.target === ''}>
              Add
            </button>
          </div>
        </div>
      )}

      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}
