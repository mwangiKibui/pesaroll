import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { company, fakeApi, json, me, orgToken, payRunDetail, payRunPerson } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

beforeEach(() => vi.stubEnv('VITE_FEATURES', 'payRuns'))
afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const run = payRunDetail({ companyId: westlands.id })
const path = `/pay/runs/${run.id}`
const api = `/api/pay-runs/${run.id}`
const peter = '0199c0de-0000-7000-8000-00000000a601'
const commission = '0199c0de-0000-7000-8000-00000000b602'
const loan = '0199c0de-0000-7000-8000-00000000b603'
const nssf = '0199c0de-0000-7000-8000-00000000b604'
const line = (overrides: Record<string, unknown>) => ({
  kind: 'earning', code: 'BASIC', name: 'Basic pay', statutoryKey: null, base: null, rate: null, amount: 0, monthToDate: null, earlier: 0, quantity: null, unitRate: null,
  reason: null, owedFrom: null, owedTo: null, ...overrides,
})
const person = payRunPerson({
  employeeId: peter, staffNo: 'W-02', name: 'Peter Kamau',
  lines: [
    line({ amount: 16000, quantity: 20, unitRate: 800 }),
    line({ code: 'OT', name: 'Overtime', amount: 1800, quantity: 6, unitRate: 300 }),
    line({ code: 'COMM', name: 'Commission', amount: 2000, reason: 'Sold the most' }),
    line({ code: 'BASIC', name: 'Basic pay', amount: 800, quantity: 1, unitRate: 800, owedFrom: '2026-09-26', owedTo: '2026-09-30' }),
    line({ kind: 'deduction', code: 'NSSF', name: 'NSSF', statutoryKey: 'nssf_employee', base: 16000, rate: 0.06, amount: 960 }),
  ],
})
const entry = (overrides: Record<string, unknown>) => ({
  payItemId: null, quantity: null, amount: null, reason: null, source: 'entered', enteredBy: 'u1', enteredAt: '2026-10-20T07:00:00Z', afterCutoff: false, ...overrides,
})
const inputs = (deductions: unknown = null) => ({
  payRunId: run.id,
  people: [{ employeeId: peter, staffNo: 'W-02', name: 'Peter Kamau', unit: 'day', usualQuantity: 22, leftOn: null, deductions,
    entries: [entry({ quantity: 20 }), entry({ payItemId: commission, amount: 2000, reason: 'Sold the most' })] }],
  items: [{ payItemId: 'ot', code: 'OT', name: 'Overtime', unit: 'hour' }],
  amountItems: [
    { payItemId: commission, code: 'COMM', name: 'Commission', kind: 'earning', calcMethod: 'each_run', statutoryKey: null },
    { payItemId: loan, code: 'LOAN', name: 'Staff loan', kind: 'deduction', calcMethod: 'fixed', statutoryKey: null },
    { payItemId: nssf, code: 'NSSF', name: 'NSSF', kind: 'deduction', calcMethod: 'by_law', statutoryKey: 'nssf_employee' },
  ],
})

function signedIn(routes: Parameters<typeof fakeApi>[0] = {}, permissions?: string[]) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands]),
    [`GET ${api}`]: () => json(200, run),
    [`GET ${api}/people`]: () => json(200, { items: [person], total: 1, page: 1, pageSize: 10 }),
    [`GET ${api}/inputs`]: () => json(200, inputs()),
    [`PUT ${api}/inputs`]: () => json(200, inputs()),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions: permissions.map(permission => ({ permission, allCompanies: true, companyIds: [] })) }) } : {}),
    ...routes,
  })
}
const sent = (calls: ReturnType<typeof fakeApi>, method: string, to: string) => calls.filter(c => c.method === method && c.path === to).map(c => c.body)
async function openPeter() {
  await userEvent.click(await screen.findByRole('button', { name: 'View Peter Kamau' }))
  return screen.findByRole('dialog', { name: 'Peter Kamau' })
}
const earnings = (sheet: HTMLElement) => within(sheet).getByRole('list', { name: 'Earnings' })

