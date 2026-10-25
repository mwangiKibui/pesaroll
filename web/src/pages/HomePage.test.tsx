import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { company, fakeApi, json, me, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => { vi.unstubAllGlobals(); vi.unstubAllEnvs(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const mombasa = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Mombasa Ltd' })
const grant = (permission: string, companyIds: string[] = []) => ({ permission, allCompanies: companyIds.length === 0, companyIds })
const both = (area: string) => [grant(`${area}.view`), grant(`${area}.manage`)]
const hr = [...both('employees'), ...both('locations'), ...both('structure'), ...both('leave'), grant('schedules.view')]
const mary = { ...me, firstName: 'Mary', lastName: 'Wambui', fullName: 'Mary Wambui', permissions: hr }

const label = (en: string) => ({ en, sw: en })
const people = (overrides: Record<string, unknown> = {}) => ({
  companyId: westlands.id, employees: 40, joinedThisMonth: 2,
  readiness: {
    nextPayDate: '2026-10-30', ready: 37, total: 40,
    missing: [
      { key: 'kra_pin', label: label('KRA PIN'), code: 'PAY-2001', people: 3 },
      { key: 'basic_pay', label: label('Basic pay'), code: null, people: 1 },
    ],
  },
  ...overrides,
})

function signedIn(permissions = hr, home: (companyId: string) => unknown = () => people(), person = { ...mary, permissions }) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/auth/me': () => json(200, person),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    'GET /api/home/people': req => json(200, home(new URL(req.url).searchParams.get('companyId')!)),
  })
}

