import type { components } from '../api/schema'
import { useMe } from '../setup/hooks'

type Grant = components['schemas']['AccessGrant']

const area = <A extends string>(key: A) => ({ view: `${key}.view`, manage: `${key}.manage` }) as const

/**
 * The permission keys the app checks (ACC-04), the same as the API's `Permissions`: each area has
 * View and Manage, and Manage brings View. Code checks keys, never group or role names.
 */
export const permissions = {
  companies: area('companies'),
  employees: area('employees'),
  locations: area('locations'),
  structure: area('structure'),
  leave: area('leave'),
  schedules: area('schedules'),
  payItems: area('pay_items'),
  paymentAccounts: area('payment_accounts'),
  payrollHistory: area('payroll_history'),
  team: area('team'),
}

/**
 * Whether these grants allow a permission: for this company, or without one, for any company.
 * Only for showing or hiding things; the server checks every request.
 */
export function canDo(grants: Grant[] | undefined, permission: string, companyId?: string) {
  return (grants ?? []).some(g => g.permission === permission && (companyId === undefined || g.allCompanies || g.companyIds.includes(companyId)))
}

/** What the signed-in person may do, from their groups (`/api/auth/me`). */
export function usePermissions() {
  const me = useMe()
  const grants = me.data?.permissions
  return { ready: me.data !== undefined, can: (permission: string, companyId?: string) => canDo(grants, permission, companyId) }
}
