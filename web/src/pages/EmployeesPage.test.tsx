import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { company, employeeDetail, employeeRow, fakeApi, json, leavePlan, location, orgToken, schedule } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())
beforeEach(() => { URL.createObjectURL = vi.fn(() => 'blob:template'); URL.revokeObjectURL = vi.fn() })

const signedIn = { 'POST /api/auth/refresh': () => orgToken() }
const restaurant = company()
const kitchen = { id: 'dp1', code: 'KIT-01', name: 'Kitchen', divisionId: null, parentId: null, allCompanies: true, companyIds: [] }
const daily = schedule({ id: '0199c0de-0000-7000-8000-00000000f009', name: 'Casuals daily', frequency: 'daily', payDayRule: null, cutoffDay: null, payTime: '17:00:00', defaultFor: [] })
const wanjiru = employeeRow()
const otieno = employeeRow({
  id: '0199c0de-0000-7000-8000-00000000a502', staffNo: 'EMP-02', name: 'Otieno Ouma', positionTitle: null, departmentName: null, basicAmount: null,
  payBasis: null, readiness: { ready: false, missing: [{ kind: 'employee', key: 'kra_pin', label: { en: 'KRA PIN', sw: 'PIN ya KRA' } }, { kind: 'employee', key: 'basic_pay', label: { en: 'Basic pay', sw: 'Mshahara' } }] },
})

const drawer = () => screen.getByRole('dialog', { name: /employee/i })
const table = () => screen.getByRole('region', { name: 'Employees' })

async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(drawer()).getByRole('combobox', { name }), search)
  await userEvent.click(within(drawer()).getByRole('option', { name: option }))
}

function withPeople(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [restaurant]),
    'GET /api/locations': () => json(200, [location()]),
    'GET /api/structure/departments': () => json(200, [kitchen]),
    'GET /api/pay-schedules': () => json(200, [schedule(), daily]),
    'GET /api/leave-plans': () => json(200, [leavePlan()]),
    'GET /api/employees': () => json(200, { items: [wanjiru, otieno], total: 2, page: 1, pageSize: 100 }),
    ...routes,
  })
}

it('lists employees with their job, pay and what payroll still needs', async () => {
  withPeople()
  const { container } = renderApp('/setup/employees')

  expect(await screen.findByRole('heading', { name: 'Who works for you?' })).toBeInTheDocument()
  const row = (await within(table()).findByText('Wanjiru Kamau')).closest('tr')!
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Employee', 'Job', 'Payroll schedule', 'Basic pay', 'Ready for payroll', 'Actions'])
  expect(within(row).getAllByRole('cell').map(c => c.textContent).slice(0, 5))
    .toEqual(['Wanjiru KamauEMP-01', 'Head cook · Kitchen', 'Monthly', 'KES 85,000 a month', 'Ready'])
  const other = within(table()).getByText('Otieno Ouma').closest('tr')!
  expect(within(other).getAllByRole('cell')[4]).toHaveTextContent('Needs KRA PIN, Basic pay')

  await userEvent.type(screen.getByRole('searchbox', { name: 'Search employees' }), 'wanj')
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Employees').closest('a')).toHaveAttribute('aria-current', 'page')
  await expectNoA11yViolations(container)
})

it('searches and pages on the server', async () => {
  const seen: string[] = []
  withPeople({
    'GET /api/employees': req => {
      seen.push(new URL(req.url).search)
      return json(200, { items: [wanjiru], total: 230, page: Number(new URL(req.url).searchParams.get('page') ?? 1), pageSize: 100 })
    },
  })
  renderApp('/setup/employees')
  expect(await screen.findByText('Page 1 of 3')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Next page' }))
  expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument()
  await userEvent.type(screen.getByRole('searchbox', { name: 'Search employees' }), 'kamau')

  await vi.waitFor(() => expect(seen.some(q => q.includes('search=kamau') && q.includes('page=1'))).toBe(true))
  await userEvent.clear(within(screen.getByRole('main')).getByRole('combobox', { name: 'Show' }))
  await userEvent.type(within(screen.getByRole('main')).getByRole('combobox', { name: 'Show' }), 'left')
  await userEvent.click(screen.getByRole('option', { name: 'People who left' }))
  await vi.waitFor(() => expect(seen.some(q => q.includes('status=former'))).toBe(true))
})

it('adds an employee across the tabs, with the company\'s defaults filled in', async () => {
  const calls = withPeople({ 'POST /api/employees': () => json(201, employeeDetail()) })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))

  expect(within(drawer()).getAllByRole('tab').map(t => t.textContent)).toEqual(['Personal', 'IDs and numbers', 'Job', 'Pay', 'Paid into'])
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'First name' }), 'Wanjiru')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Last name' }), 'Kamau')
  await userEvent.type(within(drawer()).getByLabelText('Start date'), '2024-01-15')

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'IDs and numbers' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'KRA PIN optional' }), 'a012345678z')

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Job' }))
  expect(within(drawer()).getByRole('combobox', { name: 'Payroll schedule' })).toHaveValue('Monthly')
  expect(within(drawer()).getByRole('combobox', { name: 'Leave plan' })).toHaveValue('Standard')
  await choose('Department optional', 'kit', 'Kitchen')

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Pay' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Monthly salary (KES) optional' }), '85000')

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Paid into' }))
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'M-Pesa' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'M-Pesa number' }), '0712 345 678')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add employee' }))

  expect(await screen.findByText('Employee added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/employees')?.body).toMatchObject({
    companyId: restaurant.id, firstName: 'Wanjiru', lastName: 'Kamau', hireDate: '2024-01-15', kraPin: 'a012345678z',
    job: { departmentId: 'dp1', payScheduleId: schedule().id, employmentType: 'permanent' },
    pay: { basicAmount: 85000, payBasis: 'monthly' },
    account: { method: 'mpesa', mpesaPhone: '0712 345 678' },
  })
})

