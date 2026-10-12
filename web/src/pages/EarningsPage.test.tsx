import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, employeeRow, fakeApi, json, location, lockedPayItems, orgToken, payItem, schedule } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const signedIn = { 'POST /api/auth/refresh': () => orgToken() }
const restaurant = company()
const catering = company({ id: '0199c0de-0000-7000-8000-00000000c002', legalName: 'Oliech Catering Ltd', tradingName: null })
const kitchen = { id: 'dp1', code: 'KIT-01', name: 'Kitchen', divisionId: null, parentId: null, allCompanies: true, companyIds: [] }
const house = payItem({
  id: '0199c0de-0000-7000-8000-00000000e301', code: 'HOU-01', name: 'House allowance', calcMethod: 'fixed', defaultAmount: 15000,
  locked: false, lockedKey: null, allCompanies: false, companyIds: [catering.id],
})
const pension = payItem({
  id: '0199c0de-0000-7000-8000-00000000e302', code: 'PEN-01', name: 'Pension', kind: 'deduction', taxable: false, beforeTax: true,
  calcMethod: 'percent_of_basic', rate: 5, locked: false, lockedKey: null,
})

const drawer = () => screen.getByRole('dialog')
const panel = (name: string) => screen.getByRole('tabpanel', { name })

async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(drawer()).getByRole('combobox', { name }), search)
  await userEvent.click(within(drawer()).getByRole('option', { name: option }))
}

function withItems(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [restaurant]),
    'GET /api/locations': () => json(200, [location()]),
    'GET /api/structure/departments': () => json(200, [kitchen]),
    'GET /api/pay-schedules': () => json(200, [schedule()]),
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), pension]),
    ...routes,
  })
}

it('comes after Employees in setup', async () => {
  withItems()
  renderApp('/setup/earnings-and-deductions')
  const steps = await screen.findByRole('list', { name: 'Setup steps' })

  const names = within(steps).getAllByRole('listitem').map(i => i.textContent?.replace(/✓|optional|\d+/g, '').trim())
  expect(names.indexOf('Earnings and deductions')).toBe(names.indexOf('Employees') + 1)
})

