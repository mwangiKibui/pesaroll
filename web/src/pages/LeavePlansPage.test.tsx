import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, leavePlan, leaveTypes, location, orgToken, schedule } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const signedIn = { 'POST /api/auth/refresh': () => orgToken() }
const restaurant = company()
const catering = company({ id: '0199c0de-0000-7000-8000-00000000c002', legalName: 'Oliech Catering Ltd', tradingName: null })
const kitchen = { id: 'dp1', code: 'KIT-01', name: 'Kitchen', divisionId: null, parentId: null, allCompanies: true, companyIds: [] }
const daily = schedule({ id: '0199c0de-0000-7000-8000-00000000f009', name: 'Casuals daily', frequency: 'daily', payDayRule: null, cutoffDay: null, payTime: '17:00:00', defaultFor: [] })
const standard = leavePlan()

const drawer = () => screen.getByRole('dialog', { name: /plan|leave type/i })
const panel = (name: string) => screen.getByRole('tabpanel', { name })

async function choose(within_: HTMLElement, name: string, search: string, option: string) {
  await userEvent.type(within(within_).getByRole('combobox', { name }), search)
  await userEvent.click(within(within_).getByRole('option', { name: option }))
}

function withLeave(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [restaurant]),
    'GET /api/locations': () => json(200, [location()]),
    'GET /api/structure/departments': () => json(200, [kitchen]),
    'GET /api/pay-schedules': () => json(200, [schedule(), daily]),
    'GET /api/leave-types': () => json(200, leaveTypes()),
    'GET /api/leave-plans': () => json(200, [standard]),
    ...routes,
  })
}

it('lists the Standard plan in plain words, and the leave types with the law\'s locked', async () => {
  withLeave()
  const { container } = renderApp('/setup/leave-plans')

  expect(await screen.findByRole('heading', { name: 'What leave do your people get?' })).toBeInTheDocument()
  expect(screen.getAllByRole('tab').map(t => t.textContent)).toEqual(['Leave plans', 'Leave types'])
  const row = (await within(await screen.findByRole('tabpanel', { name: 'Leave plans' })).findByText('Standard')).closest('tr')!
  const summary = within(row).getAllByRole('cell')[1].textContent
  expect(summary).toContain('Annual leave: 1.75 days a month')
  expect(summary).toContain('Sick leave: 7 days a year + 7 on half pay')
  expect(summary).toContain('Maternity leave: 90 days each time')
  expect(summary).toContain('Unpaid leave: no limit')
  expect(within(row).getAllByRole('cell')[3]).toHaveTextContent('Mama Oliech Restaurant Ltd')

  await userEvent.click(screen.getByRole('tab', { name: 'Leave types' }))
  const sick = within(panel('Leave types')).getByText('Sick leave').closest('tr')!
  expect(within(sick).getAllByRole('cell').map(c => c.textContent).slice(1, 5)).toEqual(['Working days', 'Paid', 'Needed', 'Stops'])
  expect(within(sick).getByText('Set by law')).toBeInTheDocument()
  expect(within(sick).queryByRole('button', { name: 'Delete Sick leave' })).not.toBeInTheDocument()
  expect(within(panel('Leave types')).getByRole('button', { name: 'Delete Unpaid leave' })).toBeInTheDocument()
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Leave plans').closest('a')).toHaveAttribute('aria-current', 'page')
  expect(within(steps).getByText('Leave plans').closest('li')).toHaveTextContent('✓')
  await expectNoA11yViolations(container)
})

