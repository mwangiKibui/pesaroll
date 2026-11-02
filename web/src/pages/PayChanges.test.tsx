import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, employeeDetail, employeeRow, fakeApi, json, leavePlan, location, me, orgToken, payRunDetail, schedule } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

beforeEach(() => vi.stubEnv('VITE_FEATURES', 'payRuns'))
afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); localStorage.clear() })

const restaurant = company()
const wanjiru = employeeRow()
const changes = `/api/companies/${restaurant.id}/employees/${wanjiru.id}/pay-changes`
const grant = (permission: string) => ({ permission, allCompanies: true, companyIds: [] as string[] })
const row = (overrides: Record<string, unknown> = {}) => ({
  id: '0199c0de-0000-7000-8000-0000000c0001', validFrom: '2024-01-15', validTo: null, amount: 85000, basis: 'monthly', usualQuantity: null, reason: null,
  changedBy: null, createdAt: '2024-01-15T07:00:00Z', replacedBy: null, replacedAt: null, ...overrides,
})
const october = payRunDetail({ id: '0199c0de-0000-7000-8000-0000000a0010', companyId: restaurant.id, status: 'draft' })

function withProfile(routes: Parameters<typeof fakeApi>[0] = {}, permissions?: string[]) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [restaurant]),
    'GET /api/locations': () => json(200, [location()]),
    'GET /api/pay-schedules': () => json(200, [schedule()]),
    'GET /api/leave-plans': () => json(200, [leavePlan()]),
    'GET /api/employees': () => json(200, { items: [wanjiru], total: 1, page: 1, pageSize: 20 }),
    [`GET /api/employees/${wanjiru.id}`]: req => json(200, new URL(req.url).searchParams.get('includePay') === 'true' ? employeeDetail() : employeeDetail({ pay: null, account: null, payHidden: true })),
    [`GET /api/employees/${wanjiru.id}/pay-items`]: () => json(200, []),
    [`GET ${changes}`]: () => json(200, [row({ validTo: '2026-08-31' }), row({ id: '0199c0de-0000-7000-8000-0000000c0002', validFrom: '2026-09-01', amount: 90000, reason: 'Promoted to head cook' })].reverse()),
    [`GET /api/pay-runs/${october.id}`]: () => json(200, october),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions: permissions.map(grant) }) } : {}),
    ...routes,
  })
}

async function openPay() {
  renderApp('/setup/employees')
  await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')
  await userEvent.click(screen.getByRole('button', { name: 'View Wanjiru Kamau' }))
  const view = await screen.findByRole('dialog', { name: 'Wanjiru Kamau' })
  await userEvent.click(within(view).getByRole('tab', { name: 'Pay' }))
  return view
}
const sent = (calls: ReturnType<typeof fakeApi>) => calls.filter(c => c.method === 'POST' && c.path === changes).map(c => c.body)

it('pay over time shows after Show amounts, newest first, with why it changed', async () => {
  const calls = withProfile()
  const view = await openPay()
  const history = within(view).getByRole('region', { name: 'Pay over time' })
  expect(history).toHaveTextContent('Show amounts to see pay over time.')
  expect(calls.some(c => c.path === changes)).toBe(false)
  await userEvent.click(within(view).getByRole('button', { name: 'Show amounts' }))
  const items = await within(history).findAllByRole('listitem')
  expect(items.map(i => i.textContent)).toEqual([
    'From 1 Sep 2026KES 90,000 a monthPromoted to head cook',
    'From 15 Jan 2024 to 31 Aug 2026KES 85,000 a month',
  ])
})