it('shows earnings, deductions and employer costs on tabs; the law\'s items are locked', async () => {
  withItems()
  const { container } = renderApp('/setup/earnings-and-deductions')

  expect(await screen.findByRole('heading', { name: 'What do you pay, and what comes off pay?' })).toBeInTheDocument()
  expect(screen.getAllByRole('tab').map(t => t.textContent)).toEqual(['Earnings', 'Deductions', 'Employer costs'])
  const basic = (await within(await screen.findByRole('tabpanel', { name: 'Earnings' })).findByText('Basic pay')).closest('tr')!
  expect(within(basic).getAllByRole('cell').map(c => c.textContent).slice(0, 4)).toEqual(['Basic payBASIC', 'Fixed amount', 'Taxed', '—'])
  expect(within(basic).getByText('Always included')).toBeInTheDocument()
  expect(within(basic).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual(['View Basic pay'])   // to read, not to change

  await userEvent.click(screen.getByRole('tab', { name: 'Deductions' }))
  const rows = within(panel('Deductions')).getAllByRole('row').slice(1)
  expect(rows.map(r => within(r).getAllByRole('cell')[0].textContent)).toEqual(['PAYE', 'SHIF', 'NSSF (employee)NSSF', 'Housing Levy (employee)AHL', 'PensionPEN-01'])
  const shif = rows[1]
  expect(within(shif).getAllByRole('cell').map(c => c.textContent).slice(1, 3)).toEqual(['Set by law', 'Before tax'])
  expect(within(rows[4]).getAllByRole('cell')[1]).toHaveTextContent('5% of basic pay')
  expect(within(rows[4]).getByRole('button', { name: 'Delete Pension' })).toBeInTheDocument()

  await userEvent.click(screen.getByRole('tab', { name: 'Employer costs' }))
  const nita = within(panel('Employer costs')).getByText('NITA', { selector: '.font-medium' }).closest('tr')!
  expect(within(nita).getAllByRole('cell').map(c => c.textContent)).toContain('NITA')   // and it's paid to NITA
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Earnings and deductions').closest('a')).toHaveAttribute('aria-current', 'page')
  expect(within(steps).getByText('Earnings and deductions').closest('li')).toHaveTextContent('✓')
  await expectNoA11yViolations(container)
})

it('adds an earning from a common one; it starts on the tab you are on', async () => {
  const calls = withItems({ 'POST /api/pay-items': () => json(201, house) })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('button', { name: 'Add' }))

  expect(within(drawer()).getByRole('heading', { name: 'Add earning or deduction' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('radio', { name: 'Earning' })).toBeChecked()
  await choose('Start from a common one optional', 'house', 'House allowance')
  expect(within(drawer()).getByRole('textbox', { name: 'Name' })).toHaveValue('House allowance')
  expect(within(drawer()).getByRole('combobox', { name: 'How it\'s worked out' })).toHaveValue('A fixed amount')
  expect(within(drawer()).getByRole('switch', { name: 'Taxed' })).toBeChecked()
  expect(within(drawer()).getByRole('switch', { name: 'Taxed' })).toHaveAccessibleDescription(/Almost all earnings are taxed/)
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Default amount (KES) optional' }), '15000')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))

  expect(await screen.findByText('Earning added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-items')?.body).toMatchObject({
    name: 'House allowance', kind: 'earning', taxable: true, beforeTax: false, calcMethod: 'fixed', defaultAmount: 15000, rate: null,
    allCompanies: false, companyIds: [restaurant.id],
  })
})

it('a deduction can come off before tax, as a percentage of basic pay', async () => {
  const calls = withItems({ 'POST /api/pay-items': () => json(201, pension) })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('tab', { name: 'Deductions' }))
  await userEvent.click(screen.getByRole('button', { name: 'Add' }))

  expect(within(drawer()).getByRole('radio', { name: 'Deduction' })).toBeChecked()
  expect(within(drawer()).queryByRole('switch', { name: 'Taxed' })).not.toBeInTheDocument()
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Name' }), 'Pension')
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'How it\'s worked out' }))
  await choose('How it\'s worked out', 'perc', 'A percentage of basic pay')
  await userEvent.click(within(drawer()).getByRole('switch', { name: 'Taken off before tax' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))
  expect(within(drawer()).getByRole('textbox', { name: 'Percentage of basic pay' })).toHaveAccessibleDescription('Enter a percentage from 0.01 to 100.')

  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Percentage of basic pay' }), '5')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))
  expect(await screen.findByText('Deduction added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-items')?.body).toMatchObject({ kind: 'deduction', beforeTax: true, taxable: false, calcMethod: 'percent_of_basic', rate: 5, defaultAmount: null })
})

it('adding goes Details, Next, then Who gets it with Back and Add; a details problem from the server goes back to Details', async () => {
  const calls = withItems({ 'POST /api/pay-items': () => apiError(409, 'ONB-3529', 'Another earning or deduction already has this code. Choose a different one or leave it empty.') })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('button', { name: 'Add' }))
  expect(within(drawer()).queryByRole('button', { name: 'Add' })).not.toBeInTheDocument()

  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))   // no name yet: stays
  expect(within(drawer()).getByRole('tab', { name: 'Details' })).toHaveAttribute('aria-selected', 'true')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Name' }), 'Housing')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Code optional' }), 'basic')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))
  expect(within(drawer()).getByRole('tab', { name: 'Who gets it' })).toHaveAttribute('aria-selected', 'true')
  expect(calls.some(c => c.method === 'POST' && c.path === '/api/pay-items')).toBe(false)   // Next saves nothing

  await userEvent.click(within(drawer()).getByRole('button', { name: 'Back' }))
  expect(within(drawer()).getByRole('tab', { name: 'Details' })).toHaveAttribute('aria-selected', 'true')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))
  expect(await within(drawer()).findByText(/already has this code/)).toBeInTheDocument()
  expect(within(drawer()).getByRole('tab', { name: 'Details' })).toHaveAttribute('aria-selected', 'true')
})

