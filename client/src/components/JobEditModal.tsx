import { useState } from 'react'
import type { JobDetailDto, UpdateJobRequest } from '../types/api'
import { Field, Modal, errorText } from './Modal'
import { ProductSelect } from './ProductSelect'

// Every field the office can maintain by hand. The form is sent whole: clearing a box clears that
// value on the job (re-imports never overwrite these, §4.4).
type Form = {
  componentType: string
  clientName: string
  clientRefNumber: string
  siteAddress: string
  siteSuburb: string
  supervisorName: string
  supervisorNumber: string
  expectedDeliveryDate: string
  estBookedDeliveryDate: string
  bookedDeliveryDate: string
  invoiceNumber: string
  delivered: boolean
}

const toForm = (job: JobDetailDto): Form => ({
  componentType: job.componentType ?? '',
  clientName: job.clientName ?? '',
  clientRefNumber: job.clientRefNumber ?? '',
  siteAddress: job.siteAddress ?? '',
  siteSuburb: job.siteSuburb ?? '',
  supervisorName: job.supervisorName ?? '',
  supervisorNumber: job.supervisorNumber ?? '',
  expectedDeliveryDate: job.expectedDeliveryDate ?? '',
  estBookedDeliveryDate: job.estBookedDeliveryDate ?? '',
  bookedDeliveryDate: job.bookedDeliveryDate ?? '',
  invoiceNumber: job.invoiceNumber ?? '',
  delivered: job.delivered,
})

// An empty date box means "no date"; the API can't parse "" as one.
const toRequest = (f: Form): UpdateJobRequest => ({
  componentType: f.componentType || null,
  clientName: f.clientName,
  clientRefNumber: f.clientRefNumber,
  siteAddress: f.siteAddress,
  siteSuburb: f.siteSuburb,
  supervisorName: f.supervisorName,
  supervisorNumber: f.supervisorNumber,
  expectedDeliveryDate: f.expectedDeliveryDate || null,
  estBookedDeliveryDate: f.estBookedDeliveryDate || null,
  bookedDeliveryDate: f.bookedDeliveryDate || null,
  invoiceNumber: f.invoiceNumber,
  delivered: f.delivered,
})

export function JobEditModal({
  job,
  onClose,
  onSave,
}: {
  job: JobDetailDto
  onClose: () => void
  onSave: (request: UpdateJobRequest) => Promise<void>
}) {
  const [form, setForm] = useState<Form>(() => toForm(job))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => ({ ...f, [key]: value }))
  const text = (key: Exclude<keyof Form, 'delivered'>, type = 'text') => (
    <input type={type} style={{ width: '100%' }} value={form[key]} onChange={(e) => set(key, e.target.value)} />
  )

  async function handleSave() {
    setSaving(true)
    setError(null)
    try {
      await onSave(toRequest(form))
      onClose()
    } catch (e) {
      setError(errorText(e, 'Could not save changes.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      title={`Edit ${job.jobRef}`}
      onClose={onClose}
      width={620}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={handleSave} disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </>
      }
    >
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 12 }}>
        <Field label="Product" hint={job.schComponentType && job.schComponentType !== job.componentType ? `The design file says '${job.schComponentType}'` : undefined}>
          <ProductSelect value={form.componentType} onChange={(v) => set('componentType', v)} />
        </Field>
        <Field label="Client">{text('clientName')}</Field>
        <Field label="Client Ref Number">{text('clientRefNumber')}</Field>
        <Field label="Invoice Number">{text('invoiceNumber')}</Field>
        <Field label="Site Address">{text('siteAddress')}</Field>
        <Field label="Site Suburb">{text('siteSuburb')}</Field>
        <Field label="Supervisor Name">{text('supervisorName')}</Field>
        <Field label="Supervisor Number">{text('supervisorNumber')}</Field>
        <Field label="Expected Delivery Date">{text('expectedDeliveryDate', 'date')}</Field>
        <Field label="Est. Booked Delivery Date" hint="Drives the job's due date when set">
          {text('estBookedDeliveryDate', 'date')}
        </Field>
        <Field label="Booked Delivery Date">{text('bookedDeliveryDate', 'date')}</Field>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, alignSelf: 'end', paddingBottom: 6 }}>
          <input type="checkbox" checked={form.delivered} onChange={(e) => set('delivered', e.target.checked)} />
          <span style={{ fontSize: 13 }}>Delivered</span>
        </label>
      </div>
      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}

// The job as it stands, as an update request: for actions that change one thing (e.g. Delivered)
// without touching the rest.
export const updateRequestFrom = (job: JobDetailDto): UpdateJobRequest => toRequest(toForm(job))
