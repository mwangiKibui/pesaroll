/**
 * The ten setup steps, in order (MVP 1 onboarding spec). The last two are optional. Earnings and
 * deductions comes after Employees, since many deductions are for one employee (a SACCO, a loan).
 */
export const setupSteps = [
  { key: 'organization', optional: false },
  { key: 'companies', optional: false },
  { key: 'locations', optional: false },
  { key: 'structure', optional: false },
  { key: 'schedules', optional: false },
  { key: 'leavePlans', optional: false },
  { key: 'employees', optional: false },
  { key: 'payItems', optional: false },
  { key: 'history', optional: true },
  { key: 'team', optional: true },
] as const

export type SetupStep = (typeof setupSteps)[number]['key']

/**
 * What a step needs to be opened (ACC-04): the View permission of its area. The organization and
 * companies steps are for every member (everyone sees the companies they work with).
 */
export const stepPermission: Partial<Record<SetupStep, string>> = {
  locations: 'locations.view',
  structure: 'structure.view',
  schedules: 'schedules.view',
  leavePlans: 'leave.view',
  employees: 'employees.view',
  payItems: 'pay_items.view',
  history: 'payroll_history.view',
  team: 'team.view',
}

/** Steps with a page so far, in order; the rest come with their tasks (ONB-08 onwards). */
export const setupPaths: Partial<Record<SetupStep, string>> = {
  organization: '/setup/organization',
  companies: '/setup/companies',
  locations: '/setup/locations',
  structure: '/setup/structure',
  schedules: '/setup/schedules',
  payItems: '/setup/earnings-and-deductions',
  leavePlans: '/setup/leave-plans',
  employees: '/setup/employees',
  history: '/setup/payroll-history',
  team: '/setup/team',
}