it('a code another item has shows under the code', async () => {
  withItems({ 'POST /api/pay-items': () => apiError(409, 'ONB-3529', 'Another earning or deduction already has this code. Choose a different one or leave it empty.') })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('button', { name: 'Add' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Name' }), 'Housing')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Code optional' }), 'basic')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))

  expect(await within(drawer()).findByText(/already has this code/)).toBeInTheDocument()
})

it('edits and deletes the organization\'s own items', async () => {
  const calls = withItems({
    'GET /api/companies': () => json(200, [restaurant, catering]),
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), house]),
    [`GET /api/pay-items/${house.id}/assignments`]: () => json(200, []),
    [`PATCH /api/pay-items/${house.id}`]: () => json(200, { ...house, name: 'Housing allowance' }),
    [`DELETE /api/pay-items/${house.id}`]: () => new Response(null, { status: 204 }),
  })
  renderApp('/setup/earnings-and-deductions')

  await userEvent.click(await screen.findByRole('button', { name: 'Edit House allowance' }))
  expect(within(drawer()).getByRole('heading', { name: 'Edit earning or deduction' })).toBeInTheDocument()
  expect(within(drawer()).queryByRole('combobox', { name: /Start from a common one/ })).not.toBeInTheDocument()
  const name = within(drawer()).getByRole('textbox', { name: 'Name' })
  await userEvent.clear(name)
  await userEvent.type(name, 'Housing allowance')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PATCH')?.body).toMatchObject({ code: 'HOU-01', name: 'Housing allowance', companyIds: [catering.id] })

  await userEvent.click(screen.getByRole('button', { name: 'Delete House allowance' }))
  await userEvent.click(within(screen.getByRole('alertdialog', { name: 'Delete House allowance?' })).getByRole('button', { name: 'Delete' }))
  expect(await screen.findByText('Deleted')).toBeInTheDocument()
})

it('the table says who gets each item', async () => {
  withItems({
    'GET /api/companies': () => json(200, [restaurant, catering]),
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), { ...house, allCompanies: true, assignments: [
      { companyId: restaurant.id, everyone: true, people: 0 }, { companyId: catering.id, everyone: false, people: 3 }] }]),
  })
  renderApp('/setup/earnings-and-deductions')
  const earnings = await screen.findByRole('tabpanel', { name: 'Earnings' })
  const row = (await within(earnings).findByText('House allowance')).closest('tr')!
  expect(within(earnings).getAllByRole('columnheader').map(h => h.textContent)).toContain('Who gets it')
  expect(row).toHaveTextContent('Everyone at Mama Oliech Restaurant Ltd · 3 people at Oliech Catering Ltd')
  expect(within(earnings).getByText('Basic pay').closest('tr')).toHaveTextContent('Everyone')
})

it('a new item goes to chosen people found by any identifier, each with their own amount and dates', async () => {
  const wanjiru = employeeRow()
  const searches: string[] = []
  const calls = withItems({
    'POST /api/pay-items': () => json(201, { ...house, id: 'new-sacco', name: 'SACCO shares', kind: 'deduction', taxable: false, defaultAmount: 1500, companyIds: [restaurant.id] }),
    'PUT /api/pay-items/new-sacco/assignments': () => json(200, []),
    'GET /api/employees': req => {
      const q = new URL(req.url).searchParams
      if (q.get('search')) searches.push(q.get('search')!)
      return json(200, { items: q.get('search') ? [wanjiru] : [], total: q.get('search') ? 1 : 0, page: 1, pageSize: 8 })
    },
  })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('button', { name: 'Add' }))
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'Deduction' }))
  await choose('Start from a common one optional', 'sacco', 'SACCO savings')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Default amount (KES) optional' }), '1500')

  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Who gets it' }))
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'Specific employees' }))
  await userEvent.type(within(drawer()).getByRole('searchbox', { name: 'Find people' }), '0712 345 678')
  await userEvent.click(await within(drawer()).findByRole('button', { name: 'Add Wanjiru Kamau (EMP-01)' }))
  expect(searches.at(-1)).toBe('0712 345 678')   // the API matches any identifier
  const amount = within(drawer()).getByRole('textbox', { name: 'Amount (KES) for Wanjiru Kamau' })
  expect(amount).toHaveAttribute('placeholder', '1,500')   // the item's default unless changed
  await userEvent.type(amount, '2000')
  await userEvent.type(within(drawer()).getByLabelText('Until for Wanjiru Kamau'), '2027-03-31')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Member or account number for Wanjiru Kamau' }), 'M-10234')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))

  expect(await screen.findByText('Deduction added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PUT')?.body).toMatchObject({
    companyId: restaurant.id, everyone: false, people: [{ employeeId: wanjiru.id, amount: 2000, endsOn: '2027-03-31', reference: 'M-10234' }],
  })
})

