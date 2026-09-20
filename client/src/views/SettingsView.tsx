import { useEffect, useState } from 'react'
import {
  jobTypesApi,
  shiftsApi,
  usersApi,
  settingsApi,
  schImportPathsApi,
  batchcutWatchPathsApi,
} from '../api/admin'
import { stationsApi } from '../api/stations'
import { logsApi } from '../api/logs'
import type {
  JobTypeDto,
  ShiftDto,
  UserDto,
  SettingDto,
  SchImportPathDto,
  BatchcutWatchPathDto,
  StationDto,
} from '../types/api'
import type { BatchcutEventDto } from '../types/logs'

type Tab = 'stations' | 'jobtypes' | 'shifts' | 'users' | 'settings' | 'schpaths' | 'batchpaths' | 'batchlog'

const TABS: { key: Tab; label: string }[] = [
  { key: 'stations', label: 'Stations' },
  { key: 'jobtypes', label: 'Job Types' },
  { key: 'shifts', label: 'Shifts' },
  { key: 'users', label: 'Users' },
  { key: 'settings', label: 'System Settings' },
  { key: 'schpaths', label: 'SCH Import Paths' },
  { key: 'batchpaths', label: 'Batchcut Watch Paths' },
  { key: 'batchlog', label: 'Batchcut Log' },
]

export function SettingsView() {
  const [tab, setTab] = useState<Tab>('stations')

  return (
    <div>
      <h2 style={{ marginTop: 0 }}>Settings</h2>
      <div style={{ display: 'flex', gap: 4, marginBottom: 20, flexWrap: 'wrap' }}>
        {TABS.map((t) => (
          <button
            key={t.key}
            className="btn"
            style={{
              background: tab === t.key ? 'var(--brand-primary)' : '#fff',
              color: tab === t.key ? '#fff' : 'var(--brand-primary)',
              border: '1px solid var(--brand-primary)',
            }}
            onClick={() => setTab(t.key)}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'stations' && <StationsSection />}
      {tab === 'jobtypes' && <JobTypesSection />}
      {tab === 'shifts' && <ShiftsSection />}
      {tab === 'users' && <UsersSection />}
      {tab === 'settings' && <SystemSettingsSection />}
      {tab === 'schpaths' && <SchPathsSection />}
      {tab === 'batchpaths' && <BatchPathsSection />}
      {tab === 'batchlog' && <BatchLogSection />}
    </div>
  )
}

function StationsSection() {
  const [rows, setRows] = useState<StationDto[]>([])
  const [form, setForm] = useState({ name: '', componentType: '', uncPath: '', dailyEqATarget: '' })

  const load = () => stationsApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.name || !form.componentType) return
    await stationsApi.create({
      name: form.name,
      componentType: form.componentType,
      uncPath: form.uncPath || null,
      displayOrder: rows.length,
      dailyEqATarget: form.dailyEqATarget ? Number(form.dailyEqATarget) : null,
      archivePath: null,
    })
    setForm({ name: '', componentType: '', uncPath: '', dailyEqATarget: '' })
    load()
  }

  async function toggleActive(s: StationDto) {
    await stationsApi.update(s.id, { ...s, isActive: !s.isActive })
    load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Component Type</th>
            <th>UNC Path</th>
            <th>Daily EqA Target</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s) => (
            <tr key={s.id}>
              <td>{s.name}</td>
              <td>{s.componentType}</td>
              <td>{s.uncPath ?? '—'}</td>
              <td>{s.dailyEqATarget ?? '—'}</td>
              <td>{s.isActive ? 'Yes' : 'No'}</td>
              <td>
                <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => toggleActive(s)}>
                  {s.isActive ? 'Deactivate' : 'Activate'}
                </button>
              </td>
            </tr>
          ))}
          <tr>
            <td>
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Name" />
            </td>
            <td>
              <input
                value={form.componentType}
                onChange={(e) => setForm({ ...form, componentType: e.target.value })}
                placeholder="ComponentType"
              />
            </td>
            <td>
              <input value={form.uncPath} onChange={(e) => setForm({ ...form, uncPath: e.target.value })} placeholder="\\server\\share" />
            </td>
            <td>
              <input
                type="number"
                value={form.dailyEqATarget}
                onChange={(e) => setForm({ ...form, dailyEqATarget: e.target.value })}
                style={{ width: 80 }}
              />
            </td>
            <td colSpan={2}>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}

