import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { apiError, company, employeeRow, fakeApi, json, me, orgToken, schedule } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

// PAY-10's screen (OP-05): which statutory deductions weekly, fortnightly and daily schedules make.
beforeEach(() => { vi.useFakeTimers({ toFake: ['Date'] }); vi.setSystemTime(new Date('2026-10-27T09:00:00+03:00')) })
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const mombasa = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Mombasa Ltd' })
const monthly = schedule({ defaultFor: [westlands.id, mombasa.id], people: 12 })
const casuals = schedule({
  id: '0199c0de-0000-7000-8000-00000000f002', name: 'Casuals', frequency: 'weekly', payDayRule: null, cutoffDay: null, payWeekday: 5, cutoffDaysBefore: 2,
  allCompanies: false, companyIds: [westlands.id], defaultFor: [], nextPayDates: ['2026-10-30', '2026-11-06', '2026-11-13'], people: 2,
})
const deductionsPath = `/api/pay-schedules/${casuals.id}/deductions`
const none = { makesDeductions: false, items: [], appliesTo: 'everyone', employeeIds: [], startsOn: null, setBy: null, setAt: null }
const described = (current: object, history: object[] = []) => json(200, { payScheduleId: casuals.id, frequency: 'weekly', current, history })
const example = {
  frequency: 'weekly', wage: 5000, runs: 4, rulePackVersion: 'KE-2026.1',
  eachRun: [], pay: 20000, takeHome: 17950,
  deductions: [{ key: 'nssf_employee', amount: 1200 }, { key: 'shif', amount: 550 }, { key: 'ahl_employee', amount: 300 }, { key: 'paye', amount: 0 }],
  employerCosts: [{ key: 'nssf_employer', amount: 1200 }, { key: 'ahl_employer', amount: 300 }, { key: 'nita', amount: 50 }],
}
const otieno = employeeRow({ id: '0199c0de-0000-7000-8000-00000000a502', companyId: westlands.id, name: 'Otieno Ouma' })
const achieng = employeeRow({ id: '0199c0de-0000-7000-8000-00000000a503', companyId: westlands.id, name: 'Achieng Odhiambo' })

function signedIn(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    'GET /api/pay-schedules': () => json(200, [monthly, casuals]),
    'GET /api/pay-schedules/pay-dates': () => json(200, ['2026-10-30', '2026-11-06', '2026-11-13']),
    [`GET ${deductionsPath}`]: () => described(none),
    'POST /api/pay-schedules/deductions-example': () => json(200, example),
    [`PATCH /api/pay-schedules/${casuals.id}`]: () => json(200, casuals),
    'GET /api/employees': () => json(200, { items: [otieno, achieng], total: 2, page: 1, pageSize: 100 }),
    ...routes,
  })
}

const drawer = () => screen.getByRole('dialog', { name: /schedule/ })
const section = () => within(drawer()).getByRole('region', { name: 'Statutory deductions' })
const openCasuals = async () => {
  renderApp('/organization/payroll-schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Casuals' }))
  const part = await within(drawer()).findByRole('region', { name: 'Statutory deductions' })
  // Nothing changes until the choice in force is read.
  await vi.waitFor(() => expect(within(part).getByRole('switch', { name: 'Make statutory deductions' })).toBeEnabled())
  return part
}
const posted = (calls: ReturnType<typeof fakeApi>) => calls.filter(c => c.method === 'POST' && c.path === deductionsPath).map(c => c.body)

