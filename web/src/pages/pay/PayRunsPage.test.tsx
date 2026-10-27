import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { apiError, company, fakeApi, json, me, orgToken, payRunSummary, schedule } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

beforeEach(() => {
  vi.stubEnv('VITE_FEATURES', 'payRuns')
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date('2026-10-27T09:00:00+03:00'))
})
afterEach(() => { vi.useRealTimers(); vi.unstubAllEnvs(); vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const mombasa = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Mombasa Ltd' })
const grant = (permission: string) => ({ permission, allCompanies: true, companyIds: [] as string[] })
const viewer = ['pay_runs.view'].map(grant)

const monthly = schedule({ defaultFor: [westlands.id, mombasa.id] })
const casuals = schedule({
  id: '0199c0de-0000-7000-8000-00000000f002', name: 'Casuals', frequency: 'weekly', payDayRule: null, cutoffDay: null, payWeekday: 5, cutoffDaysBefore: 2,
  allCompanies: false, companyIds: [westlands.id], defaultFor: [], nextPayDates: ['2026-10-30', '2026-11-06', '2026-11-13'],
})
const october = payRunSummary({ companyId: westlands.id })
const week = payRunSummary({
  id: '0199c0de-0000-7000-8000-0000000a0002', companyId: westlands.id, payScheduleId: casuals.id, scheduleName: 'Casuals', periodStart: '2026-10-10',
  periodEnd: '2026-10-16', payDate: '2026-10-16', status: 'approved', people: 6, grossPay: 46800, netPay: 41200,
})
const correction = payRunSummary({ id: '0199c0de-0000-7000-8000-0000000a0003', companyId: westlands.id, kind: 'correction', status: 'draft', people: 1, grossPay: 0, netPay: 0 })
const page = (items: unknown[]) => json(200, { items, total: items.length, page: 1, pageSize: 10 })

function signedIn(permissions?: ReturnType<typeof grant>[], routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    'GET /api/pay-schedules': req => json(200, new URL(req.url).searchParams.get('companyId') === mombasa.id ? [monthly] : [monthly, casuals]),
    'GET /api/pay-runs': req => page(new URL(req.url).searchParams.get('companyId') === mombasa.id ? [] : [october, week, correction]),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions }) } : {}),
    ...routes,
  })
}

const table = () => screen.getByRole('region', { name: 'Pay runs' })
const drawer = () => screen.getByRole('dialog', { name: 'Start pay run' })
const listCalls = (calls: ReturnType<typeof fakeApi>) =>
  calls.filter(c => c.method === 'GET' && c.path === '/api/pay-runs').map(c => new URL(c.url).searchParams)
const started = (calls: ReturnType<typeof fakeApi>) => calls.filter(c => c.method === 'POST' && c.path === '/api/pay-runs').map(c => c.body)
const rowOf = async (text: string) => (await within(await screen.findByRole('region', { name: 'Pay runs' })).findAllByText(text))[0].closest('tr')!
const switchCompany = async (name: string) => {
  await userEvent.click(within(screen.getByRole('banner')).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getByRole('option', { name }))
}
const openStart = async () => {
  await rowOf('October 2026')
  await userEvent.click(screen.getByRole('button', { name: 'Start pay run' }))
  return drawer()
}

it('lists the top bar company\'s pay runs: period and schedule, pay date, people, gross and net pay, status', async () => {
  const calls = signedIn()
  const { container } = renderApp('/pay/runs')

  expect(await screen.findByRole('heading', { name: 'Pay runs', level: 1 })).toBeInTheDocument()
  expect(await within(screen.getByRole('navigation', { name: 'Main' })).findByRole('link', { name: 'Pay runs' })).toHaveAttribute('aria-current', 'page')
  const row = await rowOf('October 2026')
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['Period', 'Pay date', 'People', 'Gross pay (KES)', 'Net pay (KES)', 'Status'])
  expect(within(row).getAllByRole('cell').map(c => c.textContent)).toEqual(['October 2026Monthly', '30 Oct 2026', '14', '798,000.00', '589,200.00', 'Awaiting approval'])
  expect(within(await rowOf('10 Oct to 16 Oct 2026')).getAllByRole('cell').map(c => c.textContent))
    .toEqual(['10 Oct to 16 Oct 2026Casuals', '16 Oct 2026', '6', '46,800.00', '41,200.00', 'Approved'])
  // A correction says so; a draft has no totals yet.
  const fix = within(table()).getAllByRole('row').at(-1)!
  expect(within(fix).getAllByRole('cell').map(c => c.textContent)).toEqual(['October 2026Monthly · Correction', '30 Oct 2026', '1', '—', '—', 'Draft'])
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('companyId')).toBe(westlands.id))
  await expectNoA11yViolations(container)
})

