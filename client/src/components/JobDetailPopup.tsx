import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { errorText } from './Modal'
import type { JobDetailDto } from '../types/api'
import { StatusPill } from './StatusPill'

function Field({ label, value }: { label: string; value: string | number | null | undefined }) {
  return (
    <div>
      <div style={{ fontSize: 11, color: 'var(--text-faint)', textTransform: 'uppercase', fontWeight: 600 }}>{label}</div>
      <div style={{ fontSize: 14, color: 'var(--text-primary)' }}>{value ?? '—'}</div>
    </div>
  )
}

export function JobDetailPopup({
  job,
  onClose,
  onEdit,
  onApproveForCutting,
  onScheduleManually,
}: {
  job: JobDetailDto
  onClose: () => void
  onEdit?: () => void
  onApproveForCutting?: () => Promise<void>
  onScheduleManually?: () => void
}) {
  const { user } = useAuth()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function approve() {
    if (!onApproveForCutting) return
    setBusy(true)
    setError(null)
    try {
      await onApproveForCutting()
    } catch (e) {
      setError(errorText(e, `Could not approve ${job.jobRef} for cutting.`))
    } finally {
      setBusy(false)
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
        zIndex: 50,
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="card"
        style={{ width: 560, maxHeight: '85vh', overflow: 'auto' }}
      >
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <div>
            <div style={{ fontSize: 18, fontWeight: 700 }}>{job.jobRef}</div>
            <div style={{ fontSize: 13, color: 'var(--text-muted)' }}>{job.componentType}</div>
          </div>
          <StatusPill status={job.status} isLate={job.isLate} />
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
          <Field label="Job Reference" value={job.jobRef} />
          <Field label="Job Structure Name" value={job.jobRef} />
          <Field label="Product" value={job.componentType} />
          <Field label="SCH component type" value={job.schComponentType} />
          <Field label="Client Name" value={job.clientName} />
          <Field label="Site Address" value={job.siteAddress} />
          <Field label="Site Suburb" value={job.siteSuburb} />
          <Field label="Supervisor Name" value={job.supervisorName} />
          <Field label="Supervisor Number" value={job.supervisorNumber} />
          <Field label="Job Order Date" value={job.jobOrderDate} />
          <Field label="Production Value (Eq)" value={job.totalEqA} />
          <Field label="Estimated Start Date" value={job.estimatedStartDate?.replace('T', ' ')} />
          <Field label="Estimated Completion Date" value={job.estimatedCompletionDate?.replace('T', ' ')} />
          <Field label="Expected Delivery Date" value={job.expectedDeliveryDate} />
          <Field label="Booked Delivery Date" value={job.bookedDeliveryDate} />
          <Field label="Invoice Number" value={job.invoiceNumber} />
          <Field label="Delivered" value={job.delivered ? 'Yes' : 'No'} />
        </div>

        {job.totalEqA ? (
          <div style={{ marginTop: 16 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, color: 'var(--text-muted)', marginBottom: 4 }}>
              <span>Production progress</span>
              <span>
                {job.completedEqA.toFixed(1)} / {job.totalEqA} Eq ({Math.min(Math.round((job.completedEqA / job.totalEqA) * 100), 100)}%)
              </span>
            </div>
            <div style={{ height: 8, background: 'var(--border-subtle)', borderRadius: 4 }}>
              <div style={{ height: 8, width: `${Math.min((job.completedEqA / job.totalEqA) * 100, 100)}%`, background: 'var(--brand-primary)', borderRadius: 4 }} />
            </div>
          </div>
        ) : null}

        {job.isManuallyScheduled && (
          <div style={{ marginTop: 12, fontSize: 12, color: '#0e7490' }}>Manually scheduled for {job.manualScheduleDate} — the scheduler will not move it.</div>
        )}

        {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>
          {user?.isAdmin && job.status === 'ready' && onApproveForCutting && (
            <button className="btn btn-primary" onClick={approve} disabled={busy}>
              Approve for cutting
            </button>
          )}
          {user?.canReschedule && onScheduleManually && (
            <button className="btn btn-secondary" onClick={onScheduleManually}>
              {job.isManuallyScheduled ? 'Change pin' : 'Schedule manually'}
            </button>
          )}
          {user?.isAdmin && onEdit && (
            <button className="btn btn-secondary" onClick={onEdit}>
              Edit
            </button>
          )}
          <button className="btn btn-secondary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
