export type AuthStatus = 'unknown' | 'signedIn' | 'signedOut'

type TabMessage = { type: 'token'; token: string } | { type: 'signedOut' }

const REFRESH = '/api/auth/refresh'

/** Absolute URL on this site (Request needs one outside a page, e.g. in tests). */
const url = (path: string) => new URL(path, globalThis.location?.href ?? 'http://localhost').toString()

/**
 * Holds the 15-minute access token in memory (never in browser storage) and refreshes it
 * through the HttpOnly cookie:
 * - on start, a refresh decides whether the person is still signed in
 * - when a call fails with AUTH-1010, one refresh runs for every waiting call, then they retry
 * - tabs share new tokens and sign-out over a BroadcastChannel, and take turns to refresh
 *   (Web Locks), because the API ends the session if an old refresh cookie is used twice
 */
export function createAuthClient({ channelName = 'pesaroll-auth' }: { channelName?: string } = {}) {
  let token: string | null = null
  let status: AuthStatus = 'unknown'
  let refreshing: Promise<string | null> | null = null
  let started: Promise<void> | null = null
  const listeners = new Set<() => void>()
  const channel = typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(channelName)

  function set(next: string | null, broadcast: boolean) {
    token = next
    status = next ? 'signedIn' : 'signedOut'
    if (broadcast) channel?.postMessage(next ? { type: 'token', token: next } : { type: 'signedOut' } satisfies TabMessage)
    listeners.forEach(notify => notify())
  }

  channel?.addEventListener('message', (e: MessageEvent<TabMessage>) => {
    if (e.data?.type === 'token') set(e.data.token, false)
    else if (e.data?.type === 'signedOut') set(null, false)
  })

  function send(request: Request) {
    if (token) request.headers.set('Authorization', `Bearer ${token}`)
    return globalThis.fetch(request)
  }

  async function withTabLock<T>(work: () => Promise<T>): Promise<T> {
    const locks = typeof navigator === 'undefined' ? undefined : navigator.locks
    return locks ? locks.request('pesaroll-refresh', work) : work()
  }

  function refresh(): Promise<string | null> {
    refreshing ??= withTabLock(async () => {
      const before = token
      try {
        const res = await globalThis.fetch(new Request(url(REFRESH), { method: 'POST', credentials: 'same-origin' }))
        if (!res.ok) {
          // Another tab may have refreshed while we waited for the lock.
          if (token && token !== before) return token
          set(null, false)
          return null
        }
        const body = (await res.json()) as { accessToken: string }
        set(body.accessToken, true)
        return body.accessToken
      } catch {
        set(null, false)
        return null
      }
    }).finally(() => { refreshing = null })
    return refreshing
  }

  return {
    status: () => status,
    accessToken: () => token,

    /** The organization the token carries (null before the organization step). Read, not verified: the API verifies. */
    organizationId(): string | null {
      try {
        const payload = token?.split('.')[1]
        if (!payload) return null
        const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as { org?: string }
        return json.org ?? null
      } catch {
        return null
      }
    },

    subscribe(listener: () => void) {
      listeners.add(listener)
      return () => { listeners.delete(listener) }
    },

    /** After sign-in or sign-up. */
    setAccessToken(next: string | null, { broadcast = false }: { broadcast?: boolean } = {}) {
      set(next, broadcast)
    },

    /** Once per page load: is there a live session behind the cookie? */
    start() {
      started ??= refresh().then(() => undefined)
      return started
    },

    /** fetch with the token; on AUTH-1010 refreshes once and retries. Used by the API client. */
    async fetch(request: Request): Promise<Response> {
      const retry = request.clone()
      const res = await send(request)
      if (res.status !== 401 || new URL(request.url, 'http://x').pathname === REFRESH) return res
      const code = await res.clone().json().then((b: { code?: string }) => b?.code, () => null)
      if (code !== 'AUTH-1010') return res
      return (await refresh()) ? send(retry) : res
    },

    async signOut() {
      try {
        await send(new Request(url('/api/auth/signout'), { method: 'POST', credentials: 'same-origin' }))
      } catch {
        // Offline: forget the token here anyway; the session ends on its own.
      }
      set(null, true)
    },

    dispose() {
      channel?.close()
      listeners.clear()
    },
  }
}

export type AuthClient = ReturnType<typeof createAuthClient>