it('changes a line for this run, saying why; owed lines and hours stay with the inputs', async () => {
  const calls = signedIn()
  const { container } = renderApp(path)
  const sheet = await openPeter()
  expect(within(earnings(sheet)).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual(['Change Basic pay', 'Change Commission'])
  await userEvent.click(within(sheet).getByRole('button', { name: 'Change Basic pay' }))
  const form = screen.getByRole('dialog', { name: 'Change Basic pay for this run' })
  await userEvent.type(within(form).getByRole('textbox', { name: 'Amount for this run' }), '15000')
  await userEvent.click(within(form).getByRole('button', { name: 'Save change' }))
  expect(within(form).getByText('Say why this is changed for this pay run.')).toBeInTheDocument()
  await userEvent.type(within(form).getByRole('textbox', { name: 'Why' }), 'Two days unpaid leave')
  await userEvent.click(within(form).getByRole('button', { name: 'Save change' }))
  expect(await screen.findByText('Changed for this run. Work out pay to see it in the figures.')).toBeInTheDocument()
  // Basic pay's days stay as entered.
  expect(sent(calls, 'PUT', `${api}/inputs`)).toEqual([{ entries: [{ employeeId: peter, payItemId: null, quantity: 20, amount: 15000, reason: 'Two days unpaid leave' }] }])

  await userEvent.click(within(sheet).getByRole('button', { name: 'Change NSSF' }))
  const nssfForm = screen.getByRole('dialog', { name: 'Change NSSF for this run' })
  await userEvent.type(within(nssfForm).getByRole('textbox', { name: 'Amount for this run' }), '0')
  await userEvent.type(within(nssfForm).getByRole('textbox', { name: 'Why' }), 'Paid to NSSF directly this month')
  await userEvent.click(within(nssfForm).getByRole('button', { name: 'Save change' }))
  await vi.waitFor(() => expect(sent(calls, 'PUT', `${api}/inputs`)).toHaveLength(2))
  expect(sent(calls, 'PUT', `${api}/inputs`)[1]).toEqual({ entries: [{ employeeId: peter, payItemId: nssf, quantity: null, amount: 0, reason: 'Paid to NSSF directly this month' }] })
  await expectNoA11yViolations(container)
})

it('a changed line can go back to what it would be', async () => {
  const calls = signedIn()
  renderApp(path)
  const sheet = await openPeter()
  await userEvent.click(within(sheet).getByRole('button', { name: 'Change Commission' }))
  const form = screen.getByRole('dialog', { name: 'Change Commission for this run' })
  expect(within(form).getByRole('textbox', { name: 'Amount for this run' })).toHaveValue('2000')
  expect(within(form).getByRole('textbox', { name: 'Why' })).toHaveValue('Sold the most')
  await userEvent.click(within(form).getByRole('button', { name: 'Undo change' }))
  await vi.waitFor(() => expect(sent(calls, 'PUT', `${api}/inputs`)).toEqual([{ entries: [{ employeeId: peter, payItemId: commission, quantity: null, amount: null, reason: null }] }]))
})

it('adds an item for this run only', async () => {
  const calls = signedIn()
  renderApp(path)
  const sheet = await openPeter()
  await userEvent.click(within(sheet).getByRole('button', { name: 'Add for this run' }))
  const form = screen.getByRole('dialog', { name: 'Add for this run' })
  await userEvent.click(within(form).getByRole('button', { name: 'Add' }))
  expect(within(form).getByText('Choose what to add.')).toBeInTheDocument()
  await userEvent.click(within(form).getByRole('combobox', { name: 'Item' }))
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Staff loan'])
  await userEvent.click(screen.getByRole('option', { name: 'Staff loan' }))
  await userEvent.type(within(form).getByRole('textbox', { name: 'Amount for this run' }), '3000')
  await userEvent.type(within(form).getByRole('textbox', { name: 'Why' }), 'Advance in October')
  await userEvent.click(within(form).getByRole('button', { name: 'Add' }))
  await vi.waitFor(() => expect(sent(calls, 'PUT', `${api}/inputs`)).toEqual([{ entries: [{ employeeId: peter, payItemId: loan, quantity: null, amount: 3000, reason: 'Advance in October' }] }]))
})

it('chooses the statutory deductions for this person in this run, and goes back to the usual ones', async () => {
  let choice: unknown = null
  const calls = signedIn({
    [`GET ${api}/inputs`]: () => json(200, inputs(choice)),
    [`PUT ${api}/people/${peter}/deductions`]: async req => {
      const body = (await req.json()) as { deductions: string[] | null; reason: string | null }
      choice = body.deductions ? { deductions: body.deductions, reason: body.reason, setBy: 'u1', setAt: '2026-10-27T07:00:00Z' } : null
      return json(200, inputs(choice))
    },
  })
  renderApp(path)
  const sheet = await openPeter()
  const section = await within(sheet).findByRole('region', { name: 'Statutory deductions' })
  expect(section).toHaveTextContent('The usual ones for Peter Kamau.')
  await userEvent.click(within(section).getByRole('button', { name: 'Choose for this run' }))
  const form = screen.getByRole('dialog', { name: 'Statutory deductions for this run' })
  expect(within(form).getAllByRole('checkbox').map(c => [c.getAttribute('name'), (c as HTMLInputElement).checked]))
    .toEqual([['paye', true], ['shif', true], ['nssf', true], ['housing_levy', true], ['nita', true]])
  await userEvent.click(within(form).getByRole('checkbox', { name: 'SHIF' }))
  await userEvent.type(within(form).getByRole('textbox', { name: 'Why' }), 'Covered by a spouse this month')
  await userEvent.click(within(form).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(section).toHaveTextContent('For this run: PAYE, NSSF, Housing Levy, NITA. Covered by a spouse this month'))
  expect(sent(calls, 'PUT', `${api}/people/${peter}/deductions`)).toEqual([{ deductions: ['paye', 'nssf', 'housing_levy', 'nita'], reason: 'Covered by a spouse this month' }])

  await userEvent.click(within(section).getByRole('button', { name: 'Use the usual ones' }))
  await vi.waitFor(() => expect(section).toHaveTextContent('The usual ones for Peter Kamau.'))
  expect(sent(calls, 'PUT', `${api}/people/${peter}/deductions`)[1]).toEqual({ deductions: null, reason: null })
})

it('after a change the run is a draft again, still showing the last figures, until it is worked out', async () => {
  signedIn({ [`GET ${api}`]: () => json(200, { ...run, status: 'draft' }) })
  renderApp(path)
  const card = await screen.findByRole('region', { name: 'Changed since it was worked out' })
  expect(card).toHaveTextContent('The figures below were worked out on 26 Oct 2026, before the latest changes. Work it out again to bring them up to date.')
  expect(within(card).getByRole('button', { name: 'Work out pay' })).toBeInTheDocument()
  expect(await screen.findByRole('region', { name: 'People in this pay run' })).toBeInTheDocument()
})

it('someone who may only look, or a run that is sent, has nothing to change', async () => {
  signedIn({ [`GET ${api}`]: () => json(200, { ...run, canManage: false }) }, ['pay_runs.view'])
  renderApp(path)
  const sheet = await openPeter()
  expect(within(sheet).queryAllByRole('button', { name: /^Change |Add for this run|Choose for this run/ })).toHaveLength(0)
})
