import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { apiError, company, fakeApi, json, me, orgToken, schedule } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

afterEach(() => { vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const mombasa = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Mombasa Ltd' })
const grant = (permission: string) => ({ permission, allCompanies: true, companyIds: [] as string[] })
const hr = ['employees.view', 'employees.manage', 'schedules.view'].map(grant)
const payroll = ['employees.view', 'pay.view', 'schedules.view', 'schedules.manage'].map(grant)

const monthly = schedule({ defaultFor: [westlands.id, mombasa.id], people: 12 })
const weekly = schedule({
  id: '0199c0de-0000-7000-8000-00000000f002', name: 'Casuals', frequency: 'weekly', payDayRule: null, cutoffDay: null, payWeekday: 5, cutoffDaysBefore: 2,
  allCompanies: false, companyIds: [westlands.id], defaultFor: [], nextPayDates: ['2026-10-30', '2026-11-06', '2026-11-13'], people: 4,
})
const daily = schedule({
  id: '0199c0de-0000-7000-8000-00000000f003', name: 'Day workers', frequency: 'daily', payDayRule: null, cutoffDay: null, payTime: '17:00:00', usesCutoff: false,
  cutoffAppliesTo: [], allCompanies: false, companyIds: [mombasa.id], defaultFor: [], nextPayDates: ['2026-10-27'], people: 0,
})

/** The list the API gives for a company: the schedules that serve it, with its people. */
const forCompany = (id: string | null) => (id === mombasa.id ? [{ ...monthly, people: 3 }, daily] : [monthly, weekly])

/** Signed in with these permissions (none: everything), two companies. */
function signedIn(permissions?: ReturnType<typeof grant>[], routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    'GET /api/pay-schedules': req => json(200, forCompany(new URL(req.url).searchParams.get('companyId'))),
    'GET /api/pay-schedules/pay-dates': () => json(200, ['2026-10-30', '2026-11-30', '2026-12-31']),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions }) } : {}),
    ...routes,
  })
}

const table = () => screen.getByRole('region', { name: 'Payroll schedules' })
const drawer = () => screen.getByRole('dialog')
const listCalls = (calls: ReturnType<typeof fakeApi>) =>
  calls.filter(c => c.method === 'GET' && c.path === '/api/pay-schedules').map(c => new URL(c.url).searchParams)
/** A schedule's row, by its name ("Monthly" is also a frequency, shown under the name). */
const rowOf = async (name: string) => (await within(await screen.findByRole('region', { name: 'Payroll schedules' })).findAllByText(name))[0].closest('tr')!
const switchCompany = async (name: string) => {
  await userEvent.click(within(screen.getByRole('banner')).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getByRole('option', { name }))
}

it('opens in the shell for the top bar\'s company: schedule, pay day with its cut-off, companies, people and next pay dates', async () => {
  const calls = signedIn()
  const { container } = renderApp('/organization/payroll-schedules')

  expect(await screen.findByRole('heading', { name: 'Payroll schedules', level: 1 })).toBeInTheDocument()
  expect(await within(screen.getByRole('navigation', { name: 'Main' })).findByRole('link', { name: 'Payroll schedules' })).toHaveAttribute('aria-current', 'page')
  const row = await rowOf('Monthly')
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Schedule', 'Pay day', 'Companies', 'People', 'Next pay dates', 'Actions'])
  expect(within(row).getAllByRole('cell').map(c => c.textContent).slice(0, 5))
    .toEqual(['MonthlyMonthly', 'Last working day · cut-off 25th', 'All companies', '12', 'Fri 30 Oct 2026Mon 30 Nov 2026Thu 31 Dec 2026'])
  expect(within(await rowOf('Casuals')).getAllByRole('cell').map(c => c.textContent).slice(0, 4))
    .toEqual(['CasualsWeekly', 'Every Friday · cut-off 2 days before', 'Westlands Ltd', '4'])
  // Only the top bar's company is asked for.
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('companyId')).toBe(westlands.id))
  await expectNoA11yViolations(container)
})

it('switching the company in the top bar changes the schedules and their people', async () => {
  const calls = signedIn()
  renderApp('/organization/payroll-schedules')
  await rowOf('Casuals')

  await switchCompany('Mombasa Ltd')
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('companyId')).toBe(mombasa.id))
  expect(within(await rowOf('Day workers')).getAllByRole('cell').map(c => c.textContent).slice(0, 4))
    .toEqual(['Day workersDaily', 'Working days, 5:00 pm', 'Mombasa Ltd', '0'])
  expect(within(await rowOf('Monthly')).getAllByRole('cell')[3]).toHaveTextContent('3')
  expect(within(table()).queryByText('Casuals')).not.toBeInTheDocument()
})

