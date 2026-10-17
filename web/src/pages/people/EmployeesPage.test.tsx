import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { company, employeeDetail, employeeRow, fakeApi, json, leavePlan, leaveTypes, me, orgToken, schedule } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

afterEach(() => { vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const mombasa = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Mombasa Ltd' })
const grant = (permission: string) => ({ permission, allCompanies: true, companyIds: [] as string[] })
const hr = ['employees.view', 'employees.manage', 'leave.view', 'schedules.view', 'structure.view'].map(grant)
const payroll = ['employees.view', 'pay.view', 'pay.manage', 'leave.view', 'schedules.view', 'structure.view'].map(grant)
const viewer = ['employees.view'].map(grant)

const wanjiru = employeeRow({ companyId: westlands.id })
const hidden = { ...wanjiru, basicAmount: null, payBasis: null, currency: null, payHidden: true }
const otieno = employeeRow({
  id: '0199c0de-0000-7000-8000-00000000a502', companyId: westlands.id, staffNo: 'EMP-02', name: 'Otieno Ouma', basicAmount: null, payBasis: null,
  payHidden: true, hireDate: '2026-09-01', exitDate: '2099-12-31',
  readiness: { ready: false, missing: [{ kind: 'employee', key: 'kra_pin', label: { en: 'KRA PIN', sw: 'PIN ya KRA' }, code: 'PAY-2001' }] },
})

const page = (items: unknown[]) => json(200, { items, total: items.length, page: 1, pageSize: 20 })

/** Signed in with these permissions (none: everything), two companies, and the employees list answering with `list`. */
function signedIn(permissions?: ReturnType<typeof grant>[], routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands, mombasa]),
    'GET /api/pay-schedules': () => json(200, [schedule({ allCompanies: true })]),
    'GET /api/leave-plans': () => json(200, [leavePlan()]),
    'GET /api/leave-types': () => json(200, leaveTypes()),
    'GET /api/employees': () => page([hidden, otieno]),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions }) } : {}),
    ...routes,
  })
}

const table = () => screen.getByRole('region', { name: 'Employees' })
const listCalls = (calls: ReturnType<typeof fakeApi>) =>
  calls.filter(c => c.method === 'GET' && c.path === '/api/employees').map(c => new URL(c.url).searchParams)

it('opens in the shell for the top bar\'s company: name and staff number, job, schedule, pay, ready for payroll', async () => {
  const calls = signedIn(hr)
  const { container } = renderApp('/people/employees')

  expect(await screen.findByRole('heading', { name: 'Employees', level: 1 })).toBeInTheDocument()
  expect(await within(screen.getByRole('navigation', { name: 'Main' })).findByRole('link', { name: 'Employees' })).toHaveAttribute('aria-current', 'page')
  const row = (await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')).closest('tr')!
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Employee', 'Job', 'Payroll schedule', 'Basic pay', 'Ready for payroll', 'Actions'])
  expect(within(row).getAllByRole('cell').map(c => c.textContent).slice(0, 5))
    .toEqual(['Wanjiru KamauEMP-01Joined 15 Jan 2024', 'Head cook · Kitchen', 'Monthly', 'Hidden', 'Ready'])
  // Only the top bar's company is asked for.
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('companyId')).toBe(westlands.id))
  expect(listCalls(calls).every(q => !q.has('companyIds'))).toBe(true)
  await expectNoA11yViolations(container)
})

it('switching the company in the top bar changes the table', async () => {
  const calls = signedIn(hr, {
    'GET /api/employees': req => (new URL(req.url).searchParams.get('companyId') === mombasa.id ? page([]) : page([hidden])),
  })
  renderApp('/people/employees')
  await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')

  await userEvent.click(within(screen.getByRole('banner')).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa Ltd' }))
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('companyId')).toBe(mombasa.id))
  expect(await screen.findByText('No employees here yet')).toBeInTheDocument()
})

