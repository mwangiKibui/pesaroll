import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { company, employeeDetail, employeeRow, equityAccount, fakeApi, json, leavePlan, location, orgToken, schedule } from '../test/fakeApi'
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

it('Add employee opens a sheet with one row: the company\'s lists, its schedule and leave plan filled in, and Date left waits for Still works here', async () => {
  let rows: unknown[] = []
  withPeople({
    'GET /api/structure/positions': () => json(200, [{ id: 'ps1', code: 'COO-01', title: 'Head cook', departmentId: 'dp1', allCompanies: true, companyIds: [] }]),
    'POST /api/employees/import/check': async req => { rows = ((await req.json()) as { rows: unknown[] }).rows; return json(200, { problems: [], notes: [] }) },
    'POST /api/employees/import': () => json(200, { imported: 2 }),
  })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))

  const sheet = await screen.findByRole('dialog', { name: /Add 1 employee/ })
  expect(within(sheet).getAllByRole('textbox', { name: /First name, row/ })).toHaveLength(1)
  expect(within(sheet).queryByRole('combobox', { name: 'Company, row 1' })).not.toBeInTheDocument()   // one company, chosen above
  expect(within(sheet).queryByRole('combobox', { name: 'Paid, row 1' })).not.toBeInTheDocument()   // pay follows the schedule
  expect(within(sheet).queryByRole('combobox', { name: 'Location, row 1' })).not.toBeInTheDocument()   // the company's main location
  expect(within(sheet).getByRole('textbox', { name: 'Address, row 1' })).toBeInTheDocument()
  expect(within(sheet).queryByRole('button', { name: 'Remove row 1' })).not.toBeInTheDocument()   // the first row stays
  for (const column of ['Division', 'Department', 'Unit', 'Position', 'Paid into', 'Bank']) {
    expect(within(sheet).getByRole('combobox', { name: `${column}, row 1` })).toBeInTheDocument()
  }
  expect(within(sheet).getByRole('combobox', { name: 'Payroll schedule, row 1' })).toHaveValue('Monthly')
  expect(within(sheet).getByRole('combobox', { name: 'Leave plan, row 1' })).toHaveValue('Standard')
  expect(within(sheet).getByRole('checkbox', { name: 'Still works here, row 1' })).toBeChecked()
  expect(within(sheet).getByLabelText('Date left, row 1')).toBeDisabled()
  expect(within(sheet).getByLabelText('Contract end, row 1')).toBeDisabled()   // Permanent
  expect(within(sheet).getByRole('combobox', { name: 'Branch, row 1' })).toBeDisabled()   // until a bank is chosen

  await userEvent.type(within(sheet).getByRole('textbox', { name: 'First name, row 1' }), 'Wanjiru')
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'Last name, row 1' }), 'Kamau')
  await userEvent.type(within(sheet).getByLabelText('Start date, row 1'), '2024-01-15')
  await userEvent.type(within(sheet).getByRole('combobox', { name: 'Position, row 1' }), 'head')
  await userEvent.click(screen.getByRole('option', { name: 'Head cook' }))

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
    { line: 1, cells: { company: 'Mama Oliech Restaurant Ltd', firstName: 'Wanjiru', hireDate: '2024-01-15', position: 'Head cook', stillHere: 'Yes', exitDate: '',
      payrollSchedule: 'Monthly', leavePlan: 'Standard', employmentType: 'Permanent' } },
    { line: 2, cells: { company: 'Mama Oliech Restaurant Ltd', firstName: 'Akinyi', stillHere: 'No', exitDate: '2024-06-30' } },
  ])
  expect(rows[0]).not.toHaveProperty('cells.payBasis')
})