it('Add schedule starts with only the top bar\'s company, never all companies', async () => {
  const calls = signedIn(undefined, { 'POST /api/pay-schedules': () => json(201, schedule({ id: 'new', name: 'Mid-month', allCompanies: false, companyIds: [mombasa.id] })) })
  renderApp('/organization/payroll-schedules')
  await rowOf('Monthly')
  await switchCompany('Mombasa Ltd')
  await rowOf('Day workers')

  await userEvent.click(screen.getByRole('button', { name: 'Add schedule' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Mid-month')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))

  expect(await screen.findByText('Schedule added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-schedules')?.body).toMatchObject({ allCompanies: false, companyIds: [mombasa.id] })
})

it('edit switches the cut-off on, with its day, and the list is fetched again', async () => {
  const noCutoff = { ...weekly, frequency: 'monthly', payDayRule: 'last_working_day', payWeekday: null, cutoffDaysBefore: null, usesCutoff: false, cutoffAppliesTo: [] }
  const calls = signedIn(undefined, {
    'GET /api/pay-schedules': () => json(200, [noCutoff]),
    [`PATCH /api/pay-schedules/${weekly.id}`]: () => json(200, { ...noCutoff, usesCutoff: true, cutoffDay: 20 }),
  })
  renderApp('/organization/payroll-schedules')
  expect(within(await rowOf('Casuals')).getAllByRole('cell')[1]).toHaveTextContent('Last working day · no cut-off')
  const before = listCalls(calls).length

  await userEvent.click(screen.getByRole('button', { name: 'Edit Casuals' }))
  await userEvent.click(within(drawer()).getByRole('switch', { name: 'Use a cut-off' }))
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'Cut-off day' }))
  await userEvent.type(within(drawer()).getByRole('combobox', { name: 'Cut-off day' }), '20')
  await userEvent.click(within(drawer()).getByRole('option', { name: '20th' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Schedule saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PATCH')?.body).toMatchObject({ usesCutoff: true, cutoffDay: 20 })
  await vi.waitFor(() => expect(listCalls(calls).length).toBeGreaterThan(before))
})

it('deleting asks first; a refusal shows the reason with its code, and the schedule stays', async () => {
  signedIn(undefined, {
    [`DELETE /api/pay-schedules/${weekly.id}`]: () => apiError(409, 'ONB-3541', 'Casuals is used by 4 employees. Move them first.'),
  })
  renderApp('/organization/payroll-schedules')
  await rowOf('Casuals')

  await userEvent.click(screen.getByRole('button', { name: 'Delete Casuals' }))
  const confirm = screen.getByRole('alertdialog', { name: 'Delete Casuals?' })
  await userEvent.click(within(confirm).getByRole('button', { name: 'Delete schedule' }))

  const problem = await within(confirm).findByRole('alert')
  expect(problem).toHaveTextContent('Casuals is used by 4 employees. Move them first.')
  expect(problem).toHaveTextContent('Code ONB-3541 · Reference 7F3K92QD')
  await userEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }))
  expect(within(table()).getByText('Casuals')).toBeInTheDocument()
})

it('deleting a schedule removes it from the list', async () => {
  let deleted = false
  signedIn(undefined, {
    'GET /api/pay-schedules': () => json(200, deleted ? [monthly] : [monthly, weekly]),
    [`DELETE /api/pay-schedules/${weekly.id}`]: () => { deleted = true; return new Response(null, { status: 204 }) },
  })
  renderApp('/organization/payroll-schedules')
  await rowOf('Casuals')

  await userEvent.click(screen.getByRole('button', { name: 'Delete Casuals' }))
  await userEvent.click(within(screen.getByRole('alertdialog', { name: 'Delete Casuals?' })).getByRole('button', { name: 'Delete schedule' }))

  expect(await screen.findByText('Schedule deleted')).toBeInTheDocument()
  await vi.waitFor(() => expect(within(table()).queryByText('Casuals')).not.toBeInTheDocument())
})

it('Payroll with Manage can add, edit and delete', async () => {
  signedIn(payroll)
  renderApp('/organization/payroll-schedules')
  await rowOf('Monthly')
  expect(screen.getByRole('button', { name: 'Add schedule' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Edit Monthly' })).toBeInTheDocument()
})

it('someone who may only view schedules (HR, by the address) sees the list without Add, Edit or Delete', async () => {
  signedIn(hr)
  renderApp('/organization/payroll-schedules')
  await rowOf('Monthly')
  expect(screen.queryByRole('button', { name: 'Add schedule' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Edit Monthly' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Delete Monthly' })).not.toBeInTheDocument()
  expect(within(table()).queryByRole('columnheader', { name: 'Actions' })).not.toBeInTheDocument()
})

it('without View on schedules for the company, says so and asks for nothing', async () => {
  const calls = signedIn(['employees.view'].map(grant))
  renderApp('/organization/payroll-schedules')
  expect(await screen.findByText('You can\'t see this company\'s payroll schedules')).toBeInTheDocument()
  expect(listCalls(calls)).toHaveLength(0)
})
