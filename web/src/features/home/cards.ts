import type { Feature } from '../../app/features'
import { permissions } from '../../app/permissions'

/**
 * Home as a list of cards (OP-30), chosen like the navigation (OP-01): each card names the
 * permission it needs and, until the task that brings its data is done, the feature flag that
 * hides it. Someone in HR and Payroll gets both sets on one page. Code checks permission keys,
 * never group or role names; the API checks every call. Cards showing pay amounts need `pay.view` (ACC-05).
 */
export interface HomeCard { key: string; permission?: string; feature?: Feature }

const p = permissions
export const homeCards: HomeCard[] = [
  { key: 'addEmployee', permission: p.employees.manage },
  { key: 'employees', permission: p.employees.view },
  { key: 'joinedThisMonth', permission: p.employees.view },
  { key: 'readyForPayroll', permission: p.employees.view },
  // Later cards, each added by the task that brings its data.
  { key: 'waitingForYou', feature: 'approvals' },
  { key: 'offToday', permission: p.leave.view, feature: 'whosOff' },
  { key: 'whosOffThisWeek', permission: p.leave.view, feature: 'whosOff' },
  { key: 'joinerChecklists', permission: p.employees.view, feature: 'checklists' },
  { key: 'midYearReviews', permission: p.employees.view, feature: 'performance' },
  { key: 'announcements', permission: p.employees.view, feature: 'announcements' },
  { key: 'belowMinimumWage', permission: p.pay.view, feature: 'payRuns' },
]

type Can = (permission: string, companyId?: string) => boolean

/** The cards this person gets for the chosen company, in order. */
export function visibleCards(can: Can, flagOn: (f: Feature) => boolean, companyId?: string): HomeCard[] {
  return homeCards.filter(c => (!c.feature || flagOn(c.feature)) && (!c.permission || can(c.permission, companyId)))
}