it('changes pay from an earlier date, saying why, and says which runs to work out again and which to correct', async () => {
  const calls = withProfile({
    [`POST ${changes}`]: () => json(200, {
      change: row({ id: '0199c0de-0000-7000-8000-0000000c0003', validFrom: '2026-09-01', amount: 95000, reason: 'Rise agreed in August' }),
      sentBackToDraft: [october.id],
      needCorrection: [{ payRunId: '0199c0de-0000-7000-8000-0000000a0009', periodStart: '2026-09-01', periodEnd: '2026-09-30', payDate: '2026-09-30' }],
    }),
  })
  const view = await openPay()
  await userEvent.click(within(view).getByRole('button', { name: 'Change pay from an earlier date' }))
  const form = screen.getByRole('dialog', { name: 'Change pay from an earlier date' })
  await userEvent.click(within(form).getByRole('button', { name: 'Save' }))
  expect(within(form).getByText('Enter the basic pay, more than 0.')).toBeInTheDocument()
  expect(within(form).getByText('Choose the date the pay changes from.')).toBeInTheDocument()
  expect(within(form).getByText('Say why the pay changes from this date.')).toBeInTheDocument()
  // Usual days or hours are only for a daily or hourly rate.
  expect(within(form).queryByRole('textbox', { name: /Usual/ })).not.toBeInTheDocument()

  await userEvent.type(within(form).getByRole('textbox', { name: 'Basic pay' }), '95,000')
  await userEvent.type(within(form).getByLabelText('Pay changes from'), '2026-09-01')
  await userEvent.type(within(form).getByRole('textbox', { name: 'Why' }), 'Rise agreed in August')
  await userEvent.click(within(form).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(sent(calls)).toEqual([{ amount: 95000, basis: 'monthly', usualQuantity: null, effectiveFrom: '2026-09-01', reason: 'Rise agreed in August' }]))

  const done = await within(view).findByRole('status')
  expect(done).toHaveTextContent('Pay changes from 1 Sep 2026.')
  expect(await within(done).findByRole('link', { name: 'October 2026' })).toHaveAttribute('href', `/pay/runs/${october.id}`)
  expect(done).toHaveTextContent('Back to draft, to be worked out again')
  expect(done).toHaveTextContent('Approved, to correct for the difference')
  expect(within(done).getByRole('link', { name: 'September 2026' })).toHaveAttribute('href', '/pay/runs/0199c0de-0000-7000-8000-0000000a0009')
  await expectNoA11yViolations(document.body)
})

it('a daily rate asks for usual days; the API\'s problems show on their fields, and a run waiting for approval as a banner', async () => {
  let answer = json(400, { code: 'PAY-2028', message: "Some pay change details don't look right. Check the fields marked.", reference: '7F3K92QD',
    fields: { effectiveFrom: 'Choose a date on or after they started (15 Jan 2024).' } })
  const calls = withProfile({ [`POST ${changes}`]: () => answer })
  const view = await openPay()
  await userEvent.click(within(view).getByRole('button', { name: 'Change pay from an earlier date' }))
  const form = screen.getByRole('dialog', { name: 'Change pay from an earlier date' })
  await userEvent.click(within(form).getByRole('combobox', { name: 'Paid' }))
  await userEvent.click(screen.getByRole('option', { name: 'By the day' }))
  await userEvent.type(within(form).getByRole('textbox', { name: 'Basic pay' }), '800')
  await userEvent.type(within(form).getByRole('textbox', { name: /^Usual days each pay run/ }), '22')
  await userEvent.type(within(form).getByLabelText('Pay changes from'), '2023-01-01')
  await userEvent.type(within(form).getByRole('textbox', { name: 'Why' }), 'Wrong rate')
  await userEvent.click(within(form).getByRole('button', { name: 'Save' }))
  expect(await within(form).findByText('Choose a date on or after they started (15 Jan 2024).')).toBeInTheDocument()
  expect(sent(calls)[0]).toEqual({ amount: 800, basis: 'daily', usualQuantity: 22, effectiveFrom: '2023-01-01', reason: 'Wrong rate' })

  answer = apiError(409, 'PAY-2021', 'This pay run is waiting for approval. Cancel the request to change it.')
  await userEvent.click(within(form).getByRole('button', { name: 'Save' }))
  expect(await within(form).findByText(/This pay run is waiting for approval/)).toBeInTheDocument()
  expect(within(form).getByText('PAY-2021', { exact: false })).toBeInTheDocument()
})

it('without Manage on pay there\'s no change button; pay over time still shows', async () => {
  withProfile({}, ['employees.view', 'pay.view'])
  const view = await openPay()
  await userEvent.click(within(view).getByRole('button', { name: 'Show amounts' }))
  expect(await within(within(view).getByRole('region', { name: 'Pay over time' })).findAllByRole('listitem')).toHaveLength(2)
  expect(within(view).queryByRole('button', { name: 'Change pay from an earlier date' })).not.toBeInTheDocument()
})
