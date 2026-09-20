import { useState } from 'react'
import { stationsApi } from '../api/stations'

export function AddStationModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [name, setName] = useState('')
  const [componentType, setComponentType] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function handleCreate() {
    if (!name.trim() || !componentType.trim()) {
      setError('Name and component type are required.')
      return
    }
    setSaving(true)
    try {
      await stationsApi.create({ name, componentType, uncPath: null, displayOrder: 0, dailyEqATarget: null, archivePath: null })
      onCreated()
    } catch {
      setError('Could not create station.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      onClick={onClose}
      style={{ position: 'fixed', inset: 0, background: 'rgba(15,23,42,0.45)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 60 }}
    >
      <div onClick={(e) => e.stopPropagation()} className="card" style={{ width: 360 }}>
        <div style={{ fontSize: 16, fontWeight: 700, marginBottom: 16 }}>Add station</div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Name</div>
            <input style={{ width: '100%' }} value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </label>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Component Type</div>
            <input style={{ width: '100%' }} value={componentType} onChange={(e) => setComponentType(e.target.value)} placeholder="e.g. WallFrames" />
          </label>
        </div>
        {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 8 }}>{error}</div>}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>
          <button className="btn btn-secondary" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={handleCreate} disabled={saving}>
            {saving ? 'Creating…' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  )
}
