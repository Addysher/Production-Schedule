import { useEffect, useState } from 'react'
import { factoriesApi } from '../api/admin'
import { stationsApi } from '../api/stations'
import { errorText } from '../components/Modal'
import type { FactoryDto, StationDto } from '../types/api'

// Settings → Factories (v3.0). A factory groups stations for its kiosk display; which stations belong
// to it is set on each station (Settings → Stations). Scheduling ignores factories.
export function FactoriesSection() {
  const [rows, setRows] = useState<FactoryDto[]>([])
  const [stations, setStations] = useState<StationDto[]>([])
  const [names, setNames] = useState<Record<number, string>>({})
  const [newName, setNewName] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function attempt(action: () => Promise<unknown>, fallback: string) {
    setError(null)
    try {
      await action()
      return true
    } catch (e) {
      setError(errorText(e, fallback))
      return false
    }
  }

  const load = () =>
    attempt(async () => {
      const [factories, stationList] = await Promise.all([factoriesApi.getAll(), stationsApi.getAll()])
      setRows(factories)
      setStations(stationList)
      setNames({})
    }, 'Could not load the factories.')

  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!newName.trim()) return
    if (await attempt(() => factoriesApi.create(newName.trim(), true), 'Could not add the factory.')) {
      setNewName('')
      load()
    }
  }

  async function rename(f: FactoryDto) {
    const name = names[f.id]?.trim()
    if (!name || name === f.name) return
    if (await attempt(() => factoriesApi.update(f.id, name, f.isActive), `Could not rename '${f.name}'.`)) load()
  }

  async function toggleActive(f: FactoryDto) {
    if (await attempt(() => factoriesApi.update(f.id, f.name, !f.isActive), `Could not update '${f.name}'.`)) load()
  }

  async function remove(f: FactoryDto) {
    if (!confirm(`Delete the factory '${f.name}'? Its stations stay, just unassigned.`)) return
    if (await attempt(() => factoriesApi.remove(f.id), `Could not delete '${f.name}'.`)) load()
  }

  const stationNames = (f: FactoryDto) =>
    f.stationIds.map((id) => stations.find((s) => s.id === id)?.name).filter(Boolean).join(', ') || '—'

  return (
    <div className="card">
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
              <td>
                <input
                  value={names[f.id] ?? f.name}
                  onChange={(e) => setNames({ ...names, [f.id]: e.target.value })}
                  onBlur={() => rename(f)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') rename(f)
                  }}
                  aria-label={`${f.name} name`}
                />
              </td>
              <td style={{ fontSize: 12 }}>{stationNames(f)}</td>
              <td>
                <input type="checkbox" checked={f.isActive} onChange={() => toggleActive(f)} aria-label={`${f.name} active`} />
              </td>
              <td style={{ display: 'flex', gap: 6 }}>
                <a className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12, textDecoration: 'none' }} href={`/kiosk/${f.id}`} target="_blank" rel="noreferrer">
                  Open kiosk
                </a>
                <button className="btn btn-danger" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => remove(f)}>
                  Delete
                </button>
              </td>
            </tr>
          ))}
          <tr>
            <td>
              <input
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') add()
                }}
                placeholder="e.g. Factory 1"
              />
            </td>
            <td colSpan={2}></td>
            <td>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <div style={{ fontSize: 12, color: 'var(--text-faint)', marginTop: 8 }}>
        Assign stations to a factory on the Stations tab. Jobs still go to any station that makes their product, whichever factory it's in.
      </div>
      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 10 }}>{error}</div>}
    </div>
  )
}
