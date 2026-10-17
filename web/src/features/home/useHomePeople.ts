import { useQuery } from '@tanstack/react-query'
import { useServices } from '../../app/services'

/** Home's people numbers for one company (OP-30): counts and what's missing, never pay. Under 'employees' so adding someone refreshes it. */
export function useHomePeople(companyId: string | null, enabled: boolean) {
  const { api } = useServices()
  return useQuery({
    queryKey: ['employees', 'home', companyId],
    enabled: enabled && !!companyId,
    queryFn: async () => {
      const { data, error } = await api.GET('/api/home/people', { params: { query: { companyId: companyId! } } })
      if (error || !data) throw new Error('home')
      return data
    },
  })
}
