import { useEffect, useState } from 'react'
import {
  usersApi,
  settingsApi,
  productsApi,
  batchcutWatchPathsApi,
} from '../api/admin'
import { logsApi } from '../api/logs'
import type {
  UserDto,
  SettingDto,
  ProductDto,
  BatchcutWatchPathDto,
} from '../types/api'
import type { BatchcutEventDto } from '../types/logs'
import { JobTypesSection, ShiftsSection, StationsSection } from './SettingsTables'
import { errorText } from '../components/Modal'
import { applyTheme, readThemeSettings, DEFAULT_PRIMARY_COLOR, DEFAULT_SECONDARY_COLOR, type ThemeMode } from '../theme/applyTheme'

type Tab = 'stations' | 'jobtypes' | 'shifts' | 'users' | 'settings' | 'products' | 'batchpaths' | 'batchlog'

const TABS: { key: Tab; label: string }[] = [
  { key: 'products', label: 'Products' },
  { key: 'stations', label: 'Stations' },
  { key: 'jobtypes', label: 'Job Types' },
  { key: 'shifts', label: 'Shifts' },
  { key: 'users', label: 'Users' },
  { key: 'settings', label: 'System Settings' },
  { key: 'batchpaths', label: 'Batchcut Watch Paths' },
  { key: 'batchlog', label: 'Batchcut Log' },
]

export function SettingsView() {
  const [tab, setTab] = useState<Tab>('products')

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
      {tab === 'products' && <ProductsSection />}
      {tab === 'batchpaths' && <BatchPathsSection />}
      {tab === 'batchlog' && <BatchLogSection />}
    </div>
  )
}

// Each section's actions go through this: a refusal from the server (e.g. "At least one active Admin
// is required") is shown under the table instead of disappearing.
function useAttempt() {
  const [error, setError] = useState<string | null>(null)
  const attempt = async (action: () => Promise<unknown>, fallback: string) => {
    setError(null)
    try {
      await action()
      return true
    } catch (e) {
      setError(errorText(e, fallback))
      return false
    }
  }
  const errorLine = error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 10 }}>{error}</div>
  return { attempt, errorLine }
}

