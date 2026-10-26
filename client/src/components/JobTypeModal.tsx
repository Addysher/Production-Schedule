import { useState } from 'react'
import { jobTypesApi } from '../api/admin'
import type { JobTypeDto } from '../types/api'
import { Field, Modal, errorText } from './Modal'
import { ProductSelect } from './ProductSelect'

export function JobTypeModal({ jobType, onClose, onSaved }: { jobType?: JobTypeDto; onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(jobType?.name ?? '')
  const [componentType, setComponentType] = useState(jobType?.componentType ?? '')
  const [minsPerUnit, setMinsPerUnit] = useState(jobType?.minsPerUnit?.toString() ?? '')
  const [isActive, setIsActive] = useState(jobType?.isActive ?? true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function save() {
    if (!name.trim() || !(Number(minsPerUnit) > 0)) return setError('A name and a cycle time above zero are required.')
    setSaving(true)
    setError(null)
    const body = { name: name.trim(), componentType: componentType.trim() || null, minsPerUnit: Number(minsPerUnit), isActive }
    try {
      if (jobType) await jobTypesApi.update(jobType.id, body)
      else await jobTypesApi.create(body)
      onSaved()
    } catch (e) {
      setError(errorText(e, 'Could not save the job type.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      title={jobType ? `Edit ${jobType.name}` : 'Add job type'}
      onClose={onClose}
      footer={
        <>
          <button className="btn btn-secondary" onClick={onClose} disabled={saving}>
            Cancel
          </button>
          <button className="btn btn-primary" onClick={save} disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </>
      }
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <Field label="Name">
          <input style={{ width: '100%' }} value={name} onChange={(e) => setName(e.target.value)} autoFocus />
        </Field>
        <Field label="Product" hint="Sets the cycle time for this product's jobs on the schedule">
          <ProductSelect value={componentType} onChange={setComponentType} allowNone />
        </Field>
        <Field label="Minutes per unit" hint="Cycle time per EqA — drives job durations in both the schedule and the Quote Estimator">
          <input type="number" step="0.1" style={{ width: '100%' }} value={minsPerUnit} onChange={(e) => setMinsPerUnit(e.target.value)} />
        </Field>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
          <span style={{ fontSize: 13 }}>Active</span>
        </label>
      </div>
      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}
