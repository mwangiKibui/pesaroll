import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAuthClient } from './authClient'

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const token = (t: string) => json(200, { accessToken: t, tokenType: 'Bearer', expiresIn: 900 })
const ended = () => json(401, { code: 'AUTH-1010', message: 'Your session has ended. Sign in again.', reference: 'ABCD2345' })

function stubFetch(handler: (req: Request) => Response | Promise<Response>) {
  const calls: Request[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo, init?: RequestInit) => {
    const req = input instanceof Request ? input : new Request(new URL(input as string, 'http://localhost'), init)
    calls.push(req.clone())
    return handler(req)
  }))
  return calls
}

afterEach(() => vi.unstubAllGlobals())

describe('auth client', () => {
  it('sends the access token on every call', async () => {
    const calls = stubFetch(() => json(200, {}))
    const auth = createAuthClient()
    auth.setAccessToken('t1')

    await auth.fetch(new Request('http://localhost/api/auth/me'))

    expect(calls[0].headers.get('Authorization')).toBe('Bearer t1')
  })

  it('on start, a refresh through the cookie decides whether the person is signed in', async () => {
    stubFetch(req => (req.url.endsWith('/api/auth/refresh') ? token('fresh') : json(404, {})))
    const auth = createAuthClient()
    expect(auth.status()).toBe('unknown')

    await auth.start()

    expect(auth.status()).toBe('signedIn')
    expect(auth.accessToken()).toBe('fresh')
  })

  it('with no live cookie, start leaves the person signed out', async () => {
    stubFetch(() => json(401, { code: 'AUTH-1007', message: 'Sign in to continue.', reference: 'ABCD2345' }))
    const auth = createAuthClient()

    await auth.start()

    expect(auth.status()).toBe('signedOut')
  })

  it('when the token has ended, refreshes once for all waiting calls and retries them', async () => {
    let refreshes = 0
    const calls = stubFetch(async req => {
      if (req.url.endsWith('/api/auth/refresh')) { refreshes++; await new Promise(r => setTimeout(r, 10)); return token('t2') }
      return req.headers.get('Authorization') === 'Bearer t2' ? json(200, { ok: true }) : ended()
    })
    const auth = createAuthClient()
    auth.setAccessToken('t1')

    const results = await Promise.all([1, 2, 3].map(() => auth.fetch(new Request('http://localhost/api/auth/me'))))

    expect(results.map(r => r.status)).toEqual([200, 200, 200])
    expect(refreshes).toBe(1)
    expect(calls.filter(c => c.url.endsWith('/api/auth/me') && c.headers.get('Authorization') === 'Bearer t2')).toHaveLength(3)
  })

  it('a retry keeps the request body', async () => {
    const bodies: string[] = []
    stubFetch(async req => {
      if (req.url.endsWith('/api/auth/refresh')) return token('t2')
      bodies.push(await req.text())
      return req.headers.get('Authorization') === 'Bearer t2' ? json(200, {}) : ended()
    })
    const auth = createAuthClient()
    auth.setAccessToken('t1')

    await auth.fetch(new Request('http://localhost/api/things', { method: 'POST', body: '{"a":1}' }))

    expect(bodies).toEqual(['{"a":1}', '{"a":1}'])
  })

  it('when the refresh fails too, the person is signed out and told', async () => {
    stubFetch(req => (req.url.endsWith('/api/auth/refresh') ? ended() : ended()))
    const auth = createAuthClient()
    auth.setAccessToken('t1')
    const listener = vi.fn()
    auth.subscribe(listener)

    const res = await auth.fetch(new Request('http://localhost/api/auth/me'))

    expect(res.status).toBe(401)
    expect(auth.status()).toBe('signedOut')
    expect(listener).toHaveBeenCalled()
  })

  it('other errors are passed through without a refresh', async () => {
    let refreshes = 0
    stubFetch(req => {
      if (req.url.endsWith('/api/auth/refresh')) { refreshes++; return token('x') }
      return json(401, { code: 'AUTH-1001', message: 'Wrong', reference: 'ABCD2345' })
    })
    const auth = createAuthClient()

    const res = await auth.fetch(new Request('http://localhost/api/auth/signin', { method: 'POST' }))

    expect(res.status).toBe(401)
    expect(refreshes).toBe(0)
  })

  it('signing out ends the session on the server and forgets the token', async () => {
    const calls = stubFetch(() => new Response(null, { status: 204 }))
    const auth = createAuthClient()
    auth.setAccessToken('t1')

    await auth.signOut()

    expect(calls.some(c => c.url.endsWith('/api/auth/signout') && c.method === 'POST')).toBe(true)
    expect(auth.status()).toBe('signedOut')
    expect(auth.accessToken()).toBeNull()
  })

  it('a token or sign-out from another tab is shared through the channel', async () => {
    stubFetch(() => new Response(null, { status: 204 }))
    const a = createAuthClient({ channelName: 'test-tabs' })
    const b = createAuthClient({ channelName: 'test-tabs' })

    a.setAccessToken('shared', { broadcast: true })
    await vi.waitFor(() => expect(b.accessToken()).toBe('shared'))

    await a.signOut()
    await vi.waitFor(() => expect(b.status()).toBe('signedOut'))
    a.dispose(); b.dispose()
  })
})