function UsersSection() {
  const [rows, setRows] = useState<UserDto[]>([])
  const [form, setForm] = useState({ username: '', password: '', role: 'Viewer' })
  const { attempt, errorLine } = useAttempt()

  const load = () => attempt(() => usersApi.getAll().then(setRows), 'Could not load users.')
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.username || !form.password) return
    if (await attempt(() => usersApi.create(form.username, form.password, form.role), 'Could not add the user.')) {
      setForm({ username: '', password: '', role: 'Viewer' })
      load()
    }
  }

  async function toggleActive(u: UserDto) {
    if (await attempt(() => usersApi.update(u.id, u.role, !u.isActive), `Could not update ${u.username}.`)) load()
  }

  async function changeRole(u: UserDto, role: string) {
    if (await attempt(() => usersApi.update(u.id, role, u.isActive), `Could not change ${u.username}'s role.`)) load()
  }

  async function resetPassword(u: UserDto) {
    const next = window.prompt(`New password for ${u.username} (at least 8 characters):`)
    if (next === null) return
    if (await attempt(() => usersApi.resetPassword(u.id, next), 'Could not reset the password.')) {
      window.alert(`Password reset for ${u.username}.`)
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
              <td>
                <select value={u.role} onChange={(e) => changeRole(u, e.target.value)}>
                  <option value="Viewer">Viewer</option>
                  <option value="Supervisor">Supervisor</option>
                  <option value="Admin">Admin</option>
                </select>
              </td>
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
                <option value="Supervisor">Supervisor</option>
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
      {errorLine}
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
  const { attempt, errorLine } = useAttempt()

  // Saves to the server and previews instantly — applyTheme runs on the merged settings so the whole
  // app (sidebar, badges, status colours) reflects the change immediately, not just on next reload.
  // onSaved merges the one changed row into the parent's state synchronously (no refetch round trip).
  // Every control is disabled for the duration of a save (`saving`): two of these in flight at once
  // would each compute applyTheme from the `rows` snapshot they started with, so whichever finishes
  // last would silently clobber the other's change — serialising them (rather than only avoiding a
  // stale refetch) is what actually rules that out.
  async function set(key: string, value: string) {
    setSaving(true)
    await attempt(async () => {
      const saved = await settingsApi.update(key, value)
      // Deliberately does NOT clear a personal header-toggle override: once a user (including this
      // admin) has explicitly picked a mode for their own browser, changing the sitewide default here
      // must not silently override it — this control still saves the default (and this page's own
      // button highlighting reflects it), it just may not be what you personally see rendered.
      applyTheme([...rows.filter((r) => r.key !== key), saved])
      onSaved(saved)
    }, 'Could not save the appearance setting.')
    setSaving(false)
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
      {errorLine}
    </div>
  )
}

// What each setting does, shown under its name. Keys not listed here still show and save as plain text.
const SETTING_HINTS: Record<string, string> = {
  AtRiskBufferMinutes: 'A job is "at risk" when its planned finish is within this many minutes of its due date',
  ShiftStartHour: 'Hour the working day starts (0–23)',
  ShiftEndHour: 'Hour the working day ends (1–24, after the start)',
  WorkingDays: 'Days production runs: 0 = Sun … 6 = Sat, comma separated',
  ScheduleRetentionMonths: 'Archived schedule records older than this are purged by nightly maintenance',
  PrdPollIntervalMinutes: 'How often production (PRD) files are read',
  BatchcutPollIntervalSeconds: 'How often the Batchcut watch paths are checked',
  RawFilesPath: 'MME drop folder: saw files exported from MiTek land here',
  SawFilesDoNotCutPath: 'Waiting folder: complete saw-file bundles wait here until approved for cutting',
  SawFilesForCuttingPath: 'Cutting folder: bundles approved for cutting are moved here for the saw',
  SawFilesDoneBasePath: 'Where cut (__DONE) folders are filed away',
  MmePollIntervalSeconds: 'How often the MME drop folder is checked',
  CreepThresholdPercent: 'Flag a job when its production value grows by more than this percentage',
  MpsPreMoveDaysBeforeProduction: 'MPS files are sent to the station this many working days before production',
  SchFileSettleSeconds: 'An SCH file is only imported once it has been unchanged this long',
  NightlyMaintenanceRunHour: 'Hour of the night the maintenance tasks (and the MPS send) run',
  FreezeWindowDays: 'Lock-out: jobs starting today and the next this-many working days can’t be moved. 0 = off',
}

function SystemSettingsSection() {
  const [rows, setRows] = useState<SettingDto[]>([])
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [rowErrors, setRowErrors] = useState<Record<string, string>>({})
  const { attempt, errorLine } = useAttempt()

  const load = () => attempt(() => settingsApi.getAll().then(setRows), 'Could not load settings.')
  useEffect(() => {
    load()
  }, [])

  async function save(key: string) {
    if (edits[key] === undefined) return
    try {
      const saved = await settingsApi.update(key, edits[key])
      mergeSaved(saved)
      setEdits(({ [key]: _, ...rest }) => rest)
      setRowErrors(({ [key]: _, ...rest }) => rest)
    } catch (e) {
      setRowErrors((current) => ({ ...current, [key]: errorText(e, 'Could not save this setting.') }))
    }
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
              <td>
                <div>{s.key}</div>
                {SETTING_HINTS[s.key] && <div style={{ fontSize: 11, color: 'var(--text-faint)', maxWidth: 360 }}>{SETTING_HINTS[s.key]}</div>}
              </td>
              <td>
                <input
                  style={{ width: '100%' }}
                  value={edits[s.key] ?? s.value}
                  onChange={(e) => setEdits({ ...edits, [s.key]: e.target.value })}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') save(s.key)
                  }}
                />
                {rowErrors[s.key] && <div style={{ color: '#dc2626', fontSize: 12, marginTop: 4 }}>{rowErrors[s.key]}</div>}
              </td>
              <td>{new Date(s.updatedAt).toLocaleString()}</td>
              <td>
                <button
                  className="btn btn-secondary"
                  style={{ padding: '2px 8px', fontSize: 12 }}
                  disabled={edits[s.key] === undefined || edits[s.key] === s.value}
                  onClick={() => save(s.key)}
                >
                  Save
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {errorLine}
      </div>
    </>
  )
}

