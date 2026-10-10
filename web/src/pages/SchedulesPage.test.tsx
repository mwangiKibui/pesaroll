import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, location, orgToken, schedule } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const signedIn = { 'POST /api/auth/refresh': () => orgToken() }
const restaurant = company()
const catering = company({ id: '0199c0de-0000-7000-8000-00000000c002', legalName: 'Oliech Catering Ltd', tradingName: null })
const monthly = schedule()
const midMonth = schedule({
  id: '0199c0de-0000-7000-8000-00000000f002', name: 'Mid-month', payDayRule: 'fixed_day', payDay: 15, cutoffDay: 10, allCompanies: false,
  companyIds: [catering.id], defaultFor: [catering.id], nextPayDates: ['2026-11-13', '2026-12-15', '2027-01-15'],
})
const kitchen = { id: 'dp1', code: 'KIT-01', name: 'Kitchen', divisionId: null, parentId: null, allCompanies: true, companyIds: [] }

const drawer = () => screen.getByRole('dialog')
const table = () => screen.getByRole('region', { name: 'Payroll schedules' })

async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(drawer()).getByRole('combobox', { name }), search)
  await userEvent.click(within(drawer()).getByRole('option', { name: option }))
}

function oneCompany(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [restaurant]),
    'GET /api/locations': () => json(200, [location()]),
    'GET /api/structure/departments': () => json(200, [kitchen]),
    'GET /api/pay-schedules': () => json(200, [monthly]),
    // The preview: a fixed 15th, or the last working day.
    'GET /api/pay-schedules/pay-dates': req => new URL(req.url).searchParams.get('payDay') === '15'
      ? json(200, ['2026-10-15', '2026-11-13', '2026-12-15'])
      : json(200, ['2026-10-30', '2026-11-30', '2026-12-31']),
    ...routes,
  })
}

it('lists the suggested Monthly schedule with its pay day, cut-off and next pay dates', async () => {
  oneCompany()
  const { container } = renderApp('/setup/schedules')

  expect(await screen.findByRole('heading', { name: 'When do you pay your people?' })).toBeInTheDocument()
  const row = (await within(table()).findByRole('button', { name: 'Edit Monthly' })).closest('tr')!
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Schedule', 'How often', 'Pay day', 'Cut-off', 'Next pay dates', 'Applies to', 'Default for', 'Actions'])
  expect(within(row).getAllByRole('cell').map(c => c.textContent).slice(0, 7)).toEqual([
    'Monthly', 'Monthly', 'Last working day', '25th', 'Fri 30 Oct 2026Mon 30 Nov 2026Thu 31 Dec 2026', 'All companies', 'Mama Oliech Restaurant Ltd',
  ])
  expect(screen.queryByRole('button', { name: /Show companies/ })).not.toBeInTheDocument()
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Payroll schedules').closest('a')).toHaveAttribute('aria-current', 'page')
  expect(within(steps).getByText('Payroll schedules').closest('li')).toHaveTextContent('✓')
  await expectNoA11yViolations(container)
})

it('with several companies, the selector filters the table to the chosen company', async () => {
  oneCompany({ 'GET /api/companies': () => json(200, [restaurant, catering]), 'GET /api/pay-schedules': () => json(200, [monthly, midMonth]) })
  renderApp('/setup/schedules')
  const row = (await within(await screen.findByRole('region', { name: 'Payroll schedules' })).findByText('Mid-month')).closest('tr')!
  expect(within(row).getAllByRole('cell')[2]).toHaveTextContent('15th')

  await userEvent.click(screen.getByRole('button', { name: /Show companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Ltd/ }))   // untick: only the restaurant stays
  await userEvent.keyboard('{Escape}')

  expect(within(table()).getByRole('button', { name: 'Edit Monthly' })).toBeInTheDocument()   // for all companies
  expect(within(table()).queryByText('Mid-month')).not.toBeInTheDocument()
})

