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

const text = (en: string) => ({ en, sw: en })

/** Kenya as the API describes it (the seeded country data, trimmed). */
export const kenya = {
  code: 'KE', name: text('Kenya'), currency: 'KES', regionLabel: { en: 'County', sw: 'Kaunti' },
  fields: [
    { key: 'kra_pin', label: text('KRA PIN'), hint: text("The company's PIN from KRA iTax. Used for PAYE, Housing Levy and the P10 return."), example: 'P051234567X', pattern: '^[AP][0-9]{9}[A-Z]$', requiredForPayroll: true },
    { key: 'nssf_employer_no', label: text('NSSF employer number'), hint: text('From your NSSF registration. Used for the monthly NSSF return.'), example: null, pattern: '^[A-Z0-9/-]{4,20}$', requiredForPayroll: true },
    { key: 'sha_employer_no', label: text('SHA employer number'), hint: null, example: null, pattern: '^[A-Z0-9/-]{4,20}$', requiredForPayroll: true },
    { key: 'business_registration_no', label: text('Business registration number'), hint: null, example: null, pattern: null, requiredForPayroll: false },
  ],
  regions: [{ code: '001', name: 'Mombasa' }, { code: '022', name: 'Kiambu' }, { code: '047', name: 'Nairobi' }],
  wageAreas: [
    { code: 'cities', name: text('Cities'), description: text('Nairobi, Mombasa, Kisumu, Nakuru and Eldoret cities.'), towns: ['Nairobi', 'Mombasa', 'Kisumu', 'Nakuru', 'Eldoret'] },
    { code: 'municipalities', name: text('Former municipalities and town councils'), description: text('Former municipalities and the town councils of Mavoko, Ruiru and Limuru.'), towns: ['Mavoko', 'Ruiru', 'Limuru'] },
    { code: 'other', name: text('All other areas'), description: text('Anywhere that is neither a city, a former municipality nor one of those town councils.'), towns: [] },
  ],
}

export const industries = [
  { code: 'restaurants_bars', name: text('Restaurants, bars and clubs') },
  { code: 'hotels', name: text('Hotels and hospitality') },
]

const missing = (key: string, label: string) => ({ kind: 'identifier', key, label: text(label) })

/** A company as the API returns it; by default only its KRA PIN is in, so two numbers are missing. */
export const company = (overrides: Record<string, unknown> = {}) => ({
  id: '0199c0de-0000-7000-8000-00000000c001', legalName: 'Mama Oliech Restaurant Ltd', tradingName: 'Mama Oliech',
  countryCode: 'KE', currency: 'KES', industryCode: 'restaurants_bars', addressLine: 'Marcus Garvey Road', town: 'Nairobi',
  regionCode: '047', postalAddress: null, postalCode: null, phone: null, email: null,
  identifiers: { kra_pin: 'P051234567X' },
  readiness: { ready: false, missing: [missing('nssf_employer_no', 'NSSF employer number'), missing('sha_employer_no', 'SHA employer number')] },
  ...overrides,
})

export const banks = [
  { code: '68', name: 'Equity Bank', branches: [{ code: '047', name: 'Moi Avenue, Nairobi' }, { code: '025', name: 'Mombasa' }] },
  { code: '01', name: 'KCB Bank', branches: [{ code: '100', name: 'Moi Avenue, Nairobi' }] },
]

/** A paying account as the API returns it: the number only ever masked. */
export const equityAccount = (overrides: Record<string, unknown> = {}) => ({
  id: '0199c0de-0000-7000-8000-00000000e001', method: 'bank', bankCode: '68', bankName: 'Equity Bank', branchCode: '047',
  branchName: 'Moi Avenue, Nairobi', accountName: 'Mama Oliech Restaurant Ltd', accountNumberMasked: '•••• 8901', mpesaShortcode: null,
  isDefault: true, createdAt: '2026-10-08T10:00:00Z', ...overrides,
})

/** A location as the API returns it; by default the company's Head office and its main location. */
export const location = (overrides: Record<string, unknown> = {}) => ({
  id: '0199c0de-0000-7000-8000-00000000d001', code: 'HQ-01', name: 'Head office', kind: 'head_office', countryCode: 'KE', regionCode: '047', town: 'Nairobi',
  addressLine: 'Marcus Garvey Road', wageAreaCode: 'cities', allCompanies: false,
  companyIds: [company().id], mainFor: [company().id], ...overrides,
})

/** A payroll schedule as the API returns it; by default the suggested Monthly, the company's default. */
export const schedule = (overrides: Record<string, unknown> = {}) => ({
  id: '0199c0de-0000-7000-8000-00000000f001', name: 'Monthly', frequency: 'monthly', payDayRule: 'last_working_day', payDay: null, cutoffDay: 25,
  allCompanies: true, companyIds: [], defaultFor: [company().id], nextPayDates: ['2026-10-30', '2026-11-30', '2026-12-31'], ...overrides,
})

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
    'GET /api/countries': () => json(200, [kenya]),
    'GET /api/industries': () => json(200, industries),
    'GET /api/companies': () => json(200, []),
    'GET /api/banks': () => json(200, banks),
    'GET /api/locations': () => json(200, []),
    'GET /api/structure/divisions': () => json(200, []),
    'GET /api/structure/departments': () => json(200, []),
    'GET /api/structure/positions': () => json(200, []),
    'GET /api/structure/units': () => json(200, []),
    'GET /api/pay-schedules': () => json(200, []),
    [`GET /api/companies/${company().id}/payment-accounts`]: () => json(200, []),
    'PATCH /api/organizations/current': async req => json(200, { ...organization, name: ((await req.json()) as { name: string }).name }),
  }
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const req = input instanceof Request ? input : new Request(input, init)
    const path = new URL(req.url).pathname
    // JSON bodies are logged parsed; a file upload (multipart) is logged as its form data.
    const isForm = req.headers.get('Content-Type')?.startsWith('multipart/form-data')
    const text = isForm ? '' : await req.clone().text()
    const body = isForm ? await req.clone().formData() : text ? JSON.parse(text) : null
    calls.push({ method: req.method, path, body, auth: req.headers.get('Authorization') })
    const handler = routes[`${req.method} ${path}`] ?? defaults[`${req.method} ${path}`]
    return handler ? handler(req) : json(404, {})
  }))
  return calls
}
