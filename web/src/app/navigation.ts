import i18n from '../i18n'
import { matches } from '../components/search'
import type { Feature } from './features'
import { permissions } from './permissions'

/**
 * The organization portal's pages as data (OP-01): each link names the permission it needs (an
 * area's View, ACC-04) and, until its page is built, the feature flag that hides it. A link shows
 * when the person may open it for the chosen company and its flag (if any) is on; a section with
 * nothing to show is hidden. Labels and search words come from `nav.links.<key>` and
 * `nav.words.<key>` in the translations. Hiding is never the protection: the API checks every call.
 * Pages that show people's pay amounts (pay runs, payouts, filings) need `pay.view` (ACC-05).
 */
export interface NavLink { key: string; href: string; permission?: string; feature?: Feature }
export interface NavSection { key: string; links: NavLink[] }

const p = permissions
export const navigation: NavSection[] = [
  { key: 'main', links: [
    { key: 'home', href: '/' },
    { key: 'approvals', href: '/approvals', feature: 'approvals' },
    { key: 'tasks', href: '/tasks', feature: 'tasks' },
  ] },
  { key: 'pay', links: [
    { key: 'payRuns', href: '/pay/runs', permission: p.pay.view, feature: 'payRuns' },
    { key: 'earnings', href: '/setup/earnings-and-deductions', permission: p.payItems.view },
    { key: 'payouts', href: '/pay/payouts', permission: p.pay.view, feature: 'payouts' },
    { key: 'expenses', href: '/pay/expenses', permission: p.payItems.view, feature: 'expenses' },
    { key: 'filings', href: '/pay/filings', permission: p.pay.view, feature: 'filings' },
    { key: 'payrollHistory', href: '/setup/payroll-history', permission: p.payrollHistory.view },
  ] },
  { key: 'people', links: [
    { key: 'employees', href: '/setup/employees', permission: p.employees.view },
    { key: 'orgChart', href: '/people/org-chart', permission: p.employees.view, feature: 'orgChart' },
    { key: 'attendance', href: '/people/attendance', permission: p.employees.view, feature: 'attendance' },
    { key: 'performance', href: '/people/performance', permission: p.employees.view, feature: 'performance' },
    { key: 'checklists', href: '/people/joiners-and-leavers', permission: p.employees.view, feature: 'checklists' },
    { key: 'announcements', href: '/people/announcements', permission: p.employees.view, feature: 'announcements' },
    { key: 'surveys', href: '/people/surveys', permission: p.employees.view, feature: 'surveys' },
  ] },
  { key: 'leave', links: [
    { key: 'leaveRequests', href: '/leave/requests', permission: p.leave.view, feature: 'leaveRequests' },
    { key: 'whosOff', href: '/leave/whos-off', permission: p.leave.view, feature: 'whosOff' },
    { key: 'holidays', href: '/leave/holidays', permission: p.leave.view, feature: 'holidays' },
  ] },
  { key: 'organization', links: [
    { key: 'companies', href: '/setup/companies', permission: p.companies.view },
    { key: 'locations', href: '/setup/locations', permission: p.locations.view },
    { key: 'structure', href: '/setup/structure', permission: p.structure.view },
    { key: 'schedules', href: '/setup/schedules', permission: p.schedules.view },
    { key: 'leavePlans', href: '/setup/leave-plans', permission: p.leave.view },
    { key: 'workflows', href: '/organization/workflows', permission: p.companies.manage, feature: 'workflows' },
  ] },
  { key: 'reports', links: [
    { key: 'reports', href: '/reports', feature: 'reports' },
  ] },
  { key: 'admin', links: [
    { key: 'team', href: '/setup/team', permission: p.team.view },
    { key: 'setup', href: '/setup/organization', permission: p.companies.manage },
    { key: 'settings', href: '/admin/settings', permission: p.companies.manage, feature: 'settings' },
    { key: 'billing', href: '/admin/billing', permission: p.companies.manage, feature: 'billing' },
  ] },
]

type Can = (permission: string, companyId?: string) => boolean

const allowed = (link: NavLink, can: Can, flagOn: (f: Feature) => boolean, companyId?: string) =>
  (!link.feature || flagOn(link.feature)) && (!link.permission || can(link.permission, companyId))

/** The sections and links this person may see, for the chosen company. */
export function visibleNavigation(can: Can, flagOn: (f: Feature) => boolean, companyId?: string): NavSection[] {
  return navigation
    .map(s => ({ ...s, links: s.links.filter(l => allowed(l, can, flagOn, companyId)) }))
    .filter(s => s.links.length > 0)
}

/** Pages this person may open whose name or words match ("payroll", "leave", "sacco"); nothing for an empty search. */
export function searchPages(query: string, can: Can, flagOn: (f: Feature) => boolean, companyId?: string): NavLink[] {
  if (!query.trim()) return []
  return visibleNavigation(can, flagOn, companyId).flatMap(s => s.links)
    .filter(l => matches(`${i18n.t(`nav.links.${l.key}`)} ${i18n.t(`nav.words.${l.key}`)}`, query))
}
