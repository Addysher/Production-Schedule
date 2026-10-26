import { useEffect, useState } from 'react'
import { factoriesApi } from '../api/admin'
import { stationsApi } from '../api/stations'
import type { FactoryDto, StationDto } from '../types/api'
import { Field, Modal, errorText } from './Modal'
import { ProductSelect } from './ProductSelect'

// Create or edit a station (a line). Its one-off day changes (overtime / closed) are set on the Calendar.
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
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

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

      <div style={{ fontSize: 11, color: 'var(--text-faint)', marginTop: 14 }}>
        Overtime and closed days (shutdowns, public holidays) are set per line on the Calendar: use the chip on the line's day.
      </div>

      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}