it('adds a plan for daily-paid people from a ready-made one, as the daily schedule\'s default', async () => {
  const calls = withLeave({ 'POST /api/leave-plans': () => json(201, leavePlan({ id: 'p2', name: 'Daily-paid', defaultFor: [] })) })
  renderApp('/setup/leave-plans')
  await userEvent.click(await screen.findByRole('button', { name: 'Add' }))

  expect(within(drawer()).getByRole('radio', { name: 'Leave plan' })).toBeChecked()
  await choose(drawer(), 'Start from optional', 'daily', 'Daily-paid')
  expect(within(drawer()).getByRole('textbox', { name: 'Plan name' })).toHaveValue('Daily-paid')
  const rules = within(drawer()).getByRole('list', { name: 'Leave in this plan' })
  expect(within(rules).getAllByRole('listitem')[0]).toHaveTextContent('Annual leave: 0.0805 days per day worked (= 21 days a year if they work every working day)')
  await userEvent.click(within(drawer()).getByRole('button', { name: /Default for payroll schedules/ }))
  await userEvent.click(screen.getByRole('option', { name: 'Casuals daily' }))
  await userEvent.keyboard('{Escape}')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add plan' }))

  expect(await screen.findByText('Leave plan added')).toBeInTheDocument()
  const body = calls.find(c => c.method === 'POST' && c.path === '/api/leave-plans')?.body as { rules: unknown[] }
  expect(body).toMatchObject({ name: 'Daily-paid', defaultForSchedules: [daily.id], allCompanies: false, companyIds: [restaurant.id] })
  expect(body.rules[0]).toMatchObject({ leaveTypeId: 'lt1', earnMethod: 'per_day_worked', amount: 0.0805, unit: 'days' })
  expect(body.rules[1]).toMatchObject({ leaveTypeId: 'lt2', earnMethod: 'yearly', amount: 7, halfPay: 7, waitingMonths: 2 })
})

it('edits how annual leave is earned, and stops it going below the law', async () => {
  const calls = withLeave({ [`PATCH /api/leave-plans/${standard.id}`]: () => json(200, standard) })
  renderApp('/setup/leave-plans')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Standard' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Edit Annual leave' }))

  const modal = screen.getByRole('dialog', { name: 'Annual leave' })
  await userEvent.clear(within(modal).getByRole('combobox', { name: 'How it\'s earned' }))
  await choose(modal, 'How it\'s earned', 'week', 'Each week')
  const amount = within(modal).getByRole('textbox', { name: 'Days a week' })
  await userEvent.clear(amount)
  await userEvent.type(amount, '0.3')
  expect(within(modal).getByText('= 15.6 days a year')).toBeInTheDocument()
  await userEvent.click(within(modal).getByRole('button', { name: 'Save' }))
  expect(amount).toHaveAccessibleDescription('Annual leave in Kenya is at least 21 days a year.')

  await userEvent.clear(amount)
  await userEvent.type(amount, '0.5')
  await userEvent.click(within(modal).getByRole('button', { name: 'Save' }))
  expect(within(within(drawer()).getByRole('list', { name: 'Leave in this plan' })).getAllByRole('listitem')[0]).toHaveTextContent('Annual leave: 0.5 days a week (= 26 days a year)')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Leave plan saved')).toBeInTheDocument()
  expect((calls.find(c => c.method === 'PATCH')?.body as { rules: unknown[] }).rules[0]).toMatchObject({ earnMethod: 'weekly', amount: 0.5 })
})

it('sick leave can\'t be less than 7 days on full pay and 7 on half pay', async () => {
  withLeave()
  renderApp('/setup/leave-plans')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Standard' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Edit Sick leave' }))

  const modal = screen.getByRole('dialog', { name: 'Sick leave' })
  const half = within(modal).getByRole('textbox', { name: 'Days on half pay optional' })
  await userEvent.clear(half)
  await userEvent.click(within(modal).getByRole('button', { name: 'Save' }))

  expect(within(modal).getByRole('textbox', { name: 'Days a year' })).toHaveAccessibleDescription('Sick leave in Kenya is at least 7 days on full pay and 7 on half pay a year.')
})

