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

const weekly = schedule({
  id: '0199c0de-0000-7000-8000-00000000f004', name: 'Casuals weekly', frequency: 'weekly', payDayRule: null, cutoffDay: null, payWeekday: 5, cutoffDaysBefore: 2,
  allCompanies: false, companyIds: [restaurant.id], defaultFor: [], nextPayDates: ['2026-10-16', '2026-10-23', '2026-10-30'],
})
const fortnightly = schedule({
  id: '0199c0de-0000-7000-8000-00000000f005', name: 'Fortnightly', frequency: 'fortnightly', payDayRule: null, cutoffDay: null, payWeekday: 3, firstPayDate: '2026-10-14',
  cutoffDaysBefore: 0, allCompanies: false, companyIds: [restaurant.id], defaultFor: [], nextPayDates: ['2026-10-14', '2026-10-28', '2026-11-11'],
})
const daily = schedule({
  id: '0199c0de-0000-7000-8000-00000000f006', name: 'Casuals daily', frequency: 'daily', payDayRule: null, cutoffDay: null, payTime: '17:00:00', usesCutoff: false, cutoffAppliesTo: [],
  allCompanies: false, companyIds: [restaurant.id], defaultFor: [], nextPayDates: ['2026-10-12', '2026-10-13', '2026-10-14'],
})

it('shows weekly, every-two-weeks and daily schedules in their own words', async () => {
  oneCompany({ 'GET /api/pay-schedules': () => json(200, [monthly, weekly, fortnightly, daily]) })
  renderApp('/setup/schedules')
  await screen.findByRole('region', { name: 'Payroll schedules' })
  const cells = async (name: string) =>
    within((await within(table()).findByRole('button', { name: `Edit ${name}` })).closest('tr')!).getAllByRole('cell').map(c => c.textContent).slice(1, 4)

  expect(await cells('Casuals weekly')).toEqual(['Weekly', 'Every Friday', '2 days before'])
  expect(await cells('Fortnightly')).toEqual(['Every two weeks', 'Every other Wednesday', 'On the pay day'])
  expect(await cells('Casuals daily')).toEqual(['Daily', 'Working days, 5:00 pm', '—'])
})

it('adds a schedule every two weeks, starting on one of the next two pay days', async () => {
  const calls = oneCompany({
    'GET /api/pay-schedules/pay-dates': req => {
      const q = new URL(req.url).searchParams
      if (q.get('frequency') === 'weekly') return json(200, ['2026-10-16', '2026-10-23', '2026-10-30'])
      if (q.get('frequency') === 'fortnightly') return json(200, q.get('firstPayDate') === '2026-10-23' ? ['2026-10-23', '2026-11-06', '2026-11-20'] : ['2026-10-16', '2026-10-30', '2026-11-13'])
      return json(200, ['2026-10-30', '2026-11-30', '2026-12-31'])
    },
    'POST /api/pay-schedules': () => json(201, fortnightly),
  })
  renderApp('/setup/schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Add schedule' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Fortnightly')
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'How often' }))
  await choose('How often', 'two', 'Every two weeks')

  expect(within(drawer()).queryByRole('radio', { name: 'Last working day' })).not.toBeInTheDocument()
  expect(within(drawer()).getByRole('combobox', { name: 'Pay day' })).toHaveValue('Friday')
  expect(within(drawer()).getByRole('combobox', { name: 'Cut-off' })).toHaveValue('2 days before')
  await choose('First pay date', '23', 'Fri 23 Oct 2026')
  expect(await within(drawer()).findByText('Fri 6 Nov 2026')).toBeInTheDocument()
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))

  expect(await screen.findByText('Schedule added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-schedules')?.body).toMatchObject({
    name: 'Fortnightly', frequency: 'fortnightly', payWeekday: 5, firstPayDate: '2026-10-23', cutoffDaysBefore: 2, payDayRule: null, cutoffDay: null, payTime: null,
  })
})

