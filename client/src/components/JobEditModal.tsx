import { useState } from 'react'
import type { JobDetailDto, UpdateJobRequest } from '../types/api'

export function JobEditModal({
  job,
  onClose,
  onSave,
}: {
  job: JobDetailDto
  onClose: () => void
  onSave: (request: UpdateJobRequest) => Promise<void>
}) {
  const [form, setForm] = useState<UpdateJobRequest>({
    supervisorName: job.supervisorName ?? '',
    supervisorNumber: job.supervisorNumber ?? '',
    bookedDeliveryDate: job.bookedDeliveryDate ?? '',
    invoiceNumber: job.invoiceNumber ?? '',
    delivered: job.delivered,
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSave() {
    setSaving(true)
    setError(null)
    try {
      await onSave(form)
      onClose()
    } catch {
      setError('Could not save changes.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(15, 23, 42, 0.45)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 60,
      }}
    >
      <div onClick={(e) => e.stopPropagation()} className="card" style={{ width: 440 }}>
        <div style={{ fontSize: 16, fontWeight: 700, marginBottom: 16 }}>Edit {job.jobRef}</div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Supervisor Name</div>
            <input
              style={{ width: '100%' }}
              value={form.supervisorName ?? ''}
              onChange={(e) => setForm({ ...form, supervisorName: e.target.value })}
            />
          </label>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Supervisor Number</div>
            <input
              style={{ width: '100%' }}
              value={form.supervisorNumber ?? ''}
              onChange={(e) => setForm({ ...form, supervisorNumber: e.target.value })}
            />
          </label>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Booked Delivery Date</div>
            <input
              type="date"
              style={{ width: '100%' }}
              value={form.bookedDeliveryDate ?? ''}
              onChange={(e) => setForm({ ...form, bookedDeliveryDate: e.target.value })}
            />
          </label>
          <label>
            <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Invoice Number</div>
            <input
              style={{ width: '100%' }}
              value={form.invoiceNumber ?? ''}
              onChange={(e) => setForm({ ...form, invoiceNumber: e.target.value })}
            />
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <input
              type="checkbox"
              checked={form.delivered}
              onChange={(e) => setForm({ ...form, delivered: e.target.checked })}
            />
            <span style={{ fontSize: 13 }}>Delivered</span>
          </label>
        </div>

        {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 8 }}>{error}</div>}

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>
          <button className="btn btn-secondary" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={handleSave} disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  )
}