it('a person can have a From date and no Until', async () => {
  const wanjiru = employeeRow()
  const calls = withItems({
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), pension]),
    [`GET /api/pay-items/${pension.id}/assignments`]: () => json(200, []),
    [`PATCH /api/pay-items/${pension.id}`]: () => json(200, pension),
    [`PUT /api/pay-items/${pension.id}/assignments`]: () => json(200, []),
    'GET /api/employees': () => json(200, { items: [wanjiru], total: 1, page: 1, pageSize: 8 }),
  })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('tab', { name: 'Deductions' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Pension' }))
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Who gets it' }))
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'Specific employees' }))
  await userEvent.type(within(drawer()).getByRole('searchbox', { name: 'Find people' }), 'wanj')
  await userEvent.click(await within(drawer()).findByRole('button', { name: 'Add Wanjiru Kamau (EMP-01)' }))
  expect(within(drawer()).getByRole('columnheader', { name: 'Until (optional)' })).toBeInTheDocument()
  expect(within(drawer()).getByText(/Leave Until empty when it has no end date/)).toBeInTheDocument()
  await userEvent.type(within(drawer()).getByLabelText('From for Wanjiru Kamau'), '2026-11-01')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PUT')?.body).toMatchObject({ people: [{ employeeId: wanjiru.id, startsOn: '2026-11-01', endsOn: null }] })
})

it('everyone at a company, and a problem the server finds shows on its tab', async () => {
  const calls = withItems({
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), pension]),
    [`GET /api/pay-items/${pension.id}/assignments`]: () => json(200, []),
    [`PATCH /api/pay-items/${pension.id}`]: () => json(200, pension),
    [`PUT /api/pay-items/${pension.id}/assignments`]: () => json(400, { code: 'ONB-3547', message: 'Some details of who gets it don\'t look right.', reference: '7F3K92QD',
      fields: { rate: 'Enter a rate above 0 and up to 100.' } }),
  })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('tab', { name: 'Deductions' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Edit Pension' }))
  await userEvent.click(within(drawer()).getByRole('tab', { name: 'Who gets it' }))
  await userEvent.click(within(drawer()).getByRole('radio', { name: 'All employees of Mama Oliech Restaurant Ltd' }))
  expect(within(drawer()).getByText(/including people hired later/)).toBeInTheDocument()
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Rate (%) optional' }), '150')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await within(drawer()).findByRole('textbox', { name: 'Rate (%) optional' })).toHaveAccessibleDescription(/Enter a rate above 0 and up to 100./)
  expect(calls.find(c => c.method === 'PUT')?.body).toMatchObject({ companyId: restaurant.id, everyone: true, rate: 150 })
})