it('switching the company in the top bar changes the list', async () => {
  const calls = signedIn()
  renderApp('/pay/runs')
  await rowOf('October 2026')
  await switchCompany('Mombasa Ltd')
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('companyId')).toBe(mombasa.id))
  expect(await screen.findByText('No pay runs yet')).toBeInTheDocument()
})

it('starts a monthly pay run for the top bar\'s company: its default schedule and this month to start with', async () => {
  const calls = signedIn(undefined, { 'POST /api/pay-runs': () => json(201, { ...october, status: 'draft' }) })
  renderApp('/pay/runs')
  const sheet = await openStart()
  expect(within(sheet).getByRole('combobox', { name: 'Company' })).toHaveValue('Westlands Ltd')
  expect(within(sheet).getByRole('combobox', { name: 'Payroll schedule' })).toHaveValue('Monthly')
  expect(within(sheet).getByRole('combobox', { name: 'Month' })).toHaveValue('October 2026')
  await userEvent.click(within(sheet).getByRole('combobox', { name: 'Month' }))
  expect(within(sheet).getAllByRole('option').map(o => o.textContent)).toEqual(['September 2026', 'October 2026', 'November 2026'])
  await userEvent.click(within(sheet).getByRole('option', { name: 'November 2026' }))
  const before = listCalls(calls).length
  await userEvent.click(within(sheet).getByRole('button', { name: 'Start pay run' }))

  expect(await screen.findByText('Pay run started')).toBeInTheDocument()
  expect(started(calls)).toEqual([{ companyId: westlands.id, payScheduleId: monthly.id, month: '2026-11', payDate: null }])
  await vi.waitFor(() => expect(listCalls(calls).length).toBeGreaterThan(before))
})

it('a weekly schedule is run by pay date, from its next pay dates', async () => {
  const calls = signedIn(undefined, { 'POST /api/pay-runs': () => json(201, { ...week, status: 'draft' }) })
  renderApp('/pay/runs')
  const sheet = await openStart()
  await userEvent.clear(within(sheet).getByRole('combobox', { name: 'Payroll schedule' }))
  await userEvent.type(within(sheet).getByRole('combobox', { name: 'Payroll schedule' }), 'Cas')
  await userEvent.click(within(sheet).getByRole('option', { name: 'Casuals' }))
  expect(within(sheet).queryByRole('combobox', { name: 'Month' })).not.toBeInTheDocument()
  expect(within(sheet).getByRole('combobox', { name: 'Pay date' })).toHaveValue('Fri 30 Oct 2026')
  await userEvent.click(within(sheet).getByRole('button', { name: 'Start pay run' }))

  expect(await screen.findByText('Pay run started')).toBeInTheDocument()
  expect(started(calls)).toEqual([{ companyId: westlands.id, payScheduleId: casuals.id, month: null, payDate: '2026-10-30' }])
})

it('a run that already exists is refused with its code; problems with the details show on their fields', async () => {
  let answer = apiError(409, 'PAY-2012', 'A pay run for Monthly in October 2026 already exists at Westlands Ltd.')
  signedIn(undefined, { 'POST /api/pay-runs': () => answer })
  renderApp('/pay/runs')
  const sheet = await openStart()
  await userEvent.click(within(sheet).getByRole('button', { name: 'Start pay run' }))
  const problem = await within(sheet).findByRole('alert')
  expect(problem).toHaveTextContent('A pay run for Monthly in October 2026 already exists at Westlands Ltd.')
  expect(problem).toHaveTextContent('Code PAY-2012')

  answer = json(400, { code: 'PAY-2020', message: 'Some pay run details don\'t look right. Check the fields marked.', reference: 'R1', fields: { payScheduleId: 'Choose a payroll schedule the company uses.' } })
  await userEvent.click(within(sheet).getByRole('button', { name: 'Start pay run' }))
  expect(await within(sheet).findByText('Choose a payroll schedule the company uses.')).toBeInTheDocument()
})

it('only someone who manages pay runs for the company can start one', async () => {
  signedIn(viewer)
  renderApp('/pay/runs')
  await rowOf('October 2026')
  expect(screen.queryByRole('button', { name: 'Start pay run' })).not.toBeInTheDocument()
})

it('without View on pay runs for the company, says so and asks for nothing', async () => {
  const calls = signedIn(['employees.view'].map(grant))
  renderApp('/pay/runs')
  expect(await screen.findByText('You can\'t see this company\'s pay runs')).toBeInTheDocument()
  expect(listCalls(calls)).toHaveLength(0)
})

it('stays hidden until its flag is on', async () => {
  vi.stubEnv('VITE_FEATURES', '')
  const calls = signedIn()
  renderApp('/pay/runs')
  expect(await screen.findByText("This page isn't ready yet.")).toBeInTheDocument()
  expect(listCalls(calls)).toHaveLength(0)
})
