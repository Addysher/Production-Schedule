import { useAuth } from '../auth/AuthContext'
import type { JobDetailDto } from '../types/api'
import { StatusPill } from './StatusPill'

function Field({ label, value }: { label: string; value: string | number | null | undefined }) {
  return (
    <div>
      <div style={{ fontSize: 11, color: '#94a3b8', textTransform: 'uppercase', fontWeight: 600 }}>{label}</div>
      <div style={{ fontSize: 14, color: '#1e293b' }}>{value ?? '—'}</div>
    </div>
  )
}

export function JobDetailPopup({
  job,
  onClose,
  onEdit,
  onApproveForCutting,
}: {
  job: JobDetailDto
  onClose: () => void
  onEdit?: () => void
  onApproveForCutting?: () => void
}) {
  const { user } = useAuth()

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
            <div style={{ fontSize: 13, color: '#64748b' }}>{job.componentType}</div>
          </div>
          <StatusPill status={job.status} isLate={job.isLate} />
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
          <Field label="Job Reference" value={job.jobRef} />
          <Field label="Job Structure Name" value={job.jobRef} />
          <Field label="Component Type" value={job.componentType} />
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

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 20 }}>
          {user?.isAdmin && job.status === 'ready' && onApproveForCutting && (
            <button className="btn btn-primary" onClick={onApproveForCutting}>
              Approve for cutting
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
