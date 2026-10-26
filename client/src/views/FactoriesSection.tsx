import { useEffect, useState } from 'react'
import { factoriesApi } from '../api/admin'
import { stationsApi } from '../api/stations'
import { Field, Modal, errorText } from '../components/Modal'
import type { FactoryDto, StationDto } from '../types/api'

// Settings → Factories (v3.0). A factory groups stations for its kiosk display; which stations belong
// to it is set on each station (Settings → Stations). Scheduling ignores factories.
// Same pattern as the other tabs: "+ Add factory" and Edit open a form; nothing saves until Save.
export function FactoriesSection() {
  const [rows, setRows] = useState<FactoryDto[]>([])
  const [stations, setStations] = useState<StationDto[]>([])
  const [editing, setEditing] = useState<FactoryDto | 'new' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  async function load() {
    try {
      const [factories, stationList] = await Promise.all([factoriesApi.getAll(), stationsApi.getAll()])
      setRows(factories)
      setStations(stationList)
    } catch (e) {
      setError(errorText(e, 'Could not load the factories.'))
    }
  }

  useEffect(() => {
    load()
  }, [])

  async function remove(f: FactoryDto) {
    if (!confirm(`Delete the factory '${f.name}'? Its stations stay, just unassigned.`)) return
    setError(null)
    try {
      await factoriesApi.remove(f.id)
      setNotice(`Deleted '${f.name}'.`)
      await load()
    } catch (e) {
      setError(errorText(e, `Could not delete '${f.name}'.`))
    }
  }

  const stationNames = (f: FactoryDto) =>
    f.stationIds.map((id) => stations.find((s) => s.id === id)?.name).filter(Boolean).join(', ') || '—'

  return (
    <div className="card">
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12, gap: 12 }}>
        <div style={{ fontSize: 13 }}>
          {notice && <span style={{ color: '#16a34a' }}>{notice}</span>}
          {error && <span style={{ color: '#dc2626' }}>{error}</span>}
        </div>
        <button
          className="btn btn-primary"
          onClick={() => {
            setNotice(null)
            setEditing('new')
          }}
        >
          + Add factory
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Stations</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((f) => (
            <tr key={f.id} style={{ opacity: f.isActive ? 1 : 0.55 }}>
              <td style={{ fontWeight: 600 }}>{f.name}</td>
              <td style={{ fontSize: 12 }}>{stationNames(f)}</td>
              <td>{f.isActive ? 'Yes' : 'No'}</td>
              <td>
                <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                  <button
                    className="btn btn-secondary"
                    style={{ padding: '2px 10px', fontSize: 12 }}
                    onClick={() => {
                      setNotice(null)
                      setEditing(f)
                    }}
                  >
                    Edit
                  </button>
                  <a className="btn btn-secondary" style={{ padding: '2px 10px', fontSize: 12, textDecoration: 'none' }} href={`/kiosk/${f.id}`} target="_blank" rel="noreferrer">
                    Open kiosk
                  </a>
                  <button className="btn btn-danger" style={{ padding: '2px 10px', fontSize: 12 }} onClick={() => remove(f)}>
                    Delete
                  </button>
                </div>
              </td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={4} style={{ textAlign: 'center', color: 'var(--text-faint)' }}>
                No factories yet
              </td>
            </tr>
          )}
        </tbody>
      </table>
      <div style={{ fontSize: 12, color: 'var(--text-faint)', marginTop: 8 }}>
        Assign stations to a factory on the Stations tab. Jobs still go to any station that makes their product, whichever factory it's in.
      </div>
      {editing && (
        <FactoryModal
          factory={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={(message) => {
            setEditing(null)
            setError(null)
            setNotice(message)
            load()
          }}
        />
      )}
    </div>
  )
}

function FactoryModal({ factory, onClose, onSaved }: { factory?: FactoryDto; onClose: () => void; onSaved: (message: string) => void }) {
  const [name, setName] = useState(factory?.name ?? '')
  const [isActive, setIsActive] = useState(factory?.isActive ?? true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function save() {
    if (!name.trim()) return setError('Enter a name.')
    setSaving(true)
    setError(null)
    try {
      if (factory) {
        await factoriesApi.update(factory.id, name.trim(), isActive)
        onSaved(`Saved '${name.trim()}'.`)
      } else {
        await factoriesApi.create(name.trim(), isActive)
        onSaved(`Added '${name.trim()}'.`)
      }
    } catch (e) {
      setError(errorText(e, 'Could not save the factory.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      title={factory ? `Edit ${factory.name}` : 'Add factory'}
      onClose={onClose}
      width={400}
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
          <input
            style={{ width: '100%' }}
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') save()
            }}
            placeholder="e.g. Factory 3"
            autoFocus
          />
        </Field>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          <span style={{ fontSize: 13 }}>Active (shown on the kiosk list)</span>
        </label>
      </div>
      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}
