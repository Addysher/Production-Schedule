import { useEffect, useState } from 'react'
import {
  usersApi,
  settingsApi,
  schImportPathsApi,
  batchcutWatchPathsApi,
} from '../api/admin'
import { logsApi } from '../api/logs'
import type {
  UserDto,
  SettingDto,
  SchImportPathDto,
  BatchcutWatchPathDto,
} from '../types/api'
import type { BatchcutEventDto } from '../types/logs'
import { JobTypesSection, ShiftsSection, StationsSection } from './SettingsTables'
import { applyTheme, readThemeSettings, DEFAULT_PRIMARY_COLOR, DEFAULT_SECONDARY_COLOR, type ThemeMode } from '../theme/applyTheme'

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
              background: tab === t.key ? 'var(--brand-primary)' : 'var(--card-bg)',
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

  async function resetPassword(u: UserDto) {
    const next = window.prompt(`New password for ${u.username} (at least 8 characters):`)
    if (next === null) return
    try {
      await usersApi.resetPassword(u.id, next)
      window.alert(`Password reset for ${u.username}.`)
    } catch (e) {
      const data = (e as { response?: { data?: unknown } }).response?.data
      window.alert(typeof data === 'string' ? data : 'Could not reset the password.')
    }
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
              <td style={{ display: 'flex', gap: 6 }}>
                <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => toggleActive(u)}>
                  {u.isActive ? 'Deactivate' : 'Activate'}
                </button>
                <button className="btn btn-secondary" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => resetPassword(u)}>
                  Reset password
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

const PRIMARY_PALETTE = [
  { name: 'Teal (default)', hex: DEFAULT_PRIMARY_COLOR },
  { name: 'Navy', hex: '#1e3a5f' },
  { name: 'Forest Green', hex: '#166534' },
  { name: 'Maroon', hex: '#7f1d1d' },
  { name: 'Slate', hex: '#334155' },
  { name: 'Purple', hex: '#5b21b6' },
  { name: 'Charcoal', hex: '#1f2937' },
  { name: 'Rust', hex: '#9a3412' },
]

const SECONDARY_PALETTE = [
  { name: 'Amber (default)', hex: DEFAULT_SECONDARY_COLOR },
  { name: 'Red', hex: '#ef4444' },
  { name: 'Blue', hex: '#3b82f6' },
  { name: 'Lime', hex: '#84cc16' },
  { name: 'Pink', hex: '#ec4899' },
  { name: 'Cyan', hex: '#06b6d4' },
  { name: 'Orange', hex: '#f97316' },
  { name: 'Ice White', hex: '#f8fafc' },
]

const CUSTOM = '__custom__'

function ColorSetting({
  label,
  value,
  palette,
  disabled,
  onChange,
}: {
  label: string
  value: string
  palette: { name: string; hex: string }[]
  disabled: boolean
  onChange: (hex: string) => void
}) {
  const matched = palette.find((p) => p.hex.toLowerCase() === value.toLowerCase())

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
      <div style={{ width: 110, fontSize: 13, fontWeight: 600 }}>{label}</div>
      <select
        disabled={disabled}
        value={matched ? matched.hex : CUSTOM}
        onChange={(e) => {
          if (e.target.value !== CUSTOM) onChange(e.target.value)
        }}
      >
        {palette.map((p) => (
          <option key={p.hex} value={p.hex}>
            {p.name}
          </option>
        ))}
        <option value={CUSTOM}>Custom…</option>
      </select>
      <input type="color" disabled={disabled} value={value} onChange={(e) => onChange(e.target.value)} style={{ width: 40, height: 30, padding: 2 }} />
      <span style={{ fontSize: 12, color: 'var(--text-faint)', fontFamily: 'monospace' }}>{value}</span>
    </div>
  )
}

function AppearanceSection({ rows, onSaved }: { rows: SettingDto[]; onSaved: (saved: SettingDto) => void }) {
  const { primaryColor, secondaryColor, mode } = readThemeSettings(rows)
  const [saving, setSaving] = useState(false)

  // Saves to the server and previews instantly — applyTheme runs on the merged settings so the whole
  // app (sidebar, badges, status colours) reflects the change immediately, not just on next reload.
  // onSaved merges the one changed row into the parent's state synchronously (no refetch round trip).
  // Every control is disabled for the duration of a save (`saving`): two of these in flight at once
  // would each compute applyTheme from the `rows` snapshot they started with, so whichever finishes
  // last would silently clobber the other's change — serialising them (rather than only avoiding a
  // stale refetch) is what actually rules that out.
  async function set(key: string, value: string) {
    setSaving(true)
    try {
      const saved = await settingsApi.update(key, value)
      // Deliberately does NOT clear a personal header-toggle override: once a user (including this
      // admin) has explicitly picked a mode for their own browser, changing the sitewide default here
      // must not silently override it — this control still saves the default (and this page's own
      // button highlighting reflects it), it just may not be what you personally see rendered.
      applyTheme([...rows.filter((r) => r.key !== key), saved])
      onSaved(saved)
    } finally {
      setSaving(false)
    }
  }

  const modeButton = (m: ThemeMode, label: string) => (
    <button
      key={m}
      className="btn"
      disabled={saving}
      style={{
        background: mode === m ? 'var(--brand-primary)' : 'var(--card-bg)',
        color: mode === m ? '#fff' : 'var(--brand-primary)',
        border: '1px solid var(--brand-primary)',
        borderRadius: 0,
      }}
      onClick={() => set('ThemeMode', m)}
    >
      {label}
    </button>
  )

  return (
    <div className="card" style={{ marginBottom: 16, display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div style={{ fontWeight: 700 }}>Appearance</div>
      <ColorSetting label="Primary colour" value={primaryColor} palette={PRIMARY_PALETTE} disabled={saving} onChange={(hex) => set('ThemePrimaryColor', hex)} />
      <ColorSetting label="Secondary colour" value={secondaryColor} palette={SECONDARY_PALETTE} disabled={saving} onChange={(hex) => set('ThemeSecondaryColor', hex)} />
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        <div style={{ width: 110, fontSize: 13, fontWeight: 600 }}>Theme mode</div>
        <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden' }}>
          {modeButton('system', 'System')}
          {modeButton('light', 'Light')}
          {modeButton('dark', 'Dark')}
        </div>
        <span style={{ fontSize: 12, color: 'var(--text-faint)' }}>
          {saving ? 'Saving…' : mode === 'system' ? "Follows this PC's light/dark setting" : `Always ${mode}`}
        </span>
      </div>
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

  function mergeSaved(saved: SettingDto) {
    setRows((current) => [...current.filter((r) => r.key !== saved.key), saved].sort((a, b) => a.key.localeCompare(b.key)))
  }

  return (
    <>
      <AppearanceSection rows={rows} onSaved={mergeSaved} />
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
    </>
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
              <td colSpan={5} style={{ textAlign: 'center', color: 'var(--text-faint)' }}>
                No events yet
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
