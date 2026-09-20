import { useState } from 'react'
import { authApi } from '../api/auth'

export function ChangePasswordModal({ onClose }: { onClose: () => void }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)
  const [saving, setSaving] = useState(false)

  async function submit() {
    if (next.length < 8) return setError('New password must be at least 8 characters.')
    if (next !== confirm) return setError('New password and confirmation do not match.')
    setSaving(true)
    setError(null)
    try {
      await authApi.changePassword(current, next)
      setDone(true)
    } catch (e) {
      const data = (e as { response?: { data?: unknown } }).response?.data
      setError(typeof data === 'string' ? data : 'Could not change the password.')
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
        <div style={{ fontSize: 16, fontWeight: 700, marginBottom: 16 }}>Change password</div>

        {done ? (
          <>
            <div style={{ fontSize: 14, marginBottom: 20 }}>Your password has been changed.</div>
            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button className="btn btn-primary" onClick={onClose}>
                Close
              </button>
            </div>
          </>
        ) : (
          <>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <label>
                <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Current password</div>
                <input type="password" style={{ width: '100%' }} value={current} onChange={(e) => setCurrent(e.target.value)} autoFocus />
              </label>
              <label>
                <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>New password</div>
                <input type="password" style={{ width: '100%' }} value={next} onChange={(e) => setNext(e.target.value)} />
              </label>
              <label>
                <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Confirm new password</div>
                <input type="password" style={{ width: '100%' }} value={confirm} onChange={(e) => setConfirm(e.target.value)} />
              </label>
            </div>
            {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 8 }}>{error}</div>}
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>
              <button className="btn btn-secondary" onClick={onClose} disabled={saving}>
                Cancel
              </button>
              <button className="btn btn-primary" onClick={submit} disabled={saving || !current || !next}>
                {saving ? 'Saving…' : 'Change password'}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}
