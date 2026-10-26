// A job with no booked delivery date: pencilled into the schedule by its rough date, but not cut or
// produced until the office books delivery (v3.0).
export function TbaTag() {
  return (
    <span
      title="No booked delivery date yet: pencilled in by its expected date, and not cut or produced until it's booked"
      style={{ fontSize: 10, fontWeight: 700, letterSpacing: 0.5, padding: '1px 5px', borderRadius: 3, border: '1px dashed currentColor', marginLeft: 6, whiteSpace: 'nowrap' }}
    >
      TBA
    </span>
  )
}
