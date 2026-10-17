/**
 * People → Employees' filters (OP-07), kept in the page's address so other pages can link to them,
 * e.g. the HR home's "Fix them": /people/employees?missing=kra_pin, or ?status=leaving.
 */

/** Who to show: still here (leaving ones too), leaving (a date set), left (the API's "former"), or everyone. */
export const listStatuses = ['current', 'leaving', 'former', 'all'] as const
export type Status = (typeof listStatuses)[number]

/** What payroll still needs (the API's readiness keys), anything (not ready) or nothing (ready). */
export const missingFilters = ['none', 'any', 'kra_pin', 'national_id', 'basic_pay', 'payment_account'] as const
export type Missing = (typeof missingFilters)[number]

export interface ListFilters { status: Status; missing: Missing | null }

/** The address's search as filters; anything unknown is left out. */
export function readFilters(search: Record<string, unknown>): { status?: Status; missing?: Missing } {
  const status = listStatuses.find(s => s === search.status)
  const missing = missingFilters.find(m => m === search.missing)
  return { ...(status ? { status } : {}), ...(missing ? { missing } : {}) }
}
