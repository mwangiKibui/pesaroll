import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, location, lockedPayItems, orgToken, payItem, schedule } from '../test/fakeApi'
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
  expect(within(basic).queryByRole('button')).not.toBeInTheDocument()

  await userEvent.click(screen.getByRole('tab', { name: 'Deductions' }))
  const rows = within(panel('Deductions')).getAllByRole('row').slice(1)
  expect(rows.map(r => within(r).getAllByRole('cell')[0].textContent)).toEqual(['PAYE', 'SHIF', 'NSSF (employee)NSSF', 'Housing Levy (employee)AHL', 'PensionPEN-01'])
  const shif = rows[1]
  expect(within(shif).getAllByRole('cell').map(c => c.textContent).slice(1, 3)).toEqual(['Set by law', 'Before tax'])
  expect(within(rows[4]).getAllByRole('cell')[1]).toHaveTextContent('5% of basic pay')
  expect(within(rows[4]).getByRole('button', { name: 'Delete Pension' })).toBeInTheDocument()

  await userEvent.click(screen.getByRole('tab', { name: 'Employer costs' }))
  expect(within(panel('Employer costs')).getByText('NITA')).toBeInTheDocument()
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
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))
  expect(within(drawer()).getByRole('textbox', { name: 'Percentage of basic pay' })).toHaveAccessibleDescription('Enter a percentage from 0.01 to 100.')

  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Percentage of basic pay' }), '5')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))
  expect(await screen.findByText('Deduction added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/pay-items')?.body).toMatchObject({ kind: 'deduction', beforeTax: true, taxable: false, calcMethod: 'percent_of_basic', rate: 5, defaultAmount: null })
})

it('a code another item has shows under the code', async () => {
  withItems({ 'POST /api/pay-items': () => apiError(409, 'ONB-3529', 'Another earning or deduction already has this code. Choose a different one or leave it empty.') })
  renderApp('/setup/earnings-and-deductions')
  await userEvent.click(await screen.findByRole('button', { name: 'Add' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Name' }), 'Housing')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Code optional' }), 'basic')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add' }))

  expect(await within(drawer()).findByText(/already has this code/)).toBeInTheDocument()
})

it('edits and deletes the organization\'s own items', async () => {
  const calls = withItems({
    'GET /api/companies': () => json(200, [restaurant, catering]),
    'GET /api/pay-items': () => json(200, [...lockedPayItems(), house]),
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
