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
  costCentre?: string | null
  departmentId?: string | null
  grade?: string | null
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

/**
 * Which setup steps are done so far (ONB-12 will track all ten on the server). Locations is done
 * once every company has its main location, which it gets when it is added; Org structure once
 * there are departments or positions.
 */
export function useSetupDone(hasOrganization: boolean): SetupStep[] {
  const companies = useCompanies(hasOrganization)
  const locations = useLocations(hasOrganization && !!companies.data?.length)
  const departments = useStructure('departments', hasOrganization && !!companies.data?.length)
  const positions = useStructure('positions', hasOrganization && !!companies.data?.length)
  if (!hasOrganization) return []
  if (!companies.data?.length) return ['organization']
  const mains = new Set((locations.data ?? []).flatMap(l => l.mainFor))
  if (!companies.data.every(c => mains.has(c.id))) return ['organization', 'companies']
  // Org structure is done once it has departments or positions (suggested or the business's own).
  return departments.data?.length || positions.data?.length
    ? ['organization', 'companies', 'locations', 'structure']
    : ['organization', 'companies', 'locations']
}

/** Where "Finish setting up" leads: the first step with a page that isn't done, else the last one built. */
export function nextSetupPath(done: SetupStep[]) {
  const built = setupSteps.filter(s => setupPaths[s.key])
  return setupPaths[(built.find(s => !done.includes(s.key)) ?? built[built.length - 1]).key]!
}