function JobTypesSection() {
  const [rows, setRows] = useState<JobTypeDto[]>([])
  const [form, setForm] = useState({ name: '', componentType: '', minsPerUnit: '' })

  const load = () => jobTypesApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.name || !form.minsPerUnit) return
    await jobTypesApi.create({ name: form.name, componentType: form.componentType || null, minsPerUnit: Number(form.minsPerUnit), isActive: true })
    setForm({ name: '', componentType: '', minsPerUnit: '' })
    load()
  }

  async function toggleActive(t: JobTypeDto) {
    await jobTypesApi.update(t.id, { name: t.name, componentType: t.componentType, minsPerUnit: t.minsPerUnit, isActive: !t.isActive })
    load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Component Type</th>
            <th>Mins / Unit</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((t) => (
            <tr key={t.id}>
              <td>{t.name}</td>
              <td>{t.componentType ?? '—'}</td>
              <td>{t.minsPerUnit}</td>
              <td>{t.isActive ? 'Yes' : 'No'}</td>
              <td>
                <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => toggleActive(t)}>
                  {t.isActive ? 'Deactivate' : 'Activate'}
                </button>
              </td>
            </tr>
          ))}
          <tr>
            <td>
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Name" />
            </td>
            <td>
              <input value={form.componentType} onChange={(e) => setForm({ ...form, componentType: e.target.value })} placeholder="ComponentType" />
            </td>
            <td>
              <input
                type="number"
                value={form.minsPerUnit}
                onChange={(e) => setForm({ ...form, minsPerUnit: e.target.value })}
                style={{ width: 80 }}
              />
            </td>
            <td colSpan={2}>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}

function ShiftsSection() {
  const [rows, setRows] = useState<ShiftDto[]>([])
  const [form, setForm] = useState({ name: '', startTime: '06:00', endTime: '18:00' })

  const load = () => shiftsApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.name) return
    await shiftsApi.create({ name: form.name, startTime: form.startTime, endTime: form.endTime, isActive: true, breaks: [] })
    setForm({ name: '', startTime: '06:00', endTime: '18:00' })
    load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Start</th>
            <th>End</th>
            <th>Breaks</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s) => (
            <tr key={s.id}>
              <td>{s.name}</td>
              <td>{s.startTime}</td>
              <td>{s.endTime}</td>
              <td>{s.breaks.length}</td>
            </tr>
          ))}
          <tr>
            <td>
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Name" />
            </td>
            <td>
              <input type="time" value={form.startTime} onChange={(e) => setForm({ ...form, startTime: e.target.value })} />
            </td>
            <td>
              <input type="time" value={form.endTime} onChange={(e) => setForm({ ...form, endTime: e.target.value })} />
            </td>
            <td>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}

function UsersSection() {
  const [rows, setRows] = useState<UserDto[]>([])
  const [form, setForm] = useState({ username: '', password: '', role: 'Viewer' })

  const load = () => usersApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.username || !form.password) return
    await usersApi.create(form.username, form.password, form.role)
    setForm({ username: '', password: '', role: 'Viewer' })
    load()
  }

  async function toggleActive(u: UserDto) {
    await usersApi.update(u.id, u.role, !u.isActive)
    load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Username</th>
            <th>Role</th>
            <th>Active</th>
            <th>Last Login</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((u) => (
            <tr key={u.id}>
              <td>{u.username}</td>
              <td>{u.role}</td>
              <td>{u.isActive ? 'Yes' : 'No'}</td>
              <td>{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : '—'}</td>
              <td>
                <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => toggleActive(u)}>
                  {u.isActive ? 'Deactivate' : 'Activate'}
                </button>
              </td>
            </tr>
          ))}
          <tr>
            <td>
              <input value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} placeholder="Username" />
            </td>
            <td>
              <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}>
                <option value="Viewer">Viewer</option>
                <option value="Admin">Admin</option>
              </select>
            </td>
            <td colSpan={2}>
              <input
                type="password"
                value={form.password}
                onChange={(e) => setForm({ ...form, password: e.target.value })}
                placeholder="Password"
              />
            </td>
            <td>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}

