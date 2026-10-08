import { QueryClient } from '@tanstack/react-query'
import createClient from 'openapi-fetch'
import { createContext, useContext, useSyncExternalStore } from 'react'
import type { components, paths } from '../api/schema'
import { createAuthClient, type AuthClient } from '../auth/authClient'

export type Schemas = components['schemas']
export type ApiError = Schemas['ErrorResponse']

/**
 * Everything a screen needs to talk to the API. Created once in main.tsx, fresh per test.
 * Tests pass their own channel name: tabs share sign-in over a BroadcastChannel, and test files
 * running side by side in one process would otherwise hear each other.
 */
export function createServices({ channelName }: { channelName?: string } = {}) {
  const auth = createAuthClient({ channelName })
  const api = createClient<paths>({
    baseUrl: globalThis.location?.origin ?? 'http://localhost',
    fetch: request => auth.fetch(request),
  })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 60_000 } } })
  return { auth, api, queryClient }
}

export type Services = ReturnType<typeof createServices>

export const ServicesContext = createContext<Services | null>(null)

export function useServices() {
  const services = useContext(ServicesContext)
  if (!services) throw new Error('useServices needs the App around it')
  return services
}

export function useAuthStatus(auth: AuthClient) {
  return useSyncExternalStore(auth.subscribe, auth.status)
}