it('a deduction is paid to a payee, added from the drawer; the law\'s items show who they go to', async () => {
  const stima = { id: 'payee-1', name: 'Stima SACCO', method: 'mpesa_paybill', bankCode: null, bankName: null, branchCode: null, branchName: null,
    accountNumber: null, paybill: '510800', paybillAccount: 'EMP-4471', chequePayableTo: null, ourReference: 'EMP-4471', usedBy: 0 }
  let payees: unknown[] = []
  const calls = withItems({
    'GET /api/payees': () => json(200, payees),
    'POST /api/payees': () => { payees = [stima]; return json(201, stima) },
    'POST /api/pay-items': () => json(201, { ...pension, id: 'new-shares', name: 'SACCO savings', calcMethod: 'fixed', rate: null, payeeId: stima.id }),
  })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('tab', { name: 'Deductions' }))
  const deductions = screen.getByRole('tabpanel', { name: 'Deductions' })
  expect(within(deductions).getAllByRole('columnheader').map(h => h.textContent)).toContain('Paid to')
  expect(within(deductions).getByText('SHIF').closest('tr')).toHaveTextContent('SHA')

  await userEvent.click(screen.getByRole('button', { name: 'Add' }))
  await choose('Start from a common one optional', 'sacco', 'SACCO savings')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add a payee' }))
  const modal = screen.getByRole('dialog', { name: 'Add a payee' })
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Name' }), 'Stima SACCO')
  await userEvent.click(within(modal).getByRole('radio', { name: /M-Pesa paybill/ }))   // (it starts on Bank / SACCO transfer)
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Paybill number' }), '510800')
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Account optional' }), 'EMP-4471')
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Your reference with them optional' }), 'EMP-4471')
  await userEvent.click(within(modal).getByRole('combobox', { name: 'How they identify each person' }))
  await userEvent.click(screen.getByRole('option', { name: 'Their staff number' }))
  await userEvent.click(within(modal).getByRole('button', { name: 'Add payee' }))

  expect(await within(drawer()).findByRole('combobox', { name: 'Paid to optional' })).toHaveValue('Stima SACCO')   // chosen
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/payees')?.body).toMatchObject({
    name: 'Stima SACCO', method: 'mpesa_paybill', paybill: '510800', paybillAccount: 'EMP-4471', ourReference: 'EMP-4471', referenceSource: 'staff_no',
  })
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Next' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))
  expect(await screen.findByText('Deduction added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-items')?.body).toMatchObject({ payeeId: stima.id })
})