it('adds a schedule on a fixed day, showing its next pay dates before saving', async () => {
  let schedules = [monthly]
  const added = schedule({ id: '0199c0de-0000-7000-8000-00000000f003', name: 'Mid-month', payDayRule: 'fixed_day', payDay: 15, allCompanies: false, companyIds: [restaurant.id], defaultFor: [] })
  const calls = oneCompany({
    'GET /api/pay-schedules': () => json(200, schedules),
    'POST /api/pay-schedules': () => { schedules = [monthly, added]; return json(201, added) },
  })
  renderApp('/setup/schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Add schedule' }))

  expect(within(drawer()).getByRole('heading', { name: 'Add schedule' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('combobox', { name: 'How often' })).toHaveValue('Monthly')
  expect(within(drawer()).getByRole('radio', { name: 'Last working day' })).toBeChecked()
  expect(await within(drawer()).findByText('Fri 30 Oct 2026')).toBeInTheDocument()
  expect(within(drawer()).queryByRole('combobox', { name: 'Day of the month' })).not.toBeInTheDocument()

  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Mid-month')
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'A fixed day' }))
  await choose('Day of the month', '15', '15th')
  expect(await within(drawer()).findByText('Fri 13 Nov 2026')).toBeInTheDocument()
  expect(within(drawer()).getByRole('combobox', { name: 'Cut-off day' })).toHaveValue('25th')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))

  expect(await screen.findByText('Schedule added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-schedules')?.body).toMatchObject({
    name: 'Mid-month', frequency: 'monthly', payDayRule: 'fixed_day', payDay: 15, cutoffDay: 25, allCompanies: false, companyIds: [restaurant.id], defaultFor: [],
  })
  expect(await within(table()).findByText('Mid-month')).toBeInTheDocument()
})

it('checks the details before sending; problems the server finds show under their fields', async () => {
  const calls = oneCompany({ 'POST /api/pay-schedules': () => apiError(409, 'ONB-3525', 'You already have a schedule with this name.') })
  renderApp('/setup/schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Add schedule' }))

  await userEvent.click(within(drawer()).getByRole('radio', { name: 'A fixed day' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))
  expect(within(drawer()).getByRole('textbox', { name: 'Schedule name' })).toHaveAccessibleDescription("Enter the schedule's name (2 to 60 characters).")
  expect(within(drawer()).getByRole('combobox', { name: 'Day of the month' })).toHaveAccessibleDescription('Choose a pay day from 1 to 28.')
  expect(calls.filter(c => c.method === 'POST' && c.path === '/api/pay-schedules')).toHaveLength(0)

  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Monthly')
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'Last day of the month' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))
  expect(await within(drawer()).findByText('You already have a schedule with this name.')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-schedules')?.body).toMatchObject({ payDayRule: 'last_day', payDay: null })
})

it('edits a schedule', async () => {
  const calls = oneCompany({ [`PATCH /api/pay-schedules/${monthly.id}`]: () => json(200, { ...monthly, cutoffDay: 20 }) })
  renderApp('/setup/schedules')

  await userEvent.click(await screen.findByRole('button', { name: 'Edit Monthly' }))
  expect(within(drawer()).getByRole('heading', { name: 'Edit schedule' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('checkbox', { name: 'Default for Mama Oliech Restaurant Ltd' })).toBeChecked()
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'Cut-off day' }))
  await choose('Cut-off day', '20', '20th')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Schedule saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PATCH')?.body).toMatchObject({ name: 'Monthly', cutoffDay: 20, allCompanies: true, defaultFor: [restaurant.id] })
})

it('deleting asks first; a default schedule is refused with the reason', async () => {
  oneCompany({
    [`DELETE /api/pay-schedules/${monthly.id}`]: () => apiError(409, 'ONB-3527', 'This is the default schedule of Mama Oliech Restaurant Ltd. Make another schedule their default first.'),
  })
  renderApp('/setup/schedules')

  await userEvent.click(await screen.findByRole('button', { name: 'Delete Monthly' }))
  await userEvent.click(within(screen.getByRole('alertdialog', { name: 'Delete Monthly?' })).getByRole('button', { name: 'Delete schedule' }))

  expect(await screen.findByText(/This is the default schedule of Mama Oliech Restaurant Ltd/)).toBeInTheDocument()
  expect(within(table()).getByRole('button', { name: 'Edit Monthly' })).toBeInTheDocument()
})

it('Proceed goes to the dashboard with five steps done', async () => {
  oneCompany()
  renderApp('/setup/schedules')

  await userEvent.click(await within(await screen.findByRole('main')).findByRole('button', { name: 'Proceed to Org Dashboard' }))

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(await screen.findByText('Finish setting up · 5 of 10')).toBeInTheDocument()
  expect(screen.getAllByText('Next: Pay items').length).toBeGreaterThan(0)
})
