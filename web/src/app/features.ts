/**
 * Feature flags (OP-01): one per page or card that isn't built yet, so the shell can ship and fill in
 * task by task. Off unless switched on in `VITE_FEATURES` (a comma list, or "all"), so production
 * shows only finished pages. A task that finishes a page removes its flag.
 */
export const features = [
  'approvals', 'tasks', 'notifications',
  'payRuns', 'payouts', 'expenses', 'filings',
  'orgChart', 'attendance', 'performance', 'checklists', 'announcements', 'surveys',
  'leaveRequests', 'whosOff', 'holidays',
  'workflows', 'reports', 'settings', 'billing',
  'employeePortal',
  // People → Employees in the app (OP-07, /people/employees): Home's "Fix them" and number links go there once it's on.
  'employees',
] as const

export type Feature = (typeof features)[number]

export function featureOn(name: Feature, setting: string | undefined = import.meta.env.VITE_FEATURES): boolean {
  const on = (setting ?? '').split(',').map(f => f.trim().toLowerCase()).filter(Boolean)
  return on.includes('all') || on.includes(name.toLowerCase())
}