it('a payee can be paid by SACCO transfer, to a SACCO and branch from the list', async () => {
  const calls = withItems({
    'GET /api/saccos': () => json(200, [
      { code: 'STIMA', name: 'Stima SACCO', branches: [{ code: '001', name: 'Head Office, Parklands, Nairobi' }, { code: '005', name: 'Mombasa' }] },
      { code: 'MENTOR', name: 'Mentor SACCO', branches: [{ code: '002', name: 'Kenol' }] },
    ]),
    'POST /api/payees': () => json(201, { id: 'payee-2', name: 'Welfare Fund', method: 'bank', institution: 'sacco', usedBy: 0 }),
  })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('tab', { name: 'Deductions' }))
  await userEvent.click(screen.getByRole('button', { name: 'Add' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add a payee' }))
  const modal = screen.getByRole('dialog', { name: 'Add a payee' })
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Name' }), 'Welfare Fund')
  await userEvent.click(within(modal).getByRole('radio', { name: /Bank \/ SACCO transfer/ }))
  await userEvent.click(within(modal).getByRole('radio', { name: 'SACCO' }))
  expect(within(modal).getByRole('combobox', { name: 'Branch' })).toBeDisabled()   // until a SACCO is chosen
  await userEvent.click(within(modal).getByRole('combobox', { name: 'SACCO' }))
  await userEvent.click(screen.getByRole('option', { name: 'Stima SACCO' }))
  await userEvent.click(within(modal).getByRole('combobox', { name: 'Branch' }))
  expect(screen.queryByRole('option', { name: 'Kenol' })).not.toBeInTheDocument()   // Mentor's
  await userEvent.click(screen.getByRole('option', { name: 'Mombasa' }))
  await userEvent.type(within(modal).getByRole('textbox', { name: 'Account number' }), 'ST-00451')
  await userEvent.click(within(modal).getByRole('button', { name: 'Add payee' }))

  await vi.waitFor(() => expect(calls.find(c => c.method === 'POST' && c.path === '/api/payees')?.body).toMatchObject({
    name: 'Welfare Fund', method: 'bank', institution: 'sacco', saccoCode: 'STIMA', saccoBranchCode: '005', accountNumber: 'ST-00451', bankCode: null,
  }))
})

it('the eye opens an item to read: who it\'s paid to and who gets it, with member numbers', async () => {
  const stima = { id: 'payee-1', name: 'Stima SACCO', method: 'bank', institution: 'sacco', saccoCode: 'STIMA', saccoName: 'Stima SACCO', saccoBranchCode: '005',
    saccoBranchName: 'Mombasa', accountNumber: 'ST-00451', bankCode: null, bankName: null, branchCode: null, branchName: null, paybill: null,
    paybillAccount: null, chequePayableTo: null, ourReference: 'EMP-4471', usedBy: 1 }
  const loan = payItem({ id: 'loan-1', code: 'LOA-01', name: 'Loan Repayment', kind: 'deduction', taxable: false, calcMethod: 'fixed', defaultAmount: 5000,
    locked: false, lockedKey: null, payeeId: stima.id, assignments: [{ companyId: restaurant.id, everyone: false, people: 1 }] })
  withItems({
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), loan]),
    'GET /api/payees': () => json(200, [stima]),
    'GET /api/pay-items/loan-1/assignments': () => json(200, [{ companyId: restaurant.id, everyone: false, amount: null, rate: null, startsOn: null, endsOn: null,
      people: [{ employeeId: 'e1', name: 'Wanjiru Kamau', staffNo: 'EMP-01', amount: 2000, rate: null, startsOn: '2026-10-06', endsOn: null, reference: '94737',
        shownReference: '94737', fromProfile: false }], missing: [] }]),
  })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('tab', { name: 'Deductions' }))
  expect(within(panel('Deductions')).getByRole('button', { name: 'View SHIF' })).toBeInTheDocument()   // the law's items too
  await userEvent.click(within(panel('Deductions')).getByRole('button', { name: 'View Loan Repayment' }))

  const view = screen.getByRole('dialog', { name: 'Loan Repayment' })
  expect(within(view).getByText('Stima SACCO · Mombasa · account ST-00451')).toBeInTheDocument()
  expect(within(view).getByText('Your reference: EMP-4471')).toBeInTheDocument()
  await userEvent.click(within(view).getByRole('tab', { name: 'Who gets it' }))
  const row = (await within(view).findByText('Wanjiru Kamau')).closest('tr')!
  expect(within(row).getAllByRole('cell').map(c => c.textContent)).toEqual(['Wanjiru KamauEMP-01', 'KES 2,000', '94737', 'from 6 Oct 2026'])

  await userEvent.click(within(view).getByRole('button', { name: 'Edit' }))
  expect(await screen.findByRole('dialog', { name: 'Edit earning or deduction' })).toBeInTheDocument()
})

it('each tab\'s table is paginated, 20 to a page', async () => {
  const many = Array.from({ length: 25 }, (_, n) => payItem({ id: `own-${n}`, code: `OWN-${n + 1}`, name: `Allowance ${String(n + 1).padStart(2, '0')}`,
    locked: false, lockedKey: null }))
  withItems({ 'GET /api/pay-items': () => json(200, [...lockedPayItems(), ...many]) })
  renderApp('/setup/earnings-and-deductions')
  const earnings = await screen.findByRole('tabpanel', { name: 'Earnings' })
  const pages = await within(earnings).findByRole('navigation', { name: 'Pages of Earnings' })
  expect(within(pages).getByText('Showing 1–20 of 26')).toBeInTheDocument()   // Basic pay and 25 own
  await userEvent.click(within(pages).getByRole('button', { name: 'Next page' }))
  expect(within(pages).getByText('Showing 21–26 of 26')).toBeInTheDocument()
  expect(within(earnings).getByText('Allowance 25')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('tab', { name: 'Deductions' }))
  expect(within(panel('Deductions')).getByRole('navigation', { name: 'Pages of Deductions' })).toHaveTextContent('Showing 1–4 of 4')
})

