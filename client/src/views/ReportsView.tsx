import { localIsoDate } from '../utils/dates'
import { useEffect, useMemo, useState } from 'react'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { reportsApi } from '../api/reports'
import type { CompletionRowDto } from '../types/api'
import { errorText } from '../components/Modal'

function toCsv(headers: string[], rows: (string | number)[][]): string {
  const escape = (v: string | number) => {
    const s = String(v)
    return s.includes(',') || s.includes('"') ? `"${s.replace(/"/g, '""')}"` : s
  }
  return [headers, ...rows].map((r) => r.map(escape).join(',')).join('\n')
}

function downloadCsv(filename: string, csv: string) {
  const blob = new Blob([csv], { type: 'text/csv' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

export function ReportsView() {
  const [report, setReport] = useState<'production' | 'completed-late'>('production')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [dateFrom, setDateFrom] = useState(() => {
    const d = new Date()
    d.setDate(d.getDate() - 13)
    return localIsoDate(d)
  })
  const [dateTo, setDateTo] = useState(() => localIsoDate(new Date()))


  // Report 1: Daily Production Value by Station. Comes from when each member was actually completed, so a job
  // that ran over three days is credited to the days it was really produced, not all to its start day.
  const [productionRows, setProductionRows] = useState<{ station: string; date: string; value: number }[]>([])
  useEffect(() => {
    if (!dateFrom || !dateTo) return
    reportsApi
      .production(dateFrom, dateTo)
      .then((rows) => setProductionRows(rows.map((r) => ({ station: r.stationName, date: r.date.slice(0, 10), value: r.eqa }))))
      .catch((e) => {
        setProductionRows([])
        setLoadError(errorText(e, 'Could not load the production report.'))
      })
  }, [dateFrom, dateTo])

  const productionChartData = useMemo(() => {
    const byDate = new Map<string, Record<string, number | string>>()
    for (const row of productionRows) {
      const entry = byDate.get(row.date) ?? { date: row.date }
      entry[row.station] = row.value
      byDate.set(row.date, entry)
    }
    return Array.from(byDate.values()).sort((a, b) => String(a.date).localeCompare(String(b.date)))
  }, [productionRows])

  const stationNames = useMemo(() => Array.from(new Set(productionRows.map((r) => r.station))), [productionRows])

  // Report 2: Jobs Completed vs Completed Late, by the day each job actually finished. A job is late when
  // it finished on a later day than it was due (worked out on the server, archived jobs included).
  const [completedLateRows, setCompletedLateRows] = useState<CompletionRowDto[]>([])
  useEffect(() => {
    if (!dateFrom || !dateTo) return
    reportsApi
      .completions(dateFrom, dateTo)
      .then(setCompletedLateRows)
      .catch((e) => {
        setCompletedLateRows([])
        setLoadError(errorText(e, 'Could not load the completions report.'))
      })
  }, [dateFrom, dateTo])

  function exportCsv() {
    if (report === 'production') {
      downloadCsv(
        'production-by-station.csv',
        toCsv(
          ['Station', 'Date', 'Total Production Value'],
          productionRows.map((r) => [r.station, r.date, r.value.toFixed(2)]),
        ),
      )
    } else {
      downloadCsv(
        'jobs-completed-vs-late.csv',
        toCsv(
          ['Date', 'Jobs Completed', 'Completed Late', 'Late %', 'Late Jobs'],
          completedLateRows.map((r) => [r.date, r.completed, r.completedLate, r.latePercent, r.lateJobRefs.join(' ')]),
        ),
      )
    }
  }

  return (
    <div>
      <h2 style={{ marginTop: 0 }}>Reports</h2>
      {loadError && <div style={{ color: '#dc2626', fontSize: 13, marginBottom: 12 }}>{loadError}</div>}

      <div style={{ display: 'flex', gap: 12, alignItems: 'center', marginBottom: 16, flexWrap: 'wrap' }}>
        <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--brand-primary)' }}>
          <button
            className="btn"
            style={{ background: report === 'production' ? 'var(--brand-primary)' : 'var(--card-bg)', color: report === 'production' ? '#fff' : 'var(--brand-primary)', borderRadius: 0 }}
            onClick={() => setReport('production')}
          >
            Production by Station
          </button>
          <button
            className="btn"
            style={{ background: report === 'completed-late' ? 'var(--brand-primary)' : 'var(--card-bg)', color: report === 'completed-late' ? '#fff' : 'var(--brand-primary)', borderRadius: 0 }}
            onClick={() => setReport('completed-late')}
          >
            Completed vs Late
          </button>
        </div>

        <label style={{ fontSize: 12 }}>
          From <input type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} />
        </label>
        <label style={{ fontSize: 12 }}>
          To <input type="date" value={dateTo} onChange={(e) => setDateTo(e.target.value)} />
        </label>

        <button className="btn btn-secondary" onClick={exportCsv}>
          Export CSV
        </button>
      </div>

      {report === 'production' ? (
        <>
          <div className="card" style={{ height: 320, marginBottom: 16 }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={productionChartData}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" tick={{ fontSize: 11 }} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip />
                <Legend />
                {stationNames.map((s, i) => (
                  <Bar key={s} dataKey={s} fill={['#0a5e73', '#fdb913', '#22c55e', '#3b82f6', '#ef4444'][i % 5]} />
                ))}
              </BarChart>
            </ResponsiveContainer>
          </div>
          <div className="card">
            <table>
              <thead>
                <tr>
                  <th>Station</th>
                  <th>Date</th>
                  <th>Total Production Value</th>
                </tr>
              </thead>
              <tbody>
                {productionRows.map((r, i) => (
                  <tr key={i}>
                    <td>{r.station}</td>
                    <td>{r.date}</td>
                    <td>{r.value.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      ) : (
        <>
          <div className="card" style={{ height: 320, marginBottom: 16 }}>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={completedLateRows}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" tick={{ fontSize: 11 }} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip />
                <Legend />
                <Bar dataKey="completed" name="Completed" fill="#22c55e" />
                <Bar dataKey="completedLate" name="Completed late" fill="#ef4444" />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <div className="card">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Jobs Completed</th>
                  <th>Completed Late</th>
                  <th>Late %</th>
                  <th>Late Jobs</th>
                </tr>
              </thead>
              <tbody>
                {completedLateRows.map((r, i) => (
                  <tr key={i}>
                    <td>{r.date}</td>
                    <td>{r.completed}</td>
                    <td>{r.completedLate}</td>
                    <td>{r.latePercent}%</td>
                    <td style={{ fontSize: 12 }}>{r.lateJobRefs.join(', ') || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  )
}