it('a cell is checked as soon as it is left, with the rules for Kenyan numbers and each bank', async () => {
  withPeople()
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))
  const sheet = await screen.findByRole('dialog', { name: /Add 1 employee/ })

  const pin = within(sheet).getByRole('textbox', { name: 'KRA PIN, row 1' })
  await userEvent.type(pin, 'P0512345')
  await userEvent.tab()
  expect(pin).toHaveAccessibleDescription('Enter a KRA PIN like A012345678Z: A, 9 digits and a letter.')
  const nssf = within(sheet).getByRole('textbox', { name: 'NSSF number, row 1' })
  await userEvent.type(nssf, '12345')
  await userEvent.tab()
  expect(nssf).toHaveAccessibleDescription('Enter 7 to 10 digits.')
  const mpesa = within(sheet).getByRole('textbox', { name: 'M-Pesa number, row 1' })
  expect(mpesa).toBeDisabled()   // until Paid into says M-Pesa
  await userEvent.click(within(sheet).getByRole('combobox', { name: 'Paid into, row 1' }))
  await userEvent.click(screen.getByRole('option', { name: 'M-Pesa' }))
  await userEvent.type(mpesa, '0733 123 456')   // Airtel
  await userEvent.tab()
  expect(mpesa).toHaveAccessibleDescription('Enter a Safaricom number for M-Pesa, like 0712 345 678.')

  await userEvent.clear(mpesa)
  await userEvent.type(mpesa, '0791 569 999')
  await userEvent.tab()
  expect(mpesa).not.toHaveAccessibleDescription(/Safaricom/)

  await userEvent.click(within(sheet).getByRole('combobox', { name: 'Paid into, row 1' }))
  await userEvent.click(screen.getByRole('option', { name: 'Bank' }))
  expect(mpesa).toBeDisabled()
  expect(mpesa).toHaveValue('')   // only one way to be paid
  await userEvent.type(within(sheet).getByRole('combobox', { name: 'Bank, row 1' }), 'equ')
  await userEvent.click(screen.getByRole('option', { name: 'Equity Bank' }))
  const branch = within(sheet).getByRole('combobox', { name: 'Branch, row 1' })
  expect(branch).toBeEnabled()
  await userEvent.click(branch)
  expect(screen.getAllByRole('option').map(o => o.textContent)).toEqual(['Moi Avenue, Nairobi', 'Mombasa'])   // Equity's only
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa' }))
  const account = within(sheet).getByRole('textbox', { name: 'Account number, row 1' })
  await userEvent.type(account, '0123456789')
  await userEvent.tab()
  expect(account).toHaveAccessibleDescription('Equity Bank account numbers are 13 digits.')
  await userEvent.type(account, '012')
  await userEvent.tab()
  expect(account).not.toHaveAccessibleDescription(/digits/)

  await userEvent.type(within(sheet).getByRole('combobox', { name: 'Employment type, row 1' }), '{Control>}a{/Control}contr')
  await userEvent.click(screen.getByRole('option', { name: 'Contract' }))
  expect(within(sheet).getByLabelText('Contract end, row 1')).toBeEnabled()
})

it('people who work here are paid into an account the company pays from', async () => {
  const calls = withPeople({
    [`GET /api/companies/${restaurant.id}/payment-accounts`]: () => json(200, [equityAccount()]),
    'POST /api/employees/import/check': () => json(200, { problems: [], notes: [] }),
  })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Add employee' }))
  const sheet = await screen.findByRole('dialog', { name: /Add 1 employee/ })
  expect(within(sheet).getByRole('combobox', { name: 'Paid into, row 1' })).toHaveValue('Bank')   // how the company pays
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'First name, row 1' }), 'Wanjiru')
  await userEvent.type(within(sheet).getByRole('textbox', { name: 'Last name, row 1' }), 'Kamau')
  await userEvent.type(within(sheet).getByLabelText('Start date, row 1'), '2024-01-15')
  await userEvent.click(within(sheet).getByRole('button', { name: 'Add 1 employee' }))
  expect(within(sheet).getByRole('combobox', { name: 'Bank, row 1' })).toHaveAccessibleDescription('This is needed.')

  // M-Pesa when the company only pays by bank.
  await userEvent.click(within(sheet).getByRole('combobox', { name: 'Paid into, row 1' }))
  await userEvent.click(screen.getByRole('option', { name: 'M-Pesa' }))
  expect(within(sheet).getByRole('combobox', { name: 'Paid into, row 1' }))
    .toHaveAccessibleDescription('Mama Oliech Restaurant Ltd pays by bank: add their bank details.')
  expect(calls.some(c => c.path === '/api/employees/import/check')).toBe(false)
})

