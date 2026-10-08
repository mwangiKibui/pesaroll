/** The ten setup steps, in order (MVP 1 onboarding spec). The last two are optional. */
export const setupSteps = [
  { key: 'organization', optional: false },
  { key: 'companies', optional: false },
  { key: 'locations', optional: false },
  { key: 'structure', optional: false },
  { key: 'schedules', optional: false },
  { key: 'payItems', optional: false },
  { key: 'leavePlans', optional: false },
  { key: 'employees', optional: false },
  { key: 'history', optional: true },
  { key: 'team', optional: true },
] as const

export type SetupStep = (typeof setupSteps)[number]['key']
