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

it('Add employee opens a sheet with one row: company and structure are lists, and Date left waits for Still works here', async () => {
  let rows: unknown[] = []
  withPeople({
    'GET /api/structure/positions': () => json(200, [{ id: 'ps1', code: 'COO-01', title: 'Head cook', departmentId: 'dp1', allCompanies: true, companyIds: [] }]),
    'POST /api/employees/import/check': async req => { rows = ((await req.json()) as { rows: unknown[] }).rows; return json(200, { problems: [], notes: [] }) },
    'POST /api/employees/import': () => json(200, { imported: 2 }),
  })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))

  const sheet = screen.getByRole('dialog', { name: /Add 1 employee/ })
  expect(within(sheet).getAllByRole('textbox', { name: /First name, row/ })).toHaveLength(1)
  expect(within(sheet).getByRole('combobox', { name: 'Company, row 1' })).toHaveValue('Mama Oliech Restaurant Ltd')
  for (const column of ['Division', 'Department', 'Unit', 'Position']) {
    expect(within(sheet).getByRole('combobox', { name: `${column}, row 1` })).toBeInTheDocument()
  }
  expect(within(sheet).getByRole('checkbox', { name: 'Still works here, row 1' })).toBeChecked()
  expect(within(sheet).getByLabelText('Date left, row 1')).toBeDisabled()

  await userEvent.type(within(sheet).getByRole('textbox', { name: 'First name, row 1' }), 'Wanjiru')
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'Last name, row 1' }), 'Kamau')
  await userEvent.type(within(sheet).getByLabelText('Start date, row 1'), '2024-01-15')
  await userEvent.type(within(sheet).getByRole('combobox', { name: 'Position, row 1' }), 'head')
  await userEvent.click(screen.getByRole('option', { name: 'Head cook (COO-01)' }))

  await userEvent.click(within(sheet).getByRole('button', { name: 'Add row' }))
  const second = screen.getByRole('dialog', { name: /Add 2 employees/ })
  await userEvent.type(within(second).getByRole('textbox', { name: 'First name, row 2' }), 'Akinyi')
  await userEvent.type(within(second).getByRole('textbox', { name: 'Last name, row 2' }), 'Odera')
  await userEvent.type(within(second).getByLabelText('Start date, row 2'), '2023-01-15')
  await userEvent.click(within(second).getByRole('checkbox', { name: 'Still works here, row 2' }))
  await userEvent.type(within(second).getByLabelText('Date left, row 2'), '2024-06-30')
  await userEvent.click(within(second).getByRole('button', { name: 'Add 2 employees' }))

  expect(await screen.findByText('2 employees added')).toBeInTheDocument()
  expect(rows).toMatchObject([
    { line: 1, cells: { company: 'Mama Oliech Restaurant Ltd', firstName: 'Wanjiru', hireDate: '2024-01-15', position: 'COO-01', stillHere: 'Yes', exitDate: '' } },
    { line: 2, cells: { firstName: 'Akinyi', stillHere: '', exitDate: '2024-06-30' } },
  ])
})

it('someone who no longer works here needs the date they left', async () => {
  const calls = withPeople({ 'POST /api/employees/import/check': () => json(200, { problems: [], notes: [] }) })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))
  const sheet = screen.getByRole('dialog', { name: /Add 1 employee/ })
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'First name, row 1' }), 'Akinyi')
  await userEvent.click(within(sheet).getByRole('checkbox', { name: 'Still works here, row 1' }))
  await userEvent.click(within(sheet).getByRole('button', { name: 'Add 1 employee' }))

  expect(await within(sheet).findByText('Enter the date they left, or tick Still works here.')).toBeInTheDocument()
  expect(calls.some(c => c.path === '/api/employees/import')).toBe(false)
})

it('a problem the server finds shows on its tab', async () => {
  withPeople({
    [`GET /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail()),
    [`PATCH /api/employees/${wanjiru.id}`]: () => json(400, { code: 'ONB-3538', message: 'Some employee details don\'t look right.', reference: '7F3K92QD',
      fields: { kraPin: 'Enter the person\'s KRA PIN: A, 9 digits and a letter, like A012345678Z.' } }),
  })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Wanjiru Kamau' }))
  await within(drawer()).findByDisplayValue('Wanjiru')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

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

  const stillHere = within(drawer()).getByRole('checkbox', { name: 'Still works here' })
  expect(stillHere).toBeChecked()
  expect(within(drawer()).getByLabelText('Date left')).toBeDisabled()
  await userEvent.click(stillHere)
  expect(within(drawer()).getByLabelText('Date left')).toBeEnabled()
  await userEvent.click(stillHere)

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
  withPeople({ [`GET /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail()) })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Wanjiru Kamau' }))
  await within(drawer()).findByDisplayValue('Wanjiru')
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

it('Add employee and Upload employees sit together, Add first; Upload starts with the template', async () => {
  withPeople()
  renderApp('/setup/employees')
  await screen.findByRole('region', { name: 'Employees' })
  expect(screen.getAllByRole('button', { name: /^(Add employee|Add several|Upload employees)$/ }).map(b => b.textContent))
    .toEqual(['Add employee', 'Upload employees'])   // no separate Add several: Add row is in the sheet

  await userEvent.click(screen.getByRole('button', { name: 'Upload employees' }))
  expect(within(screen.getByRole('dialog', { name: 'Upload employees' })).getByRole('button', { name: 'Download template' })).toBeInTheDocument()
})

it('Proceed goes to the dashboard with seven steps done', async () => {
  withPeople()
  renderApp('/setup/employees')

  await userEvent.click(await within(await screen.findByRole('main')).findByRole('button', { name: 'Proceed to Org Dashboard' }))

  expect(await screen.findByText('Finish setting up · 7 of 10')).toBeInTheDocument()
})