it('Upload employees starts with one company, and its template has that company\'s lists', async () => {
  const calls = withPeople({ 'GET /api/employees/template': () => new Response(new Blob(['x'])) })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Upload employees' }))
  const upload = await screen.findByRole('dialog', { name: 'Upload employees' })
  expect(within(upload).getByRole('heading', { name: '1. Choose the company' })).toBeInTheDocument()
  expect(within(upload).getByRole('combobox', { name: 'Company' })).toHaveValue('Mama Oliech Restaurant Ltd')

  await userEvent.click(within(upload).getByRole('button', { name: 'Download template' }))
  await vi.waitFor(() => expect(calls.some(c => c.path === '/api/employees/template' && c.url.includes(`companyId=${restaurant.id}`))).toBe(true))
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

it('the eye opens an employee to read, tab by tab, with Edit to change them', async () => {
  withPeople({
    'GET /api/structure/departments': () => json(200, [kitchen]),
    [`GET /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail({ job: { ...employeeDetail().job, departmentId: 'dp1' } })),
  })
  renderApp('/setup/employees')
  const row = (await within(await screen.findByRole('region', { name: 'Employees' })).findByText('Wanjiru Kamau')).closest('tr')!
  const buttons = within(row).getAllByRole('button').map(b => b.getAttribute('aria-label'))
  expect(buttons).toEqual(['View Wanjiru Kamau', 'Edit Wanjiru Kamau', 'Delete Wanjiru Kamau'])   // view first

  await userEvent.click(within(row).getByRole('button', { name: 'View Wanjiru Kamau' }))
  const view = await screen.findByRole('dialog', { name: 'Wanjiru Kamau' })
  expect(await within(view).findByText('Hse 12, Kilimani, Nairobi')).toBeInTheDocument()
  expect(within(view).queryByRole('textbox')).not.toBeInTheDocument()   // nothing to type into
  await userEvent.click(within(view).getByRole('tab', { name: 'IDs and numbers' }))
  expect(within(view).getByText('•••• 678Z')).toBeInTheDocument()   // numbers stay masked
  await userEvent.click(within(view).getByRole('tab', { name: 'Job' }))
  expect(within(view).getByText('Kitchen')).toBeInTheDocument()
  expect(within(view).getByText('Monthly')).toBeInTheDocument()
  await userEvent.click(within(view).getByRole('tab', { name: 'Pay' }))
  expect(within(view).getByText('KES 85,000 a month')).toBeInTheDocument()
  await userEvent.click(within(view).getByRole('tab', { name: 'Paid into' }))
  expect(within(view).getByText('M-Pesa')).toBeInTheDocument()

  await userEvent.click(within(view).getByRole('button', { name: 'Edit' }))
  expect(await screen.findByRole('dialog', { name: 'Edit employee' })).toBeInTheDocument()
  expect(screen.queryByRole('dialog', { name: 'Wanjiru Kamau' })).not.toBeInTheDocument()
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

it('pay follows the schedule: a daily schedule asks for a daily rate', async () => {
  const calls = withPeople({
    [`GET /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail()),
    [`PATCH /api/employees/${wanjiru.id}`]: () => json(200, employeeDetail()),
  })
  renderApp('/setup/employees')
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Wanjiru Kamau' }))
  await within(drawer()).findByDisplayValue('Wanjiru')
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Pay' }))
  expect(within(drawer()).queryByRole('radio', { name: 'Daily' })).not.toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'Monthly salary (KES) optional' })).toBeInTheDocument()

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Job' }))
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'Payroll schedule' }))
  await choose('Payroll schedule', 'cas', 'Casuals daily')
  expect(within(drawer()).getByLabelText(/Contract end/)).toBeDisabled()
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Pay' }))
  const rate = within(drawer()).getByRole('textbox', { name: 'Daily rate (KES) optional' })
  expect(rate).toHaveAccessibleDescription('Paid daily, as their payroll schedule says.')
  await userEvent.clear(rate)
  await userEvent.type(rate, '-5')
  await userEvent.tab()
  expect(rate).toHaveAccessibleDescription(/Enter an amount above 0, like 45,000./)
  await userEvent.clear(rate)
  await userEvent.type(rate, '1,200')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Employee saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PATCH')?.body).toMatchObject({ pay: { basicAmount: 1200, payBasis: null } })
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
