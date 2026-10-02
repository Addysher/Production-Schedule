import { useEffect, useState } from 'react'
import { jobTypesApi } from '../api/admin'
import { quotesApi } from '../api/quotes'
import type { JobTypeDto, QuoteEstimateResponse, QuoteLineRequest } from '../types/api'

interface LineRow extends QuoteLineRequest {
  key: number
}

let nextKey = 1

export function QuotesView() {
  const [jobTypes, setJobTypes] = useState<JobTypeDto[]>([])
  const [ref, setRef] = useState('')
  const [lines, setLines] = useState<LineRow[]>([{ key: nextKey++, jobTypeId: 0, qty: 1 }])
  const [result, setResult] = useState<QuoteEstimateResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    jobTypesApi
      .getAll()
      .then((types) => {
        const active = types.filter((t) => t.isActive)
        setJobTypes(active)
        if (active.length > 0) {
          setLines([{ key: nextKey++, jobTypeId: active[0].id, qty: 1 }])
        }
      })
      .catch(() => {})
  }, [])

  function addLine() {
    setLines([...lines, { key: nextKey++, jobTypeId: jobTypes[0]?.id ?? 0, qty: 1 }])
  }

  function removeLine(key: number) {
    setLines(lines.filter((l) => l.key !== key))
  }

  function updateLine(key: number, patch: Partial<LineRow>) {
    setLines(lines.map((l) => (l.key === key ? { ...l, ...patch } : l)))
  }

  async function submit() {
    setError(null)
    setResult(null)
    if (!ref.trim() || lines.length === 0 || lines.some((l) => !l.jobTypeId || l.qty < 1)) {
      setError('Enter a quote reference and at least one valid line item.')
      return
    }
    setLoading(true)
    try {
      const response = await quotesApi.estimate({ ref, lines: lines.map(({ jobTypeId, qty }) => ({ jobTypeId, qty })) })
      setResult(response)
    } catch {
      setError('Could not estimate this quote.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div>
      <h2 style={{ marginTop: 0 }}>Quotes</h2>

      <div className="card" style={{ maxWidth: 640 }}>
        <div style={{ fontWeight: 700, marginBottom: 12 }}>Quote Estimator</div>
        <div style={{ fontSize: 12, color: 'var(--text-faint)', marginBottom: 16 }}>
          Time-based only — queue length + job duration × quantity. Setup times and operator availability are
          not factored in.
        </div>

        <label>
          <div style={{ fontSize: 12, fontWeight: 600, marginBottom: 4 }}>Quote Reference</div>
          <input style={{ width: '100%' }} value={ref} onChange={(e) => setRef(e.target.value)} />
        </label>

        <div style={{ marginTop: 16, display: 'flex', flexDirection: 'column', gap: 8 }}>
          {lines.map((line) => (
            <div key={line.key} style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
              <select
                style={{ flex: 1 }}
                value={line.jobTypeId}
                onChange={(e) => updateLine(line.key, { jobTypeId: Number(e.target.value) })}
              >
                {jobTypes.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
              <input
                type="number"
                min={1}
                style={{ width: 80 }}
                value={line.qty}
                onChange={(e) => updateLine(line.key, { qty: Number(e.target.value) })}
              />
              <button className="btn btn-secondary" onClick={() => removeLine(line.key)} disabled={lines.length === 1}>
                &times;
              </button>
            </div>
          ))}
        </div>

        <button className="btn btn-secondary" style={{ marginTop: 12 }} onClick={addLine}>
          + Add line
        </button>

        {error && <div style={{ color: '#dc2626', fontSize: 13, marginTop: 12 }}>{error}</div>}

        <div style={{ marginTop: 20 }}>
          <button className="btn btn-primary" onClick={submit} disabled={loading}>
            {loading ? 'Estimating…' : 'Estimate'}
          </button>
        </div>
      </div>

      {result && (
        <div className="card" style={{ maxWidth: 640, marginTop: 16 }}>
          <div style={{ fontWeight: 700, marginBottom: 12 }}>Results for {result.ref}</div>
          <table>
            <thead>
              <tr>
                <th>Job Type</th>
                <th>Qty</th>
                <th>Estimated Completion</th>
              </tr>
            </thead>
            <tbody>
              {result.lines.map((l, i) => (
                <tr key={i}>
                  <td>{l.jobTypeName}</td>
                  <td>{l.qty}</td>
                  <td>{new Date(l.estimatedCompletion).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {result.lines.length > 1 && (
            <div style={{ marginTop: 12, fontWeight: 700, color: 'var(--brand-primary)' }}>
              Full order ready: {new Date(result.fullOrderReadyAt).toLocaleString()}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