it('a daily schedule asks only for the pay time', async () => {
  const calls = oneCompany({ 'POST /api/pay-schedules': () => json(201, daily) })
  renderApp('/setup/schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Add schedule' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Casuals daily')
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'How often' }))
  await choose('How often', 'dai', 'Daily')

  expect(within(drawer()).queryByRole('combobox', { name: /Cut-off/ })).not.toBeInTheDocument()
  expect(within(drawer()).getByRole('combobox', { name: 'Pay time' })).toHaveValue('5:00 pm')
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'Pay time' }))
  await choose('Pay time', '6:30', '6:30 pm')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))

  expect(await screen.findByText('Schedule added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-schedules')?.body).toMatchObject({
    frequency: 'daily', payTime: '18:30', payDayRule: null, cutoffDay: null, payWeekday: null, cutoffDaysBefore: null,
  })
})

it('the cut-off is explained, optional, and covers what is chosen, including your own items', async () => {
  const fullMonth = schedule({ id: '0199c0de-0000-7000-8000-00000000f007', name: 'Full month', cutoffDay: null, usesCutoff: false, cutoffAppliesTo: [], defaultFor: [] })
  const withTips = schedule({ id: '0199c0de-0000-7000-8000-00000000f008', name: 'With tips', cutoffAppliesTo: ['overtime', 'Tips'], defaultFor: [] })
  const calls = oneCompany({
    'GET /api/pay-schedules': () => json(200, [monthly, fullMonth, withTips]),
    'POST /api/pay-schedules': () => json(201, fullMonth),
  })
  renderApp('/setup/schedules')
  await screen.findByRole('region', { name: 'Payroll schedules' })
  const row = (await within(table()).findByRole('button', { name: 'Edit Full month' })).closest('tr')!
  expect(within(row).getAllByRole('cell')[3]).toHaveTextContent('None')

  await userEvent.click(screen.getByRole('button', { name: 'Add schedule' }))
  const uses = within(drawer()).getByRole('switch', { name: 'Use a cut-off' })
  expect(uses).toBeChecked()
  expect(uses).toHaveAccessibleDescription(/last day changes count toward this payroll/)
  const covers = within(drawer()).getByRole('button', { name: /What the cut-off applies to/ })
  expect(covers).toHaveTextContent('6 selected')

  // Your own item: the first choice, in a small form on top of the drawer.
  await userEvent.click(covers)
  expect(screen.getByRole('option', { name: 'Tips' })).toHaveAttribute('aria-selected', 'false')   // added on another schedule
  await userEvent.click(screen.getByRole('button', { name: 'Add your own' }))
  const modal = screen.getByRole('dialog', { name: 'Add what the cut-off applies to' })
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Item' }), 'Expense claims')
  await userEvent.click(within(modal).getByRole('button', { name: 'Add item' }))
  expect(screen.queryByRole('dialog', { name: 'Add what the cut-off applies to' })).not.toBeInTheDocument()
  expect(within(drawer()).getByRole('button', { name: /What the cut-off applies to/ })).toHaveTextContent('7 selected')

  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Monthly with claims')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-schedules')?.body).toMatchObject({
    usesCutoff: true, cutoffDay: 25,
    cutoffAppliesTo: ['overtime', 'new_employees', 'leavers', 'unpaid_leave', 'pay_changes', 'one_off', 'Expense claims'],
  })
})

it('turning the cut-off off keeps none of it', async () => {
  const calls = oneCompany({ 'POST /api/pay-schedules': () => json(201, monthly) })
  renderApp('/setup/schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Add schedule' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Full month')

  await userEvent.click(within(drawer()).getByRole('switch', { name: 'Use a cut-off' }))

  expect(within(drawer()).queryByRole('combobox', { name: 'Cut-off day' })).not.toBeInTheDocument()
  expect(within(drawer()).queryByRole('button', { name: /What the cut-off applies to/ })).not.toBeInTheDocument()
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-schedules')?.body).toMatchObject({
    usesCutoff: false, cutoffDay: null, cutoffDaysBefore: null, cutoffAppliesTo: [],
  })
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
    name: 'Mid-month', frequency: 'monthly', payDayRule: 'fixed_day', payDay: 15, cutoffDay: 25, payWeekday: null, firstPayDate: null, cutoffDaysBefore: null,
    payTime: null, allCompanies: false, companyIds: [restaurant.id], defaultFor: [],
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
