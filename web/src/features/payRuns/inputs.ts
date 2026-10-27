import type { components } from '../../api/schema'

type Inputs = components['schemas']['RunTimeResponse']

/** A column of the inputs grid: days or hours at a person's own rate, an item paid by the hour or day, or an amount entered each run. */
export type Column =
  | { kind: 'basic'; id: 'basic' }
  | { kind: 'time'; id: string; name: string; unit: string }
  | { kind: 'amount'; id: string; name: string }

/**
 * Basic pay's days or hours (when anyone is paid by them), each item paid by the hour or day, then
 * each item entered each run. Fixed items, percentages and statutory lines are changed in review instead.
 */
export function columnsOf(data: Inputs): Column[] {
  return [
    ...(data.people.some(p => p.unit) ? [{ kind: 'basic', id: 'basic' } as const] : []),
    ...data.items.map(i => ({ kind: 'time', id: i.payItemId, name: i.name, unit: i.unit }) as const),
    ...(data.amountItems ?? []).filter(i => i.calcMethod === 'each_run' && !i.statutoryKey).map(i => ({ kind: 'amount', id: i.payItemId, name: i.name }) as const),
  ]
}

export const keyOf = (employeeId: string, c: Column) => `${employeeId}:${c.id}`

/** What's entered now, as typed text ("" for nothing). */
export function startingValue(data: Inputs, employeeId: string, c: Column) {
  const entry = data.people.find(p => p.employeeId === employeeId)?.entries.find(e => e.payItemId === (c.kind === 'basic' ? null : c.id))
  const value = c.kind === 'amount' ? entry?.amount : entry?.quantity
  return value == null ? '' : String(Number(value))
}

const number = (text: string) => Number(text.replace(/,/g, '').trim())
const twoDecimals = (n: number) => Math.round(n * 100) === n * 100

/** The API's limits, checked before saving: days or hours from 0 to 744 (a 31-day month), amounts of 0 or more; two decimals at most. */
export function checkCell(c: Column, text: string): 'quantityInvalid' | 'amountInvalid' | null {
  if (!text.trim()) return null
  const n = number(text)
  if (c.kind === 'amount') return Number.isFinite(n) && n >= 0 && twoDecimals(n) ? null : 'amountInvalid'
  return Number.isFinite(n) && n >= 0 && n <= 744 && twoDecimals(n) ? null : 'quantityInvalid'
}

/** One entry to send: nothing typed removes what was entered. */
export function entryBody(employeeId: string, c: Column, text: string) {
  const n = text.trim() ? number(text) : null
  return { employeeId, payItemId: c.kind === 'basic' ? null : c.id, quantity: c.kind === 'amount' ? null : n, amount: c.kind === 'amount' ? n : null }
}
