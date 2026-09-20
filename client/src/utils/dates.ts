// Calendar dates in the browser's local time. `toISOString()` is UTC, so slicing it gives the wrong
// day for the first hours of a factory morning (and the last hours of an evening) in any zone but UTC.
// The server sends site-local timestamps without an offset, which the browser also reads as local.
export function localIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}