it('adds an own leave type from a common one, then puts it in a plan', async () => {
  const compassionate = { ...leaveTypes()[5], id: 'lt7', code: 'COM-01', name: 'Compassionate leave', paid: true, whenUsedUp: 'unpaid' }
  let types = leaveTypes()
  const calls = withLeave({
    'GET /api/leave-types': () => json(200, types),
    'POST /api/leave-types': () => { types = [...leaveTypes(), compassionate]; return json(201, compassionate) },
    [`PATCH /api/leave-plans/${standard.id}`]: () => json(200, standard),
  })
  renderApp('/setup/leave-plans')
  await userEvent.click(await screen.findByRole('tab', { name: 'Leave types' }))
  await userEvent.click(screen.getByRole('button', { name: 'Add' }))

  expect(within(drawer()).getByRole('radio', { name: 'Leave type' })).toBeChecked()
  await choose(drawer(), 'Start from a common one optional', 'comp', 'Compassionate leave')
  expect(within(drawer()).getByRole('radio', { name: 'The rest is unpaid' })).toBeChecked()
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add leave type' }))
  expect(await screen.findByText('Leave type added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/leave-types')?.body).toMatchObject({ name: 'Compassionate leave', whenUsedUp: 'unpaid', paid: true })

  await userEvent.click(screen.getByRole('tab', { name: 'Leave plans' }))
  await userEvent.click(screen.getByRole('button', { name: 'Edit Standard' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add leave to plan' }))
  const modal = screen.getByRole('dialog', { name: 'Add leave to plan' })
  await choose(modal, 'Leave type', 'comp', 'Compassionate leave')
  await userEvent.clear(within(modal).getByRole('combobox', { name: 'How it\'s earned' }))
  await choose(modal, 'How it\'s earned', 'each', 'Each time it\'s needed')
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Days each time' }), '5')
  await userEvent.click(within(modal).getByRole('button', { name: 'Save' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Leave plan saved')).toBeInTheDocument()
  expect((calls.find(c => c.method === 'PATCH')?.body as { rules: unknown[] }).rules).toContainEqual(
    expect.objectContaining({ leaveTypeId: 'lt7', earnMethod: 'each_time', amount: 5 }))
})

it('a default plan that can\'t be deleted says why', async () => {
  withLeave({
    [`DELETE /api/leave-plans/${standard.id}`]: () => apiError(409, 'ONB-3536', 'This is the default leave plan of Mama Oliech Restaurant Ltd. Make another plan their default first.'),
  })
  renderApp('/setup/leave-plans')

  await userEvent.click(await screen.findByRole('button', { name: 'Delete Standard' }))
  await userEvent.click(within(screen.getByRole('alertdialog', { name: 'Delete Standard?' })).getByRole('button', { name: 'Delete' }))

  expect(await screen.findByText(/This is the default leave plan of Mama Oliech Restaurant Ltd/)).toBeInTheDocument()
})

it('with several companies, the selector filters the plans', async () => {
  const cateringPlan = leavePlan({ id: 'p3', name: 'Catering plan', allCompanies: false, companyIds: [catering.id], defaultFor: [] })
  withLeave({ 'GET /api/companies': () => json(200, [restaurant, catering]), 'GET /api/leave-plans': () => json(200, [standard, cateringPlan]) })
  renderApp('/setup/leave-plans')
  expect(await within(await screen.findByRole('tabpanel', { name: 'Leave plans' })).findByText('Catering plan')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: /Show companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Ltd/ }))
  await userEvent.keyboard('{Escape}')

  expect(within(panel('Leave plans')).queryByText('Catering plan')).not.toBeInTheDocument()
  expect(within(panel('Leave plans')).getByText('Standard')).toBeInTheDocument()
})

it('Proceed goes to the dashboard with six steps done; employees are next', async () => {
  withLeave()
  renderApp('/setup/leave-plans')

  await userEvent.click(await within(await screen.findByRole('main')).findByRole('button', { name: 'Proceed to Org Dashboard' }))

  expect(await screen.findByText('Finish setting up · 6 of 10')).toBeInTheDocument()
  expect(screen.getAllByText('Next: Employees').length).toBeGreaterThan(0)
})
