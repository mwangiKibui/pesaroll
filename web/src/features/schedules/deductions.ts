import type { components } from '../../api/schema'

type Choice = components['schemas']['DeductionChoiceResponse']

/** The statutory deductions a schedule can make (same keys as the API), in the usual order. */
export const statutoryItems = ['paye', 'shif', 'nssf', 'housing_levy', 'nita'] as const

/** A choice being made in the drawer: saved with the schedule, from its date. */
export interface DeductionsDraft { makes: boolean; items: string[]; appliesTo: 'everyone' | 'chosen'; employeeIds: string[]; startsOn: string }

export type DeductionErrors = Partial<Record<'items' | 'employeeIds' | 'startsOn', string>>

/** Today in Kenya, "2026-10-27": a new choice applies from today unless they pick another date. */
export const todayInKenya = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Nairobi' }).format(new Date())

/** The draft for the choice in force (or none yet). */
export const draftFrom = (current: Choice | null, startsOn: string): DeductionsDraft => ({
  makes: current?.makesDeductions ?? false,
  items: current?.makesDeductions ? [...current.items] : [],
  appliesTo: current?.appliesTo === 'chosen' ? 'chosen' : 'everyone',
  employeeIds: [...(current?.employeeIds ?? [])],
  startsOn,
})

/** Whether two drafts choose the same (the date alone isn't a new choice). */
export const sameChoice = (a: DeductionsDraft, b: DeductionsDraft) =>
  a.makes === b.makes && (!a.makes || (a.items.join() === b.items.join() && a.appliesTo === b.appliesTo
    && (a.appliesTo === 'everyone' || [...a.employeeIds].sort().join() === [...b.employeeIds].sort().join())))

/** The same checks as the API. */
export function checkDeductions(d: DeductionsDraft, t: (key: string) => string): DeductionErrors {
  const errors: DeductionErrors = {}
  if (d.makes && d.items.length === 0) errors.items = t('schedules.statutory.itemsRequired')
  if (d.makes && d.appliesTo === 'chosen' && d.employeeIds.length === 0) errors.employeeIds = t('schedules.statutory.employeesRequired')
  if (!d.startsOn) errors.startsOn = t('schedules.statutory.startsOnRequired')
  return errors
}

/** What the API is sent. */
export const deductionsBody = (d: DeductionsDraft) => ({
  makesDeductions: d.makes,
  items: d.makes ? statutoryItems.filter(i => d.items.includes(i)) : [],
  appliesTo: d.makes ? d.appliesTo : 'everyone',
  employeeIds: d.makes && d.appliesTo === 'chosen' ? d.employeeIds : null,
  startsOn: d.startsOn,
})
