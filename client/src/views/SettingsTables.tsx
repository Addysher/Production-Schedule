import { useEffect, useState } from 'react'
import { factoriesApi, jobTypesApi, shiftsApi } from '../api/admin'
import { stationsApi } from '../api/stations'
import type { FactoryDto, JobTypeDto, ShiftDto, StationDto } from '../types/api'
import { JobTypeModal } from '../components/JobTypeModal'
import { ShiftModal } from '../components/ShiftModal'
import { StationModal } from '../components/StationModal'
import { errorText } from '../components/Modal'

// Loads a table's rows; a failure is shown above the table rather than leaving it silently empty.
function useRows<T>(fetch: () => Promise<T[]>, what: string) {
  const [rows, setRows] = useState<T[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const load = () =>
    fetch()
      .then((r) => {
        setRows(r)
        setLoadError(null)
      })
      .catch((e) => setLoadError(errorText(e, `Could not load ${what}.`)))
  const errorLine = loadError && <div style={{ color: '#dc2626', fontSize: 13, marginBottom: 10 }}>{loadError}</div>
  return { rows, load, errorLine }
}

const editButton = { padding: '2px 10px', fontSize: 12 }

export function StationsSection() {
  const [editing, setEditing] = useState<StationDto | 'new' | null>(null)

  const { rows, load, errorLine } = useRows(() => stationsApi.getAll(), 'the stations')
  const [factories, setFactories] = useState<FactoryDto[]>([])
  useEffect(() => {
    factoriesApi.getAll().then(setFactories).catch(() => setFactories([]))
  }, [])
  useEffect(() => {
    load()
  }, [])

  return (
    <div className="card">
      {errorLine}
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + Add station
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Product</th>
            <th>Factory</th>
            <th>PRD folder</th>
            <th>Archive folder</th>
            <th>Daily EqA</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s) => (
            <tr key={s.id} style={{ opacity: s.isActive ? 1 : 0.55 }}>
              <td>{s.name}</td>
              <td>{s.componentType}</td>
              <td>{factories.find((f) => f.id === s.factoryId)?.name ?? '—'}</td>
              <td style={{ wordBreak: 'break-all', fontSize: 12 }}>{s.uncPath ?? '—'}</td>
              <td style={{ wordBreak: 'break-all', fontSize: 12 }}>{s.archivePath ?? '—'}</td>
              <td>{s.dailyEqATarget ?? '—'}</td>
              <td>{s.isActive ? 'Yes' : 'No'}</td>
              <td>
                <button className="btn btn-secondary" style={editButton} onClick={() => setEditing(s)}>
                  Edit
                </button>
              </td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={8} style={{ textAlign: 'center', color: 'var(--text-faint)' }}>
                No stations yet
              </td>
            </tr>
          )}
        </tbody>
      </table>
      {editing && (
        <StationModal
          station={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}
    </div>
  )
}

export function JobTypesSection() {
  const [editing, setEditing] = useState<JobTypeDto | 'new' | null>(null)

  const { rows, load, errorLine } = useRows(() => jobTypesApi.getAll(), 'the job types')
  useEffect(() => {
    load()
  }, [])

  return (
    <div className="card">
      {errorLine}
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + Add job type
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Product</th>
            <th>Mins / unit</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((t) => (
            <tr key={t.id} style={{ opacity: t.isActive ? 1 : 0.55 }}>
              <td>{t.name}</td>
              <td>{t.componentType ?? '—'}</td>
              <td>{t.minsPerUnit}</td>
              <td>{t.isActive ? 'Yes' : 'No'}</td>
              <td>
                <button className="btn btn-secondary" style={editButton} onClick={() => setEditing(t)}>
                  Edit
                </button>
              </td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={5} style={{ textAlign: 'center', color: 'var(--text-faint)' }}>
                No job types yet
              </td>
            </tr>
          )}
        </tbody>
      </table>
      {editing && (
        <JobTypeModal
          jobType={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}
    </div>
  )
}

export function ShiftsSection() {
  const [editing, setEditing] = useState<ShiftDto | 'new' | null>(null)

  const { rows, load, errorLine } = useRows(() => shiftsApi.getAll(), 'the shifts')
  useEffect(() => {
    load()
  }, [])

  return (
    <div className="card">
      {errorLine}
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + Add shift
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Start</th>
            <th>End</th>
            <th>Breaks</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s) => (
            <tr key={s.id} style={{ opacity: s.isActive ? 1 : 0.55 }}>
              <td>{s.name}</td>
              <td>{s.startTime.slice(0, 5)}</td>
              <td>{s.endTime.slice(0, 5)}</td>
              <td>{s.breaks.length === 0 ? '—' : s.breaks.map((b) => `${b.breakStart.slice(0, 5)} (${b.breakDurationMins}m)`).join(', ')}</td>
              <td>{s.isActive ? 'Yes' : 'No'}</td>
              <td>
                <button className="btn btn-secondary" style={editButton} onClick={() => setEditing(s)}>
                  Edit
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editing && (
        <ShiftModal
          shift={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            load()
          }}
        />
      )}
    </div>
  )
}