it('a problem the server finds shows on its tab', async () => {
  withPeople({
    'POST /api/employees': () => json(400, { code: 'ONB-3538', message: 'Some employee details don\'t look right.', reference: '7F3K92QD',
      fields: { kraPin: 'Enter the person\'s KRA PIN: A, 9 digits and a letter, like A012345678Z.' } }),
  })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'First name' }), 'Wanjiru')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Last name' }), 'Kamau')
  await userEvent.type(within(drawer()).getByLabelText('Start date'), '2024-01-15')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add employee' }))

  const tab = await within(drawer()).findByRole('tab', { name: /IDs and numbers/ })
  expect(tab).toHaveTextContent(/needs fixing/)
  await userEvent.click(tab)
  expect(within(drawer()).getByRole('textbox', { name: 'KRA PIN optional' })).toHaveAccessibleDescription(/like A012345678Z/)
})

it('editing shows saved numbers masked and leaves them as they are unless retyped', async () => {
  const calls = withPeople({
    [`GET /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail()),
    [`PATCH /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail()),
  })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Wanjiru Kamau' }))
  await within(drawer()).findByDisplayValue('Wanjiru')

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'IDs and numbers' }))
  expect(within(drawer()).getByRole('textbox', { name: 'KRA PIN optional' })).toHaveAccessibleDescription(/Saved: •••• 678Z/)
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Pay' }))
  const salary = within(drawer()).getByRole('textbox', { name: 'Monthly salary (KES) optional' })
  await userEvent.clear(salary)
  await userEvent.type(salary, '95000')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Employee saved')).toBeInTheDocument()
  const body = calls.find(c => c.method === 'PATCH')?.body as Record<string, unknown>
  expect(body).toMatchObject({ kraPin: null, nationalId: null, pay: { basicAmount: 95000 } })
  expect(body.account).toBeNull()   // kept as it is
})

it('daily pay asks for a daily rate, and a rate that doesn\'t match the schedule is flagged', async () => {
  withPeople()
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Pay' }))

  await userEvent.click(within(drawer()).getByRole('radio', { name: 'Hourly' }))
  expect(within(drawer()).getByRole('textbox', { name: 'Hourly rate (KES) optional' })).toBeInTheDocument()
  expect(within(drawer()).getByText(/Monthly pays monthly/)).toBeInTheDocument()

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Job' }))
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'Payroll schedule' }))
  await choose('Payroll schedule', 'cas', 'Casuals daily')
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Pay' }))
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'Daily' }))
  expect(within(drawer()).getByRole('textbox', { name: 'Daily rate (KES) optional' })).toBeInTheDocument()
  expect(within(drawer()).queryByText(/pays monthly/)).not.toBeInTheDocument()
})

it('Add several opens a sheet of empty rows; Upload starts with the template', async () => {
  withPeople()
  renderApp('/setup/employees')
  await screen.findByRole('region', { name: 'Employees' })
  expect(screen.getAllByRole('button', { name: /^(Add employee|Add several|Upload employees)$/ }).map(b => b.textContent))
    .toEqual(['Add employee', 'Add several', 'Upload employees'])   // Add first, each with its icon

  await userEvent.click(screen.getByRole('button', { name: 'Add several' }))
  const sheet = screen.getByRole('dialog', { name: /Add \d+ employees/ })
  expect(within(sheet).getAllByRole('textbox', { name: /First name, row/ })).toHaveLength(10)
  await userEvent.click(within(sheet).getByRole('button', { name: 'Cancel' }))

  await userEvent.click(screen.getByRole('button', { name: 'Upload employees' }))
  expect(within(screen.getByRole('dialog', { name: 'Upload employees' })).getByRole('button', { name: 'Download template' })).toBeInTheDocument()
})

it('Proceed goes to the dashboard with seven steps done', async () => {
  withPeople()
  renderApp('/setup/employees')

  await userEvent.click(await within(await screen.findByRole('main')).findByRole('button', { name: 'Proceed to Org Dashboard' }))

  expect(await screen.findByText('Finish setting up · 7 of 10')).toBeInTheDocument()
})
