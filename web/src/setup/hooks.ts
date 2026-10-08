import { useQuery } from '@tanstack/react-query'
import { useServices } from '../app/services'

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
