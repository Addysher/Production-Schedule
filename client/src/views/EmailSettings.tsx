import { useEffect, useState } from 'react'
import { emailApi, settingsApi, type EmailReportStatusDto } from '../api/admin'
import { Field, errorText } from '../components/Modal'
import type { SettingDto } from '../types/api'

// Settings → Email (§5.21): the SMTP relay, and who gets each daily report and when. Everything is
// stored as ordinary settings; this tab is just a friendlier form over them.
const SMTP_FIELDS: { key: string; label: string; hint?: string; type?: string }[] = [
  { key: 'SmtpHost', label: 'SMTP server', hint: 'e.g. smtp.office365.com or the internal relay IT provides' },
  { key: 'SmtpPort', label: 'Port', hint: '25 for an internal relay, 587 with SSL/TLS' },
  { key: 'SmtpFromAddress', label: 'From address', hint: 'The mailbox the reports come from' },
  { key: 'SmtpUsername', label: 'Username', hint: 'Leave blank if the relay needs no sign-in' },
  { key: 'SmtpPassword', label: 'Password', type: 'password' },
  { key: 'EmailPickupDirectory', label: 'Test folder (instead of sending)', hint: 'When set, emails are saved here as .eml files and nothing is sent' },
]

const HOURS = Array.from({ length: 24 }, (_, h) => h)

export function EmailSettingsSection() {
  const [settings, setSettings] = useState<Record<string, string>>({})
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [reports, setReports] = useState<EmailReportStatusDto[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function load() {
    try {
      const [rows, status] = await Promise.all([settingsApi.getAll(), emailApi.reports()])
      setSettings(Object.fromEntries(rows.map((r: SettingDto) => [r.key, r.value])))
      setReports(status)
    } catch (e) {
      setError(errorText(e, 'Could not load the email settings.'))
    }
  }

  useEffect(() => {
    load()
  }, [])

  const value = (key: string) => edits[key] ?? settings[key] ?? ''
  const set = (key: string, v: string) => setEdits((current) => ({ ...current, [key]: v }))
  const changed = Object.keys(edits).filter((k) => edits[k] !== (settings[k] ?? ''))

  // Saves every changed field; stops at the first one the server refuses, naming it.
  async function save() {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      for (const key of changed) {
        try {
          await settingsApi.update(key, edits[key])
        } catch (e) {
          setError(errorText(e, `Could not save ${key}.`))
          return
        }
      }
      setEdits({})
      setNotice('Saved.')
      await load()
    } finally {
      setBusy(false)
    }
  }

  async function sendNow(report: EmailReportStatusDto) {
    if (changed.length > 0) {
      setError('Save your changes before sending.')
      return
    }
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      const result = await emailApi.sendNow(report.key)
      if (result.sent) setNotice(result.message)
      else setError(result.message)
      await load()
    } catch (e) {
      setError(errorText(e, `Could not send the ${report.title.toLowerCase()} email.`))
    } finally {
      setBusy(false)
    }
  }

  async function preview(report: EmailReportStatusDto) {
    // Opened first so the browser treats it as a response to the click, then filled in.
    const win = window.open('', '_blank')
    try {
      const html = await emailApi.preview(report.key)
      if (win) {
        win.document.open()
        win.document.write(html)
        win.document.title = `Preview — ${report.title}`
        win.document.close()
      }
    } catch (e) {
      win?.close()
      setError(errorText(e, 'Could not build the preview.'))
    }
  }

  const input = (key: string, type = 'text') => (
    <input type={type} style={{ width: '100%' }} value={value(key)} onChange={(e) => set(key, e.target.value)} autoComplete="off" />
  )

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div className="card">
        <div style={{ fontWeight: 700, marginBottom: 12 }}>Mail server</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 12 }}>
          {SMTP_FIELDS.map((f) => (
            <Field key={f.key} label={f.label} hint={f.hint}>
              {input(f.key, f.type)}
            </Field>
          ))}
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, alignSelf: 'center' }}>
            <input type="checkbox" checked={value('SmtpUseSsl') === 'true'} onChange={(e) => set('SmtpUseSsl', e.target.checked ? 'true' : 'false')} />
            <span style={{ fontSize: 13 }}>Use SSL/TLS</span>
          </label>
        </div>
      </div>

      {reports.map((r) => (
        <div className="card" key={r.key}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12, gap: 12, flexWrap: 'wrap' }}>
            <div>
              <div style={{ fontWeight: 700 }}>{r.title}</div>
              <div style={{ fontSize: 12, color: 'var(--text-faint)' }}>{DESCRIPTIONS[r.key]}</div>
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <button className="btn btn-secondary" onClick={() => preview(r)}>
                Preview
              </button>
              <button className="btn btn-secondary" disabled={busy || !r.recipients} onClick={() => sendNow(r)} title={r.recipients ? undefined : 'Set who it goes to first'}>
                Send now
              </button>
            </div>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }}>
            <Field label="Send to" hint="One address or distribution list. Leave blank to switch this email off.">
              {input(`Email${r.key}To`)}
            </Field>
            <Field label="Send at" hint="Each working day, at or after this time">
              <select style={{ width: '100%' }} value={value(`Email${r.key}Hour`)} onChange={(e) => set(`Email${r.key}Hour`, e.target.value)}>
                {HOURS.map((h) => (
                  <option key={h} value={String(h)}>
                    {String(h).padStart(2, '0')}:00
                  </option>
                ))}
              </select>
            </Field>
            {r.key === 'ReadyForProduction' && (
              <Field label="Look ahead (days)" hint="Quote jobs due within this many days are listed">
                {input('EmailReadyForProductionDays', 'number')}
              </Field>
            )}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 10 }}>
            Last sent: {r.lastSentAt ? new Date(r.lastSentAt).toLocaleString() : 'never'}
            {r.lastError && (
              <div style={{ color: '#dc2626', marginTop: 4 }}>
                Last attempt failed{r.lastErrorAt ? ` (${new Date(r.lastErrorAt).toLocaleString()})` : ''}: {r.lastError}
              </div>
            )}
          </div>
        </div>
      ))}

      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <button className="btn btn-primary" disabled={busy || changed.length === 0} onClick={save}>
          {busy ? 'Working…' : 'Save'}
        </button>
        {changed.length > 0 && <span style={{ fontSize: 12, color: 'var(--text-faint)' }}>{changed.length} unsaved change(s)</span>}
        {notice && <span style={{ fontSize: 13, color: '#16a34a' }}>{notice}</span>}
        {error && <span style={{ fontSize: 13, color: '#dc2626' }}>{error}</span>}
      </div>
    </div>
  )
}

const DESCRIPTIONS: Record<string, string> = {
  NextDayJobs: 'What each station is planned to make on the next working day, in running order.',
  ReadyForProduction: 'Jobs still at quote stage that are due soon, so they need their final design.',
  DailyProduction: 'What each station made today against its target, and the jobs finished today.',
}