it('as the payee identifies people, account numbers come from profiles: shown masked, typed ones win, and who lacks one is named', async () => {
  const helb = payItem({ id: 'helb-1', code: 'HEL-01', name: 'HELB loan', kind: 'deduction', taxable: false, calcMethod: 'fixed', defaultAmount: 2000,
    locked: false, lockedKey: null, payeeId: 'payee-helb', assignments: [{ companyId: restaurant.id, everyone: false, people: 2 }] })
  const helbPayee = { id: 'payee-helb', name: 'HELB', method: 'mpesa_paybill', paybill: '200800', referenceSource: 'national_id', usedBy: 1 }
  const calls = withItems({
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), helb]),
    'GET /api/payees': () => json(200, [helbPayee]),
    'GET /api/pay-items/helb-1/assignments': () => json(200, [{ companyId: restaurant.id, everyone: false, amount: null, rate: null, startsOn: null, endsOn: null,
      people: [
        { employeeId: 'e1', name: 'Wanjiru Kamau', staffNo: 'EMP-01', amount: null, rate: null, startsOn: '2026-11-01', endsOn: null, reference: null, shownReference: '•••• 5678', fromProfile: true },
        { employeeId: 'e2', name: 'Otieno Ouma', staffNo: 'EMP-02', amount: null, rate: null, startsOn: '2026-11-01', endsOn: null, reference: null, shownReference: null, fromProfile: false },
      ],
      missing: [{ employeeId: 'e2', name: 'Otieno Ouma', staffNo: 'EMP-02' }] }]),
    'PATCH /api/pay-items/helb-1': () => json(200, helb),
    'PUT /api/pay-items/helb-1/assignments': () => json(200, []),
  })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('tab', { name: 'Deductions' }))

  // Reading it: the number that applies, and who lacks one.
  await userEvent.click(within(panel('Deductions')).getByRole('button', { name: 'View HELB loan' }))
  const view = screen.getByRole('dialog', { name: 'HELB loan' })
  expect(within(view).getByText('Their ID number')).toBeInTheDocument()
  await userEvent.click(within(view).getByRole('tab', { name: 'Who gets it' }))
  expect(await within(view).findByText('1 person has no ID number in their profile yet: Otieno Ouma.')).toBeInTheDocument()
  expect((await within(view).findByText('Wanjiru Kamau')).closest('tr')).toHaveTextContent('•••• 5678 (profile)')
  await userEvent.click(within(view).getByRole('button', { name: 'Edit' }))

  // Editing: as HELB identifies people, each person's from their profile as a hint, and a typed one for Otieno.
  await userEvent.click(await within(drawer()).findByRole('tab', { name: 'Who gets it' }))
  expect(within(drawer()).getByText('HELB identifies each person by: Their ID number.')).toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'Member or account number for Wanjiru Kamau' })).toHaveAttribute('placeholder', '•••• 5678')
  expect(within(drawer()).getByText(/no ID number in their profile yet: Otieno Ouma/)).toBeInTheDocument()
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Member or account number for Otieno Ouma' }), 'HB-2201')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PUT')?.body).toMatchObject({ people: [{ employeeId: 'e1', reference: null }, { employeeId: 'e2', reference: 'HB-2201' }] })
})

it('with several companies, the selector filters every tab', async () => {
  withItems({ 'GET /api/companies': () => json(200, [restaurant, catering]), 'GET /api/pay-items': () => json(200, [...lockedPayItems(), house]) })
  renderApp('/setup/earnings-and-deductions')
  expect(await within(await screen.findByRole('tabpanel', { name: 'Earnings' })).findByText('House allowance')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: /Show companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Ltd/ }))   // untick: only the restaurant
  await userEvent.keyboard('{Escape}')

  expect(within(panel('Earnings')).queryByText('House allowance')).not.toBeInTheDocument()
  expect(within(panel('Earnings')).getByText('Basic pay')).toBeInTheDocument()
})

it('Proceed goes to the dashboard with six steps done', async () => {
  withItems()
  renderApp('/setup/earnings-and-deductions')

  await userEvent.click(await within(await screen.findByRole('main')).findByRole('button', { name: 'Proceed to Org Dashboard' }))

  expect(await screen.findByText('Finish setting up · 6 of 10')).toBeInTheDocument()
  expect(screen.getAllByText('Next: Leave plans').length).toBeGreaterThan(0)
})