it('a weekly schedule makes none until switched on; then the five are ticked, the example shows a month, and Save sends the choice from the date', async () => {
  const calls = signedIn({ [`POST ${deductionsPath}`]: () => described({ ...none, makesDeductions: true }) })
  const part = await openCasuals()
  expect(within(part).getByRole('switch', { name: 'Make statutory deductions' })).not.toBeChecked()
  expect(within(part).queryByRole('checkbox')).not.toBeInTheDocument()

  await userEvent.click(within(part).getByRole('switch', { name: 'Make statutory deductions' }))
  expect(within(part).getAllByRole('checkbox').map(c => [c.getAttribute('aria-label') ?? c.closest('label')?.textContent, (c as HTMLInputElement).checked]))
    .toEqual([['PAYE', true], ['SHIF', true], ['NSSF', true], ['Housing Levy', true], ['NITA', true]])
  await userEvent.click(within(part).getByRole('checkbox', { name: 'NITA' }))

  // A month of a sample week's pay, worked out by the API with today's rates.
  const shown = await within(part).findByRole('group', { name: 'Example' })
  expect(shown).toHaveTextContent('4 weekly runs of KES 5,000')
  expect(shown).toHaveTextContent('NSSF KES 1,200')
  expect(shown).toHaveTextContent('Take-home KES 17,950')
  expect(shown).toHaveTextContent('You also pay NSSF KES 1,200 · Housing Levy KES 300 · NITA KES 50')
  const asked = calls.filter(c => c.path === '/api/pay-schedules/deductions-example').at(-1)!.body
  expect(asked).toMatchObject({ frequency: 'weekly', wage: 5000, items: ['paye', 'shif', 'nssf', 'housing_levy'] })

  expect(within(part).getByLabelText('From')).toHaveValue('2026-10-27')
  await userEvent.clear(within(part).getByLabelText('From'))
  await userEvent.type(within(part).getByLabelText('From'), '2026-11-02')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Schedule saved')).toBeInTheDocument()
  expect(posted(calls)).toEqual([{ makesDeductions: true, items: ['paye', 'shif', 'nssf', 'housing_levy'], appliesTo: 'everyone', employeeIds: null, startsOn: '2026-11-02' }])
  // The schedule first, then its deductions.
  expect(calls.findIndex(c => c.method === 'PATCH')).toBeLessThan(calls.findIndex(c => c.method === 'POST' && c.path === deductionsPath))
})

it('shows the choice in force and earlier ones; saving without changing them sends none', async () => {
  const calls = signedIn({
    [`GET ${deductionsPath}`]: () => described(
      { makesDeductions: true, items: ['paye', 'nssf'], appliesTo: 'everyone', employeeIds: [], startsOn: '2026-09-01', setBy: 'u1', setAt: '2026-08-28T10:00:00Z' },
      [
        { makesDeductions: true, items: ['paye', 'nssf'], appliesTo: 'everyone', employeeIds: [], startsOn: '2026-09-01', setBy: 'u1', setAt: '2026-08-28T10:00:00Z' },
        { makesDeductions: false, items: [], appliesTo: 'everyone', employeeIds: [], startsOn: '2026-01-05', setBy: 'u1', setAt: '2026-01-02T10:00:00Z' },
      ]),
  })
  const part = await openCasuals()
  expect(await within(part).findByRole('switch', { name: 'Make statutory deductions' })).toBeChecked()
  expect(within(part).getByRole('checkbox', { name: 'PAYE' })).toBeChecked()
  expect(within(part).getByRole('checkbox', { name: 'SHIF' })).not.toBeChecked()
  expect(within(within(part).getByRole('list', { name: 'Earlier choices' })).getAllByRole('listitem').map(li => li.textContent)).toEqual([
    'From 1 Sep 2026: PAYE, NSSF · everyone on the schedule · set 28 Aug 2026',
    'From 5 Jan 2026: none · set 2 Jan 2026',
  ])
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Schedule saved')).toBeInTheDocument()
  expect(posted(calls)).toEqual([])
})