function SystemSettingsSection() {
  const [rows, setRows] = useState<SettingDto[]>([])
  const [edits, setEdits] = useState<Record<string, string>>({})

  const load = () => settingsApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  async function save(key: string) {
    if (edits[key] === undefined) return
    await settingsApi.update(key, edits[key])
    load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Key</th>
            <th>Value</th>
            <th>Updated</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s) => (
            <tr key={s.key}>
              <td>{s.key}</td>
              <td>
                <input
                  style={{ width: '100%' }}
                  value={edits[s.key] ?? s.value}
                  onChange={(e) => setEdits({ ...edits, [s.key]: e.target.value })}
                />
              </td>
              <td>{new Date(s.updatedAt).toLocaleString()}</td>
              <td>
                <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => save(s.key)}>
                  Save
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function SchPathsSection() {
  const [rows, setRows] = useState<SchImportPathDto[]>([])
  const [form, setForm] = useState({ name: '', uncPath: '', componentType: '' })

  const load = () => schImportPathsApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.name || !form.uncPath) return
    await schImportPathsApi.create({ name: form.name, uncPath: form.uncPath, componentType: form.componentType || null, isActive: true })
    setForm({ name: '', uncPath: '', componentType: '' })
    load()
  }

  async function remove(id: number) {
    await schImportPathsApi.remove(id)
    load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>UNC Path</th>
            <th>Component Type</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((p) => (
            <tr key={p.id}>
              <td>{p.name}</td>
              <td>{p.uncPath}</td>
              <td>{p.componentType ?? 'Any'}</td>
              <td>
                <button className="btn btn-danger" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => remove(p.id)}>
                  Delete
                </button>
              </td>
            </tr>
          ))}
          <tr>
            <td>
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Name" />
            </td>
            <td>
              <input value={form.uncPath} onChange={(e) => setForm({ ...form, uncPath: e.target.value })} placeholder="\\server\\share" />
            </td>
            <td>
              <input
                value={form.componentType}
                onChange={(e) => setForm({ ...form, componentType: e.target.value })}
                placeholder="(any)"
              />
            </td>
            <td>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}

function BatchPathsSection() {
  const [rows, setRows] = useState<BatchcutWatchPathDto[]>([])
  const [form, setForm] = useState({ name: '', uncPath: '' })

  const load = () => batchcutWatchPathsApi.getAll().then(setRows)
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.name || !form.uncPath) return
    await batchcutWatchPathsApi.create({ name: form.name, uncPath: form.uncPath, isActive: true })
    setForm({ name: '', uncPath: '' })
    load()
  }

  async function remove(id: number) {
    await batchcutWatchPathsApi.remove(id)
    load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>UNC Path</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((p) => (
            <tr key={p.id}>
              <td>{p.name}</td>
              <td>{p.uncPath}</td>
              <td>
                <button className="btn btn-danger" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => remove(p.id)}>
                  Delete
                </button>
              </td>
            </tr>
          ))}
          <tr>
            <td>
              <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Name" />
            </td>
            <td>
              <input value={form.uncPath} onChange={(e) => setForm({ ...form, uncPath: e.target.value })} placeholder="\\server\\share" />
            </td>
            <td>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  )
}

function BatchLogSection() {
  const [rows, setRows] = useState<BatchcutEventDto[]>([])

  useEffect(() => {
    logsApi.getBatchcutEvents(100).then(setRows)
  }, [])

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Folder</th>
            <th>Job Ref</th>
            <th>Status</th>
            <th>Detected</th>
            <th>Error</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((e) => (
            <tr key={e.id}>
              <td>{e.folderName}</td>
              <td>{e.jobRef ?? '—'}</td>
              <td>{e.status}</td>
              <td>{new Date(e.detectedAt).toLocaleString()}</td>
              <td>{e.errorMessage ?? ''}</td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr>
              <td colSpan={5} style={{ textAlign: 'center', color: '#94a3b8' }}>
                No events yet
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
