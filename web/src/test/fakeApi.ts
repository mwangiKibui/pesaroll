import { vi } from 'vitest'

export const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

export const apiError = (status: number, code: string, message: string) => json(status, { code, message, reference: '7F3K92QD' })

type Handler = (req: Request) => Response | Promise<Response>

export const roles = [
  { code: 'leadership', name: { en: 'Leadership', sw: 'Uongozi' }, roles: [
    { id: '0199c0de-5a5e-7000-8000-000000000102', code: 'ceo', name: { en: 'CEO or Managing Director', sw: 'Mkurugenzi Mtendaji' }, description: { en: 'Runs the business day to day.', sw: 'Anaendesha biashara kila siku.' }, isOther: false },
  ] },
  { code: 'other', name: { en: 'Something else', sw: 'Kitu kingine' }, roles: [
    { id: '0199c0de-5a5e-7000-8000-000000000502', code: 'other', name: { en: 'Other', sw: 'Nyingine' }, description: { en: 'Tell us your role.', sw: 'Tuambie jukumu lako.' }, isOther: true },
  ] },
]

export const me = { id: 'u1', firstName: 'Wanjiru', middleName: null, lastName: 'Kamau', fullName: 'Wanjiru Kamau', email: 'wanjiru@example.com', phone: null, signupRoleId: roles[0].roles[0].id, signupRoleOther: null, organizationId: null, signedUpVia: 'self', canOnboardOrganization: true }

export const token = (t = 'access-1') => json(200, { accessToken: t, tokenType: 'Bearer', expiresIn: 900 })

export const organization = { id: '0199c0de-0000-7000-8000-00000000a001', name: 'Mama Oliech Group' }

const b64 = (value: object) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')

/** An access token shaped like the API's (a JWT; the screen only reads it, never verifies it). */
export const jwt = (org: string | null = organization.id) =>
  `${b64({ alg: 'HS256', typ: 'JWT' })}.${b64({ sub: me.id, sid: 'session-1', ...(org ? { org } : {}) })}.signature`

/** A token for someone who has onboarded their organization. */
export const orgToken = () => token(jwt())

/**
 * A fake API on the global fetch: routes are "METHOD /path". By default the person has no
 * session (refresh answers AUTH-1007) and the role list and /me answer as above.
 */
export function fakeApi(routes: Record<string, Handler> = {}) {
  const calls: { method: string; path: string; body: unknown; auth: string | null }[] = []
  const defaults: Record<string, Handler> = {
    'POST /api/auth/refresh': () => apiError(401, 'AUTH-1007', 'Sign in to continue.'),
    'GET /api/auth/signup-roles': () => json(200, roles),
    'GET /api/auth/me': () => json(200, me),
    'POST /api/auth/signout': () => new Response(null, { status: 204 }),
    'POST /api/organizations': () => json(201, { organization, token: { accessToken: jwt(), tokenType: 'Bearer', expiresIn: 900 } }),
    'GET /api/organizations/current': () => json(200, organization),
  }
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const req = input instanceof Request ? input : new Request(input, init)
    const path = new URL(req.url).pathname
    const text = await req.clone().text()
    calls.push({ method: req.method, path, body: text ? JSON.parse(text) : null, auth: req.headers.get('Authorization') })
    const handler = routes[`${req.method} ${path}`] ?? defaults[`${req.method} ${path}`]
    return handler ? handler(req) : json(404, {})
  }))
  return calls
}
