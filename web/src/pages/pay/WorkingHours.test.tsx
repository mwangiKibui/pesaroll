import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { company, fakeApi, json, me, orgToken, schedule } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

beforeEach(() => vi.stubEnv('VITE_FEATURES', 'payRuns'))
afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const hours = `/api/companies/${westlands.id}/working-hours`
const grant = (permission: string) => ({ permission, allCompanies: true, companyIds: [] as string[] })
const wageOrder = { companyId: westlands.id, hoursPerDay: null, hoursPerMonth: null, usedPerDay: 8, usedPerMonth: 225, setBy: null, setAt: null }
const own = { ...wageOrder, hoursPerDay: 9, hoursPerMonth: 195, usedPerDay: 9, usedPerMonth: 195, setBy: me.id, setAt: '2026-10-20T07:00:00Z' }

function signedIn(routes: Parameters<typeof fakeApi>[0] = {}, permissions?: string[]) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands]),
    'GET /api/pay-schedules': () => json(200, [schedule()]),
    'GET /api/pay-runs': () => json(200, { items: [], total: 0, page: 1, pageSize: 10 }),
    [`GET ${hours}`]: () => json(200, wageOrder),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions: permissions.map(grant) }) } : {}),
    ...routes,
  })
}
const sent = (calls: ReturnType<typeof fakeApi>) => calls.filter(c => c.method === 'PUT' && c.path === hours).map(c => c.body)

it('Pay runs says the working hours used, and Payroll sets the company\'s own', async () => {
  const calls = signedIn({ [`PUT ${hours}`]: () => json(200, own) })
  const { container } = renderApp('/pay/runs')
  await userEvent.click(await screen.findByRole('button', { name: 'Working hours: 8 a day, 225 a month' }))
  const sheet = screen.getByRole('dialog', { name: 'Working hours' })
  expect(sheet).toHaveTextContent('Westlands Ltd uses the wage order\'s: 8 hours a day, 225 a month.')
  await userEvent.type(within(sheet).getByRole('textbox', { name: /^Hours in a working day/ }), '9')
  await userEvent.type(within(sheet).getByRole('textbox', { name: /^Hours in a working month/ }), '195')
  await userEvent.click(within(sheet).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(sent(calls)).toEqual([{ hoursPerDay: 9, hoursPerMonth: 195 }]))
  expect(await screen.findByRole('button', { name: 'Working hours: 9 a day, 195 a month' })).toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('the company\'s own hours can go back to the wage order\'s; wrong hours are marked', async () => {
  const calls = signedIn({
    [`GET ${hours}`]: () => json(200, own),
    [`PUT ${hours}`]: () => json(200, wageOrder),
  })
  renderApp('/pay/runs')
  await userEvent.click(await screen.findByRole('button', { name: 'Working hours: 9 a day, 195 a month' }))
  const sheet = screen.getByRole('dialog', { name: 'Working hours' })
  expect(sheet).toHaveTextContent('Westlands Ltd\'s own: 9 hours a day, 195 a month, set on 20 Oct 2026.')
  const day = within(sheet).getByRole('textbox', { name: /^Hours in a working day/ })
  expect(day).toHaveValue('9')
  await userEvent.clear(day)
  await userEvent.type(day, '25')
  await userEvent.click(within(sheet).getByRole('button', { name: 'Save' }))
  expect(within(sheet).getByText('Enter more than 0 and up to 24, or leave it empty for the wage order\'s.')).toBeInTheDocument()
  expect(sent(calls)).toEqual([])
  await userEvent.clear(day)
  await userEvent.clear(within(sheet).getByRole('textbox', { name: /^Hours in a working month/ }))
  await userEvent.click(within(sheet).getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(sent(calls)).toEqual([{ hoursPerDay: null, hoursPerMonth: null }]))
})

it('with View on pay only, the hours show without fields or Save; without it, there\'s no button', async () => {
  signedIn({}, ['pay_runs.view', 'pay.view'])
  const { unmount } = renderApp('/pay/runs')
  await userEvent.click(await screen.findByRole('button', { name: 'Working hours: 8 a day, 225 a month' }))
  const sheet = screen.getByRole('dialog', { name: 'Working hours' })
  expect(within(sheet).queryByRole('textbox')).not.toBeInTheDocument()
  expect(within(sheet).queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  unmount()

  const calls = signedIn({}, ['pay_runs.view'])
  renderApp('/pay/runs')
  expect(await screen.findByText('No pay runs yet')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Working hours/ })).not.toBeInTheDocument()
  expect(calls.some(c => c.path === hours)).toBe(false)
})
