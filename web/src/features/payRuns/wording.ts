import { shortDate } from '../schedules/wording'

/** "October 2026" from "2026-10-01". */
export const monthName = (iso: string) =>
  new Intl.DateTimeFormat('en-GB', { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(`${iso}T00:00:00Z`))

/** "10 Oct to 16 Oct 2026", with both years when they differ. */
export function datesLabel(start: string, end: string) {
  const from = shortDate(start)
  return start.slice(0, 4) === end.slice(0, 4) ? `${from.slice(0, from.lastIndexOf(' '))} to ${shortDate(end)}` : `${from} to ${shortDate(end)}`
}

/** A run's period: "October 2026" for a calendar month, else its dates. */
export function periodLabel(start: string, end: string) {
  const [y, m, d] = start.split('-').map(Number)
  const lastOfMonth = new Date(Date.UTC(y, m, 0)).toISOString().slice(0, 10)
  return d === 1 && end === lastOfMonth ? monthName(start) : datesLabel(start, end)
}

/** "2026-10" for the month a date is in, months from it (-1 last month). */
export const monthOf = (iso: string, months = 0) => {
  const [y, m] = iso.split('-').map(Number)
  const at = new Date(Date.UTC(y, m - 1 + months, 1))
  return at.toISOString().slice(0, 7)
}


/** How a status looks in a list: draft and working out plain, waiting amber, approved and paid green. */
export const statusTone = (status: string) =>
  (({ awaiting_approval: 'warning', approved: 'brand', paid: 'brand', calculated: 'brand' }) as Record<string, 'warning' | 'brand'>)[status] ?? 'neutral'

/** Runs not worked out yet have no totals to show. */
export const hasTotals = (status: string) => !['draft', 'calculating'].includes(status)