const today = () => new Intl.DateTimeFormat('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'Africa/Nairobi' }).format(new Date())
const card = (name: string) => screen.findByRole('region', { name })

it('HR sees the people numbers and Ready for payroll for the chosen company, no greeting or date, and no amounts', async () => {
  const calls = signedIn()
  const { container } = renderApp('/')

  // The page is titled Home for screen readers only; no greeting or date on screen.
  expect(await screen.findByRole('heading', { name: 'Home', level: 1 })).toHaveClass('sr-only')
  const main = screen.getByRole('main')
  expect(within(main).queryByText(/Habari/)).not.toBeInTheDocument()
  expect(within(main).queryByText(today())).not.toBeInTheDocument()

  // Two number cards, no names or start dates.
  expect(within(await card('Employees')).getByRole('link', { name: /^40/ })).toBeInTheDocument()
  expect(within(await card('Joined this month')).getByRole('link', { name: /^2/ })).toBeInTheDocument()
  expect(within(main).queryByText(/^Started /)).not.toBeInTheDocument()

  const ready = await card('Ready for payroll')
  expect(within(ready).getByText('37 of 40 ready for the 30 Oct pay run')).toBeInTheDocument()
  expect(within(ready).getByRole('progressbar', { name: 'Ready for payroll' })).toHaveAttribute('aria-valuenow', '37')
  expect(within(ready).getByText('3 without a KRA PIN')).toBeInTheDocument()
  expect(within(ready).getByText('PAY-2001')).toBeInTheDocument()
  expect(within(ready).getByText('1 without basic pay')).toBeInTheDocument()

  // No pay anywhere (ACC-05): no currency, no amounts.
  expect(main.textContent).not.toMatch(/KES|Ksh|\d{1,3},\d{3}/)
  expect(calls.find(c => c.path === '/api/home/people')?.url).toContain(`companyId=${westlands.id}`)
  await expectNoA11yViolations(container)
})

it('every number follows the company chosen in the top bar', async () => {
  const calls = signedIn(hr, id => id === mombasa.id
    ? people({ companyId: mombasa.id, employees: 8, joinedThisMonth: 0, readiness: { nextPayDate: '2026-10-30', ready: 8, total: 8, missing: [] } })
    : people())
  renderApp('/')
  expect(await within(await card('Ready for payroll')).findByText('37 of 40 ready for the 30 Oct pay run')).toBeInTheDocument()

  await userEvent.click(within(screen.getByRole('banner')).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa Ltd' }))

  const ready = await card('Ready for payroll')
  expect(await within(ready).findByText('8 of 8 ready for the 30 Oct pay run')).toBeInTheDocument()
  expect(within(await card('Employees')).getByRole('link', { name: /^8/ })).toBeInTheDocument()
  expect(calls.some(c => c.path === '/api/home/people' && c.url.includes(`companyId=${mombasa.id}`))).toBe(true)
})

it('when everyone can be paid, Ready for payroll says so', async () => {
  signedIn(hr, () => people({ readiness: { nextPayDate: '2026-10-30', ready: 40, total: 40, missing: [] } }))
  renderApp('/')
  const ready = await card('Ready for payroll')
  expect(await within(ready).findByText('Everyone can be paid')).toBeInTheDocument()
  expect(within(ready).queryByRole('link', { name: /Fix them/ })).not.toBeInTheDocument()
})

it('without a payroll schedule it speaks of the next pay run, and nobody joined shows 0', async () => {
  signedIn(hr, () => people({ joinedThisMonth: 0, readiness: { nextPayDate: null, ready: 37, total: 40, missing: [] } }))
  renderApp('/')
  expect(await within(await card('Ready for payroll')).findByText('37 of 40 ready for the next pay run')).toBeInTheDocument()
  expect(within(await card('Joined this month')).getByRole('link', { name: /^0/ })).toBeInTheDocument()
})

it('Fix them opens People → Employees filtered to the people missing each detail; the numbers link there too', async () => {
  signedIn()
  renderApp('/')
  const ready = await card('Ready for payroll')
  expect(await within(ready).findByRole('link', { name: 'Fix them: 3 without a KRA PIN' })).toHaveAttribute('href', '/people/employees?missing=kra_pin')
  expect(within(ready).getByRole('link', { name: 'Fix them: 1 without basic pay' })).toHaveAttribute('href', '/people/employees?missing=basic_pay')
  expect(within(await card('Employees')).getByRole('link', { name: /^40/ })).toHaveAttribute('href', '/people/employees')
  expect(within(await card('Joined this month')).getByRole('link', { name: /^2/ }).getAttribute('href')).toMatch(/^\/people\/employees\?hiredFrom=\d{4}-\d{2}-01$/)
})

it('people who may add employees get Add employee, which opens the form for the chosen company', async () => {
  signedIn()
  renderApp('/')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))
  expect(await screen.findByRole('dialog', { name: 'Add employee' })).toBeInTheDocument()
})

it('someone who may only view employees has no Add employee', async () => {
  signedIn([grant('employees.view')])
  renderApp('/')
  await card('Ready for payroll')
  expect(screen.queryByRole('button', { name: 'Add employee' })).not.toBeInTheDocument()
})

it('an Approver (no employees.view) sees none of these cards', async () => {
  const calls = signedIn([])
  renderApp('/')
  expect(await screen.findByRole('heading', { name: 'Home', level: 1 })).toBeInTheDocument()
  expect(screen.queryByRole('region', { name: 'Employees' })).not.toBeInTheDocument()
  expect(screen.queryByRole('region', { name: 'Joined this month' })).not.toBeInTheDocument()
  expect(screen.queryByRole('region', { name: 'Ready for payroll' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Add employee' })).not.toBeInTheDocument()
  expect(calls.some(c => c.path === '/api/home/people')).toBe(false)
})

it('HR for one company sees the cards only while that company is chosen', async () => {
  const calls = signedIn([grant('employees.view', [mombasa.id])])
  renderApp('/')
  await screen.findByRole('heading', { name: 'Home', level: 1 })
  expect(screen.queryByRole('region', { name: 'Ready for payroll' })).not.toBeInTheDocument()

  await userEvent.click(within(screen.getByRole('banner')).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa Ltd' }))
  expect(await card('Ready for payroll')).toBeInTheDocument()
  expect(calls.filter(c => c.path === '/api/home/people').every(c => c.url.includes(`companyId=${mombasa.id}`))).toBe(true)
})