it('can apply to chosen people on the schedule', async () => {
  const calls = signedIn({ [`POST ${deductionsPath}`]: () => described(none) })
  const part = await openCasuals()
  await userEvent.click(within(part).getByRole('switch', { name: 'Make statutory deductions' }))
  await userEvent.click(within(part).getByRole('radio', { name: 'Chosen employees' }))
  await userEvent.click(within(part).getByRole('button', { name: /Employees/ }))
  await userEvent.click(await screen.findByRole('option', { name: 'Otieno Ouma' }))
  await userEvent.keyboard('{Escape}')
  // Only the people on this schedule are offered.
  const asked = calls.filter(c => c.method === 'GET' && c.path === '/api/employees').map(c => new URL(c.url).searchParams).at(-1)!
  expect(asked.getAll('scheduleIds')).toEqual([casuals.id])
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Schedule saved')).toBeInTheDocument()
  expect(posted(calls)).toEqual([expect.objectContaining({ appliesTo: 'chosen', employeeIds: [otieno.id] })])
})

it('checks before sending: at least one deduction, at least one person, a date', async () => {
  const calls = signedIn()
  const part = await openCasuals()
  await userEvent.click(within(part).getByRole('switch', { name: 'Make statutory deductions' }))
  for (const name of ['PAYE', 'SHIF', 'NSSF', 'Housing Levy', 'NITA']) await userEvent.click(within(part).getByRole('checkbox', { name }))
  await userEvent.click(within(part).getByRole('radio', { name: 'Chosen employees' }))
  await userEvent.clear(within(part).getByLabelText('From'))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(within(part).getByText('Choose at least one deduction.')).toBeInTheDocument()
  expect(within(part).getByText('Choose at least one employee.')).toBeInTheDocument()
  expect(within(part).getByText('Choose the date it applies from.')).toBeInTheDocument()
  expect(calls.some(c => c.method === 'PATCH')).toBe(false)
  expect(posted(calls)).toEqual([])
})

it('a refusal of the deductions shows its code; the schedule is saved, so trying again changes it rather than adding another', async () => {
  let refuse = true
  const calls = signedIn({
    'POST /api/pay-schedules': () => json(201, casuals),
    [`POST ${deductionsPath}`]: () => (refuse ? apiError(403, 'ACC-8001', "You don't have access to do this.") : described(none)),
  })
  renderApp('/organization/payroll-schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Add schedule' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Schedule name' }), 'Casuals')
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'How often' }))
  await userEvent.type(within(drawer()).getByRole('combobox', { name: 'How often' }), 'week')
  await userEvent.click(within(drawer()).getByRole('option', { name: 'Weekly' }))
  await userEvent.click(within(section()).getByRole('switch', { name: 'Make statutory deductions' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add schedule' }))

  expect(await within(drawer()).findByText(/Code ACC-8001/)).toBeInTheDocument()
  refuse = false
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Schedule saved')).toBeInTheDocument()
  expect(calls.filter(c => c.method === 'POST' && c.path === '/api/pay-schedules')).toHaveLength(1)
  expect(calls.filter(c => c.method === 'PATCH')).toHaveLength(1)
  expect(posted(calls)).toHaveLength(2)
})

it('monthly schedules make every deduction: no section, nothing asked', async () => {
  const calls = signedIn()
  const { container } = renderApp('/organization/payroll-schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Monthly' }))
  expect(within(drawer()).queryByRole('region', { name: 'Statutory deductions' })).not.toBeInTheDocument()
  expect(calls.some(c => c.path.endsWith('/deductions'))).toBe(false)
  await expectNoA11yViolations(container)
})

it('only someone who manages schedules for every company can change them; others see the choice', async () => {
  const forWestlands = (permission: string) => ({ permission, allCompanies: false, companyIds: [westlands.id] })
  signedIn({ 'GET /api/auth/me': () => json(200, { ...me, permissions: [forWestlands('schedules.view'), forWestlands('schedules.manage'), forWestlands('employees.view')] }) })
  renderApp('/organization/payroll-schedules')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Casuals' }))
  const part = await within(drawer()).findByRole('region', { name: 'Statutory deductions' })
  expect(await within(part).findByText('Only someone who manages payroll schedules for every company can change these.')).toBeInTheDocument()
  expect(within(part).getByRole('switch', { name: 'Make statutory deductions' })).toBeDisabled()
})