it('a missing KRA PIN shows PAY-2001; joiners and leavers show when they joined or leave', async () => {
  signedIn(hr)
  renderApp('/people/employees')
  const row = (await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Otieno Ouma')).closest('tr')!
  const ready = within(row).getAllByRole('cell')[4]
  expect(ready).toHaveTextContent('No KRA PIN')
  expect(ready).toHaveTextContent('PAY-2001')
  expect(row).toHaveTextContent('Leaving on 31 Dec 2099')
})

it('HR sees pay as Hidden and can add and upload; no Show amounts', async () => {
  signedIn(hr)
  renderApp('/people/employees')
  const row = (await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')).closest('tr')!
  expect(row).toHaveTextContent('Hidden')
  expect(screen.getByRole('button', { name: 'Add employee' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Upload employees' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Show amounts' })).not.toBeInTheDocument()
})

it('Add employee starts with only the top bar\'s company', async () => {
  signedIn(hr)
  renderApp('/people/employees')
  await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')
  await userEvent.click(within(screen.getByRole('banner')).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa Ltd' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))
  const sheet = await screen.findByRole('dialog', { name: /Add 1 employee/ })
  expect(await within(sheet).findByRole('combobox', { name: 'Company' })).toHaveValue('Mombasa Ltd')
})

it('Payroll sees amounts only after Show amounts, which asks the API for them', async () => {
  const calls = signedIn(payroll, {
    'GET /api/employees': req => page([new URL(req.url).searchParams.get('includePay') === 'true' ? wanjiru : { ...hidden }]),
  })
  renderApp('/people/employees')
  const row = (await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')).closest('tr')!
  expect(row).toHaveTextContent('KES ••••••')
  expect(listCalls(calls).some(q => q.has('includePay'))).toBe(false)

  await userEvent.click(screen.getByRole('button', { name: 'Show amounts' }))
  expect(await within(table()).findByText('KES 85,000 a month')).toBeInTheDocument()
  expect(listCalls(calls).at(-1)!.get('includePay')).toBe('true')
  expect(screen.queryByRole('button', { name: 'Add employee' })).not.toBeInTheDocument()   // Payroll doesn't manage people
})

it('pay and managing people follow the top bar\'s company: Payroll and HR for Mombasa only get neither at Westlands', async () => {
  const forMombasa = (permission: string) => ({ permission, allCompanies: false, companyIds: [mombasa.id] })
  signedIn([grant('employees.view'), ...['employees.manage', 'pay.view', 'pay.manage'].map(forMombasa)])
  renderApp('/people/employees')
  const row = (await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')).closest('tr')!
  expect(row).toHaveTextContent('Hidden')
  expect(screen.queryByRole('button', { name: 'Show amounts' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Add employee' })).not.toBeInTheDocument()

  await userEvent.click(within(screen.getByRole('banner')).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa Ltd' }))
  expect(await screen.findByRole('button', { name: 'Show amounts' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add employee' })).toBeInTheDocument()
})

it('someone who may only view employees has no Add, Upload, Edit or Delete', async () => {
  signedIn(viewer)
  renderApp('/people/employees')
  const row = (await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')).closest('tr')!
  expect(within(row).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual(['View Wanjiru Kamau'])
  expect(screen.queryByRole('button', { name: 'Add employee' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Upload employees' })).not.toBeInTheDocument()
})

it('filters by status and by what payroll still needs; the address can carry them (?status=…&missing=…)', async () => {
  const calls = signedIn(hr)
  renderApp('/people/employees?missing=kra_pin')
  await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Otieno Ouma')
  // A link from the HR home lands filtered.
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('missing')).toBe('kra_pin'))
  expect(screen.getByRole('combobox', { name: 'Ready for payroll' })).toHaveValue('No KRA PIN')

  await userEvent.click(screen.getByRole('combobox', { name: 'Status' }))
  await userEvent.click(screen.getByRole('option', { name: 'Leaving' }))
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('status')).toBe('leaving'))
  expect(listCalls(calls).at(-1)!.get('missing')).toBe('kra_pin')

  await userEvent.click(screen.getByRole('combobox', { name: 'Ready for payroll' }))
  await userEvent.click(screen.getByRole('option', { name: 'Not ready' }))
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('missing')).toBe('any'))

  await userEvent.click(screen.getByRole('combobox', { name: 'Status' }))
  await userEvent.click(screen.getByRole('option', { name: 'Left' }))
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.get('status')).toBe('former'))
})

it('filters by department and payroll schedule from their columns', async () => {
  const calls = signedIn(hr, {
    'GET /api/structure/departments': () => json(200, [{ id: 'dp1', code: 'KIT-01', name: 'Kitchen', divisionId: null, parentId: null, allCompanies: true, companyIds: [] }]),
  })
  renderApp('/people/employees')
  await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')
  await userEvent.click(within(table()).getByRole('button', { name: 'Filter Job' }))
  await userEvent.click(within(screen.getByRole('dialog', { name: 'Filter Job' })).getByRole('checkbox', { name: 'Kitchen' }))
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.getAll('departmentIds')).toEqual(['dp1']))
  await userEvent.keyboard('{Escape}')
  await userEvent.click(within(table()).getByRole('button', { name: 'Filter Payroll schedule' }))
  await userEvent.click(within(screen.getByRole('dialog', { name: 'Filter Payroll schedule' })).getByRole('checkbox', { name: 'Monthly' }))
  await vi.waitFor(() => expect(listCalls(calls).at(-1)!.getAll('scheduleIds')).toEqual([schedule().id]))
})

it('the profile shows a missing KRA PIN with its code', async () => {
  signedIn(hr, {
    [`GET /api/employees/${otieno.id}`]: () => json(200, employeeDetail({
      id: otieno.id, companyId: westlands.id, firstName: 'Otieno', lastName: 'Ouma', kraPinMasked: null, pay: null, account: null, payHidden: true,
      readiness: otieno.readiness,
    })),
  })
  renderApp('/people/employees')
  await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Otieno Ouma')
  await userEvent.click(within(table()).getByRole('button', { name: 'View Otieno Ouma' }))
  const view = await screen.findByRole('dialog', { name: 'Otieno Ouma' })
  await userEvent.click(within(view).getByRole('tab', { name: 'IDs and numbers' }))
  const pin = within(view).getByText('KRA PIN', { selector: 'dt' }).parentElement!
  expect(pin).toHaveTextContent('Missing')
  expect(pin).toHaveTextContent('PAY-2001')
})

const openProfile = async () => {
  await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')
  await userEvent.click(within(table()).getByRole('button', { name: 'View Wanjiru Kamau' }))
  return screen.findByRole('dialog', { name: 'Wanjiru Kamau' })
}
const profile = { [`GET /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail({ companyId: westlands.id, pay: null, account: null, payHidden: true })) }

it('the profile\'s Leave tab shows their leave plan and what it gives; balances wait for leave tracking', async () => {
  signedIn(hr, profile)
  renderApp('/people/employees')
  const view = await openProfile()
  await userEvent.click(within(view).getByRole('tab', { name: 'Leave' }))
  const leave = within(view).getByRole('tabpanel', { name: 'Leave' })
  expect(await within(leave).findByText('Standard')).toBeInTheDocument()
  const gives = within(leave).getByRole('list', { name: 'What Standard gives' })
  expect(within(gives).getAllByRole('listitem').map(li => li.textContent).slice(0, 2))
    .toEqual(['Annual leave: 1.75 days a month', 'Sick leave: 7 days a year + 7 on half pay'])
  expect(within(leave).queryByRole('region', { name: 'Balances and requests' })).not.toBeInTheDocument()
})

it('with the leaveBalances flag on, the Leave tab has a place for balances and requests', async () => {
  vi.stubEnv('VITE_FEATURES', 'leaveBalances')
  signedIn(hr, profile)
  renderApp('/people/employees')
  const view = await openProfile()
  await userEvent.click(within(view).getByRole('tab', { name: 'Leave' }))
  expect(await within(view).findByRole('region', { name: 'Balances and requests' })).toBeInTheDocument()
  vi.unstubAllEnvs()
})

it('without View on leave, the profile has no Leave tab', async () => {
  signedIn(viewer, profile)
  renderApp('/people/employees')
  const view = await openProfile()
  expect(within(view).getByRole('tab', { name: 'Job' })).toBeInTheDocument()
  expect(within(view).queryByRole('tab', { name: 'Leave' })).not.toBeInTheDocument()
})
