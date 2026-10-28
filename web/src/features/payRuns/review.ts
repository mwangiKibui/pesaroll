import type { components } from '../../api/schema'

type Inputs = components['schemas']['RunTimeResponse']
type Line = components['schemas']['PayRunLineResponse']

/** The statutory deductions a person can have in a run, in the API's order. */
export const deductionGroups = ['paye', 'shif', 'nssf', 'housing_levy', 'nita'] as const

/**
 * What a line is changed through (PAY-04): basic pay is the entry without an item (null), a
 * statutory line its locked item, anything else entered by amount its item. Owed lines, and items
 * paid by the hour or day (entered in the grid), can't be changed here (undefined).
 */
export function targetOf(line: Line, data: Inputs): string | null | undefined {
  if (line.owedFrom) return undefined
  const items = data.amountItems ?? []
  if (line.statutoryKey) return items.find(i => i.statutoryKey === line.statutoryKey)?.payItemId
  if (line.code === 'BASIC') return null
  return items.find(i => !i.statutoryKey && i.code === line.code)?.payItemId
}

/** What's entered for a person and item now, if anything. */
export const entryFor = (data: Inputs, employeeId: string, payItemId: string | null) =>
  data.people.find(p => p.employeeId === employeeId)?.entries.find(e => e.payItemId === payItemId)

/** Items that can be added for this run alone: the organization's own that the person isn't paid this run. */
export const addable = (data: Inputs, lines: Line[]) =>
  (data.amountItems ?? []).filter(i => !i.statutoryKey && !lines.some(l => !l.owedFrom && l.code === i.code))

/** "12.50" is an amount; "", "-1" and "1.234" aren't. */
export function amountOf(text: string): number | null {
  const n = Number(text.replace(/,/g, '').trim())
  return text.trim() && Number.isFinite(n) && n >= 0 && Math.round(n * 100) === n * 100 ? n : null
}
