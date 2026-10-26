import { useEffect, useState } from 'react'
import { jobsApi } from '../api/jobs'
import { stationsApi } from '../api/stations'
import type { JobDetailDto, StationDto } from '../types/api'
import { localIsoDate } from '../utils/dates'
import { Field, Modal, errorText } from './Modal'

// Pin a job to a station and date (§3.4); the scheduler then leaves it exactly there. Only stations of the
// job's component type are offered — §4.4 says a job can't run anywhere else.
export function ScheduleManuallyModal({ job, onClose, onDone }: { job: JobDetailDto; onClose: () => void; onDone: () => void }) {
  const [stations, setStations] = useState<StationDto[]>([])
  const [stationId, setStationId] = useState<number | ''>(job.manualStationId ?? '')
  const [date, setDate] = useState(job.manualScheduleDate ?? (job.estimatedStartDate ? job.estimatedStartDate.slice(0, 10) : localIsoDate(new Date())))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    stationsApi.getAll().then((all) => {
      const compatible = all.filter((s) => s.isActive && s.componentType === job.componentType)
      setStations(compatible)
      setStationId((current) => (current !== '' ? current : (compatible[0]?.id ?? '')))
    })
  }, [job.componentType])

  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      onDone()
    } catch (e) {
      setError(errorText(e, 'The request failed.'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      title={`Schedule ${job.jobRef} manually`}
      width={400}
      onClose={onClose}
      footer={
        <>
          {job.isManuallyScheduled && (
            <button className="btn btn-danger" disabled={busy} onClick={() => run(() => jobsApi.unpinSchedule(job.jobRef))}>
              Unpin
            </button>
          )}
          <button className="btn btn-secondary" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button
            className="btn btn-primary"
            disabled={busy || stationId === '' || !date}
            onClick={() => run(() => jobsApi.scheduleManually(job.jobRef, Number(stationId), date))}
          >
            {job.isManuallyScheduled ? 'Move' : 'Pin'}
          </button>
        </>
      }
    >
      {job.isManuallyScheduled && (
        <div style={{ fontSize: 12, color: '#0e7490', marginBottom: 12 }}>Currently pinned for {job.manualScheduleDate}.</div>
      )}
      {stations.length === 0 ? (
        <div style={{ fontSize: 13, color: '#b45309' }}>
          There is no active station for the product &lsquo;{job.componentType}&rsquo;, so this job can&rsquo;t be pinned.
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          <Field label="Station">
            <select style={{ width: '100%' }} value={stationId} onChange={(e) => setStationId(Number(e.target.value))}>
              {stations.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Date" hint="The job starts at the beginning of this working day">
            <input type="date" style={{ width: '100%' }} value={date} onChange={(e) => setDate(e.target.value)} />
          </Field>
        </div>
      )}
      {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}
    </Modal>
  )
}
