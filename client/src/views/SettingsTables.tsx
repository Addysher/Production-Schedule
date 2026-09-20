import { useEffect, useState } from 'react'
import { jobTypesApi, shiftsApi } from '../api/admin'
import { stationsApi } from '../api/stations'
import type { JobTypeDto, ShiftDto, StationDto } from '../types/api'
import { JobTypeModal } from '../components/JobTypeModal'
import { ShiftModal } from '../components/ShiftModal'
import { StationModal } from '../components/StationModal'

const editButton = { padding: '2px 10px', fontSize: 12 }

export function StationsSection() {
  const [rows, setRows] = useState<StationDto[]>([])
  const [editing, setEditing] = useState<StationDto | 'new' | null>(null)

  const load = () => stationsApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  return (
    <div className="card">
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + Add station
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Component type</th>
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
              <td colSpan={7} style={{ textAlign: 'center', color: '#94a3b8' }}>
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
  const [rows, setRows] = useState<JobTypeDto[]>([])
  const [editing, setEditing] = useState<JobTypeDto | 'new' | null>(null)

  const load = () => jobTypesApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  return (
    <div className="card">
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
        <button className="btn btn-primary" onClick={() => setEditing('new')}>
          + Add job type
        </button>
      </div>
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Component type</th>
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
              <td colSpan={5} style={{ textAlign: 'center', color: '#94a3b8' }}>
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
  const [rows, setRows] = useState<ShiftDto[]>([])
  const [editing, setEditing] = useState<ShiftDto | 'new' | null>(null)

  const load = () => shiftsApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  return (
    <div className="card">
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