type ProductForm = { name: string; poolPath: string; archivePath: string; isActive: boolean }

const toForm = (p: ProductDto): ProductForm => ({ name: p.name, poolPath: p.poolPath ?? '', archivePath: p.archivePath ?? '', isActive: p.isActive })

const toBody = (f: ProductForm) => ({
  name: f.name.trim(),
  poolPath: f.poolPath.trim() || null,
  archivePath: f.archivePath.trim() || null,
  isActive: f.isActive,
})

// v3.0: a job's product comes from the pool folder its SCH file is dropped into. Stations and job
// types pick from these names, so renaming a product here renames it everywhere.
function ProductsSection() {
  const [rows, setRows] = useState<ProductDto[]>([])
  const [edits, setEdits] = useState<Record<number, ProductForm>>({})
  const [form, setForm] = useState<ProductForm>({ name: '', poolPath: '', archivePath: '', isActive: true })

  const load = () =>
    productsApi.getAll().then((all) => {
      setRows(all)
      setEdits(Object.fromEntries(all.map((p) => [p.id, toForm(p)])))
    })
  useEffect(() => {
    load()
  }, [])

  const editOf = (p: ProductDto) => edits[p.id] ?? toForm(p)
  const setEdit = (p: ProductDto, patch: Partial<ProductForm>) => setEdits({ ...edits, [p.id]: { ...editOf(p), ...patch } })
  const isDirty = (p: ProductDto) => JSON.stringify(toBody(editOf(p))) !== JSON.stringify(toBody(toForm(p)))

  async function save(p: ProductDto) {
    const next = editOf(p)
    if (next.name.trim() !== p.name && !window.confirm(`Rename '${p.name}' to '${next.name.trim()}'? Its stations, job types and jobs are renamed too.`)) return
    try {
      await productsApi.update(p.id, toBody(next))
      load()
    } catch (e) {
      window.alert(errorText(e, 'Could not save the product.'))
    }
  }

  async function add() {
    if (!form.name.trim()) return
    try {
      await productsApi.create(toBody(form))
      setForm({ name: '', poolPath: '', archivePath: '', isActive: true })
      load()
    } catch (e) {
      window.alert(errorText(e, 'Could not add the product.'))
    }
  }

  async function remove(p: ProductDto) {
    if (!window.confirm(`Delete '${p.name}'?`)) return
    try {
      await productsApi.remove(p.id)
      load()
    } catch (e) {
      window.alert(errorText(e, 'Could not delete the product.'))
    }
  }

  const cell = { width: '100%' }

  return (
    <div className="card">
      <div style={{ fontSize: 13, color: 'var(--text-muted)', marginBottom: 12 }}>
        The design program drops each product&rsquo;s SCH and MPS files into its <b>pool folder</b>; the folder decides the product. Imported SCH files are
        moved to the <b>archive folder</b> and kept.
      </div>
      <table>
        <thead>
          <tr>
            <th>Product</th>
            <th>Pool folder</th>
            <th>Archive folder</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((p) => {
            const e = editOf(p)
            return (
              <tr key={p.id}>
                <td>
                  <input style={cell} value={e.name} onChange={(ev) => setEdit(p, { name: ev.target.value })} />
                </td>
                <td>
                  <input style={cell} value={e.poolPath} onChange={(ev) => setEdit(p, { poolPath: ev.target.value })} placeholder="\\server\share\pool" />
                </td>
                <td>
                  <input style={cell} value={e.archivePath} onChange={(ev) => setEdit(p, { archivePath: ev.target.value })} placeholder="(pool\_processed)" />
                </td>
                <td>
                  <input type="checkbox" checked={e.isActive} onChange={(ev) => setEdit(p, { isActive: ev.target.checked })} />
                </td>
                <td>
                  <div style={{ display: 'flex', gap: 6 }}>
                    <button className="btn btn-primary" style={{ padding: '2px 8px', fontSize: 12 }} disabled={!isDirty(p)} onClick={() => save(p)}>
                      Save
                    </button>
                    <button className="btn btn-danger" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => remove(p)}>
                      Delete
                    </button>
                  </div>
                </td>
              </tr>
            )
          })}
          <tr>
            <td>
              <input style={cell} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="e.g. Roof Trusses" />
            </td>
            <td>
              <input style={cell} value={form.poolPath} onChange={(e) => setForm({ ...form, poolPath: e.target.value })} placeholder="\\server\share\pool" />
            </td>
            <td>
              <input style={cell} value={form.archivePath} onChange={(e) => setForm({ ...form, archivePath: e.target.value })} placeholder="\\server\share\archive" />
            </td>
            <td />
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
  const { attempt, errorLine } = useAttempt()

  const load = () => attempt(() => batchcutWatchPathsApi.getAll().then(setRows), 'Could not load the watch paths.')
  useEffect(() => {
    load()
  }, [])

  async function add() {
    if (!form.name.trim() || !form.uncPath.trim()) return
    const body = { name: form.name.trim(), uncPath: form.uncPath.trim(), isActive: true }
    if (await attempt(() => batchcutWatchPathsApi.create(body), 'Could not add the watch path.')) {
      setForm({ name: '', uncPath: '' })
      load()
    }
  }

  async function toggleActive(p: BatchcutWatchPathDto) {
    const body = { name: p.name, uncPath: p.uncPath, isActive: !p.isActive }
    if (await attempt(() => batchcutWatchPathsApi.update(p.id, body), `Could not update '${p.name}'.`)) load()
  }

  // A path with Batchcut Log history is switched off rather than deleted, so its history stays readable.
  async function remove(p: BatchcutWatchPathDto) {
    if (!confirm(`Delete the watch path '${p.name}'?`)) return
    if (await attempt(() => batchcutWatchPathsApi.remove(p.id), `Could not delete '${p.name}'.`)) load()
  }

  return (
    <div className="card">
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>UNC Path</th>
            <th>Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((p) => (
            <tr key={p.id}>
              <td>{p.name}</td>
              <td>{p.uncPath}</td>
              <td>
                <input type="checkbox" checked={p.isActive} onChange={() => toggleActive(p)} aria-label={`${p.name} active`} />
              </td>
              <td>
                <button className="btn btn-danger" style={{ padding: '2px 8px', fontSize: 12 }} onClick={() => remove(p)}>
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
            <td></td>
            <td>
              <button className="btn btn-primary" style={{ padding: '4px 10px', fontSize: 12 }} onClick={add}>
                Add
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <div style={{ fontSize: 12, color: 'var(--text-faint)', marginTop: 8 }}>
        A path with entries in the Batchcut Log is switched off instead of deleted, so its history is kept.
      </div>
      {errorLine}
    </div>
  )
}

function BatchLogSection() {
  const [rows, setRows] = useState<BatchcutEventDto[]>([])
  const { attempt, errorLine } = useAttempt()

  // §5.12: the most recent 50 events.
  useEffect(() => {
    attempt(() => logsApi.getBatchcutEvents(50).then(setRows), 'Could not load the Batchcut Log.')
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
      {errorLine}
    </div>
  )
}
