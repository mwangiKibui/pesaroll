import { useQuery } from '@tanstack/react-query'
import { useServices } from '../app/services'
import { setupPaths, setupSteps, type SetupStep } from './steps'

/** The person signed in, for the navbar. */
export function useMe() {
  const { api } = useServices()
  return useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      const { data, error } = await api.GET('/api/auth/me')
      if (error || !data) throw new Error('me')
      return data
    },
  })
}

export function useSignOut() {
  const { auth, queryClient } = useServices()
  return async () => {
    await auth.signOut()
    queryClient.clear()
  }
}

/** The signed-in person's organization. */
export function useOrganization(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['organization'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/organizations/current')
      if (error || !data) throw new Error('organization')
      return data
    },
  })
}

/** The organization's companies (ONB-03), with what each still needs before payroll. */
export function useCompanies(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['companies'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/companies')
      if (error || !data) throw new Error('companies')
      return data
    },
  })
}

/** Countries Pesaroll runs payroll in, with the numbers and regions a company there records. */
export function useCountries() {
  const { api } = useServices()
  return useQuery({
    queryKey: ['countries'],
    staleTime: Infinity,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/countries')
      if (error || !data) throw new Error('countries')
      return data
    },
  })
}

export function useIndustries() {
  const { api } = useServices()
  return useQuery({
    queryKey: ['industries'],
    staleTime: Infinity,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/industries')
      if (error || !data) throw new Error('industries')
      return data
    },
  })
}

/** The organization's locations (ONB-04), each with the companies it serves and is main for. */
export function useLocations(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['locations'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/locations')
      if (error || !data) throw new Error('locations')
      return data
    },
  })
}

/** The four levels of the org structure (ONB-05). */
export type LevelKey = 'divisions' | 'departments' | 'positions' | 'units'

/** An item of any level: positions have a title, the others a name. */
export interface StructureItem {
  id: string
  code: string
  name?: string
  title?: string
  divisionId?: string | null
  parentId?: string | null
  departmentId?: string | null
  allCompanies: boolean
  companyIds: string[]
}

export function useStructure(level: LevelKey, enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['structure', level],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET(`/api/structure/${level}` as '/api/structure/departments')
      if (error || !data) throw new Error(level)
      return data as unknown as StructureItem[]
    },
  })
}

/** The organization's payroll schedules (ONB-06), each with the companies it serves and is default for. */
export function useSchedules(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['schedules'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/pay-schedules')
      if (error || !data) throw new Error('schedules')
      return data
    },
  })
}

/** The organization's earnings, deductions and employer costs (ONB-07), Basic pay and the law's items first. */
export function usePayItems(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['payItems'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/pay-items')
      if (error || !data) throw new Error('payItems')
      return data
    },
  })
}

/** The organization's leave types (ONB-08), the law's first. */
export function useLeaveTypes(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['leaveTypes'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/leave-types')
      if (error || !data) throw new Error('leaveTypes')
      return data
    },
  })
}

/** The organization's leave plans (ONB-08), with their rules and what they're the default of. */
export function useLeavePlans(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['leavePlans'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/leave-plans')
      if (error || !data) throw new Error('leavePlans')
      return data
    },
  })
}

/** How many people currently work for the organization (ONB-09), for setup progress. */
export function useEmployeeCount(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['employees', 'count'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/employees', { params: { query: { pageSize: 1 } } })
      if (error || !data) throw new Error('employees')
      return Number(data.total)
    },
  })
}

/**
 * Which setup steps are done so far (ONB-12 will track all ten on the server). Locations is done
 * once every company has its main location, which it gets when it is added; Org structure once
 * there are departments or positions; Payroll schedules and Leave plans once every company has a
 * default schedule and plan; Earnings and deductions once Basic pay and the law's items exist.
 */
export function useSetupDone(hasOrganization: boolean): SetupStep[] {
  const companies = useCompanies(hasOrganization)
  const some = hasOrganization && !!companies.data?.length
  const locations = useLocations(some)
  const departments = useStructure('departments', some)
  const positions = useStructure('positions', some)
  const schedules = useSchedules(some)
  const payItems = usePayItems(some)
  const leavePlans = useLeavePlans(some)
  const employees = useEmployeeCount(some)
  if (!hasOrganization) return []
  if (!companies.data?.length) return ['organization']
  const mains = new Set((locations.data ?? []).flatMap(l => l.mainFor))
  if (!companies.data.every(c => mains.has(c.id))) return ['organization', 'companies']
  // Org structure is done once it has departments or positions (suggested or the business's own).
  if (!departments.data?.length && !positions.data?.length) return ['organization', 'companies', 'locations']
  // Payroll schedules once every company has its default (the suggested Monthly, made with the company).
  const defaults = new Set((schedules.data ?? []).flatMap(s => s.defaultFor))
  if (!companies.data.every(c => defaults.has(c.id))) return ['organization', 'companies', 'locations', 'structure']
  // Leave plans once every company has a default plan (Standard, made with the company).
  const leaveDefaults = new Set((leavePlans.data ?? []).flatMap(p => p.defaultFor))
  // Earnings and deductions once Basic pay and the law's items are in (made with the first company),
  // although it comes after Employees.
  const withItems = (done: SetupStep[]): SetupStep[] => (payItems.data?.some(i => i.lockedKey === 'basic') ? [...done, 'payItems'] : done)
  if (!companies.data.every(c => leaveDefaults.has(c.id))) return withItems(['organization', 'companies', 'locations', 'structure', 'schedules'])
  // Employees once there is at least one.
  return withItems(employees.data
    ? ['organization', 'companies', 'locations', 'structure', 'schedules', 'leavePlans', 'employees']
    : ['organization', 'companies', 'locations', 'structure', 'schedules', 'leavePlans'])
}

/** Where "Finish setting up" leads: the first step with a page that isn't done, else the last one built. */
export function nextSetupPath(done: SetupStep[]) {
  const built = setupSteps.filter(s => setupPaths[s.key])
  return setupPaths[(built.find(s => !done.includes(s.key)) ?? built[built.length - 1]).key]!
}

/** The banks of a country, with their branches and how long their account numbers are. */
export function useBanks(country: string) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['banks', country],
    staleTime: Infinity,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/banks', { params: { query: { country } } })
      if (error || !data) throw new Error('banks')
      return data
    },
  })
}

/** The organization's payees (ONB-07): who deductions and employer costs are paid to. */
export function usePayees(enabled = true) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['payees'],
    enabled,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/payees')
      if (error || !data) throw new Error('payees')
      return data
    },
  })
}

/** SACCOs and their branches in a country (reference data the back office maintains). */
export function useSaccos(country: string) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['saccos', country],
    staleTime: Infinity,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/saccos', { params: { query: { country } } })
      if (error || !data) throw new Error('saccos')
      return data
    },
  })
}
