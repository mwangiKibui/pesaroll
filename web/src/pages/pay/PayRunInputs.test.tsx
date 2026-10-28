import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { apiError, company, fakeApi, json, me, orgToken, payRunDetail } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

beforeEach(() => vi.stubEnv('VITE_FEATURES', 'payRuns'))
afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const run = payRunDetail({ companyId: westlands.id, cutoffOn: '2026-10-25' })
const path = `/pay/runs/${run.id}`
const api = `/api/pay-runs/${run.id}`
const peter = '0199c0de-0000-7000-8000-00000000a601'
const brian = '0199c0de-0000-7000-8000-00000000a602'
const otieno = '0199c0de-0000-7000-8000-00000000a603'
const overtime = '0199c0de-0000-7000-8000-00000000b601'
const commission = '0199c0de-0000-7000-8000-00000000b602'
const entry = (overrides: Record<string, unknown>) => ({
  payItemId: null, quantity: null, amount: null, reason: null, source: 'entered', enteredBy: 'u1', enteredAt: '2026-10-20T07:00:00Z', afterCutoff: false, ...overrides,
})
const inputs = (people?: unknown[]) => ({
  payRunId: run.id,
  people: people ?? [
    { employeeId: peter, staffNo: 'W-01', name: 'Peter Kamau', unit: 'day', usualQuantity: 22, entries: [entry({ quantity: 20 }), entry({ payItemId: commission, amount: 1500 })], deductions: null, leftOn: null },
    { employeeId: brian, staffNo: 'W-02', name: 'Brian Mwangi', unit: null, usualQuantity: null, entries: [entry({ payItemId: commission, amount: 4000, afterCutoff: true })], deductions: null, leftOn: null },
    { employeeId: otieno, staffNo: 'W-03', name: 'Otieno Ouma', unit: 'hour', usualQuantity: null, entries: [], deductions: null, leftOn: '2026-10-28' },
  ],
  items: [{ payItemId: overtime, code: 'OT', name: 'Overtime', unit: 'hour' }],
  amountItems: [
    { payItemId: commission, code: 'COMM', name: 'Commission', kind: 'earning', calcMethod: 'each_run', statutoryKey: null },
    { payItemId: 'b9', code: 'HOUSE', name: 'House allowance', kind: 'earning', calcMethod: 'fixed', statutoryKey: null },
    { payItemId: 'b8', code: 'PAYE', name: 'PAYE', kind: 'deduction', calcMethod: 'by_law', statutoryKey: 'paye' },
  ],
})

function signedIn(routes: Parameters<typeof fakeApi>[0] = {}, permissions?: string[]) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands]),
    [`GET ${api}`]: () => json(200, run),
    [`GET ${api}/people`]: () => json(200, { items: [], total: 0, page: 1, pageSize: 10 }),
    [`GET ${api}/inputs`]: () => json(200, inputs()),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions: permissions.map(permission => ({ permission, allCompanies: true, companyIds: [] })) }) } : {}),
    ...routes,
  })
}
const posted = (calls: ReturnType<typeof fakeApi>, method: string, to: string) => calls.filter(c => c.method === method && c.path === to).map(c => c.body)
async function openInputs() {
  await screen.findByRole('heading', { name: 'Monthly pay run · October 2026', level: 1 })
  await userEvent.click(screen.getByRole('tab', { name: 'Inputs' }))
  return screen.findByRole('region', { name: 'Entered for this pay run' })
}
const cell = (name: string) => screen.getByRole('textbox', { name })

it('shows what is entered: days or hours for those paid by them, overtime, and amounts entered each run, with leavers and late entries marked', async () => {
  signedIn()
  const { container } = renderApp(path)
  const grid = await openInputs()
  expect(within(grid).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['Employee', 'Days or hours worked', 'Overtime (hours)', 'Commission (KES)'])
  expect(cell('Days worked for Peter Kamau')).toHaveValue('20')
  expect(cell('Days worked for Peter Kamau')).toHaveAttribute('placeholder', '22 days')
  expect(cell('Commission (KES) for Peter Kamau')).toHaveValue('1500')
  expect(within(within(grid).getByText('Brian Mwangi').closest('tr')!).getByText('Salary')).toBeInTheDocument()
  expect(screen.queryByRole('textbox', { name: /worked for Brian Mwangi/ })).not.toBeInTheDocument()
  expect(cell('Hours worked for Otieno Ouma')).toHaveAttribute('placeholder', 'Hours')
  expect(within(grid).getByText('Otieno Ouma').closest('tr')).toHaveTextContent('Left 28 Oct 2026')
  expect(within(grid).getByText('Brian Mwangi').closest('tr')).toHaveTextContent('Next run')
  expect(screen.getByText('Entries marked Next run came after the cut-off on 25 Oct 2026, so they are paid in the next run.')).toBeInTheDocument()
  expect(screen.getByText('Saving sends this pay run back to draft, to be worked out again.')).toBeInTheDocument()
  await expectNoA11yViolations(container)
})

it('saves only what changed; a worked-out run goes back to draft', async () => {
  let status = 'calculated'
  const calls = signedIn({
    [`GET ${api}`]: () => json(200, { ...run, status }),
    [`PUT ${api}/inputs`]: () => { status = 'draft'; return json(200, inputs()) },
  })
  renderApp(path)
  await openInputs()
  await userEvent.clear(cell('Days worked for Peter Kamau'))
  await userEvent.type(cell('Days worked for Peter Kamau'), '18')
  await userEvent.type(cell('Overtime (hours) for Brian Mwangi'), '5.5')
  await userEvent.clear(cell('Commission (KES) for Peter Kamau'))
  await userEvent.click(screen.getByRole('button', { name: 'Save' }))
  expect(await screen.findByText('Entries saved')).toBeInTheDocument()
  expect(posted(calls, 'PUT', `${api}/inputs`)).toEqual([{ entries: [
    { employeeId: peter, payItemId: null, quantity: 18, amount: null },
    { employeeId: peter, payItemId: commission, quantity: null, amount: null },
    { employeeId: brian, payItemId: overtime, quantity: 5.5, amount: null },
  ] }])
  await vi.waitFor(() => expect(within(screen.getByRole('list', { name: 'Pay run steps' })).getByText('Draft').closest('li')).toHaveAttribute('aria-current', 'step'))
})

it('checks what is typed before saving, and shows the server\'s problems on their cells', async () => {
  const calls = signedIn({
    [`PUT ${api}/inputs`]: () => json(400, {
      code: 'PAY-2025', message: "Some pay run entries don't look right. Check the fields marked.", reference: 'r1',
      fields: { 'entries[0].quantity': 'Enter the days or hours worked, from 0 to 744, with up to two decimals.' },
    }),
  })
  renderApp(path)
  await openInputs()
  await userEvent.type(cell('Overtime (hours) for Peter Kamau'), '800')
  await userEvent.type(cell('Commission (KES) for Brian Mwangi'), '{Backspace>4}12.345')
  await userEvent.click(screen.getByRole('button', { name: 'Save' }))
  expect(cell('Overtime (hours) for Peter Kamau')).toHaveAccessibleDescription('Enter 0 to 744, with up to two decimals.')
  expect(cell('Commission (KES) for Brian Mwangi')).toHaveAccessibleDescription('Enter an amount of 0 or more, with up to two decimals.')
  expect(posted(calls, 'PUT', `${api}/inputs`)).toHaveLength(0)

  await userEvent.clear(cell('Overtime (hours) for Peter Kamau'))
  await userEvent.type(cell('Overtime (hours) for Peter Kamau'), '700')
  await userEvent.clear(cell('Commission (KES) for Brian Mwangi'))
  await userEvent.type(cell('Commission (KES) for Brian Mwangi'), '4000')
  await userEvent.click(screen.getByRole('button', { name: 'Save' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Code PAY-2025')
  expect(cell('Overtime (hours) for Peter Kamau')).toHaveAccessibleDescription('Enter the days or hours worked, from 0 to 744, with up to two decimals.')
})

it('copies days and hours from the last run; with none before, says so', async () => {
  let answer = apiError(409, 'PAY-2026', "There's no approved pay run on this schedule before this one to copy days and hours from.")
  const calls = signedIn({ [`POST ${api}/inputs/copy-previous`]: () => answer })
  renderApp(path)
  await openInputs()
  await userEvent.click(screen.getByRole('button', { name: 'Copy from last run' }))
  expect(await screen.findByRole('alert')).toHaveTextContent("There's no approved pay run on this schedule before this one")

  const copied = inputs()
  copied.people[2] = { ...(copied.people[2] as object), entries: [entry({ quantity: 160, source: 'copied' })] }
  answer = json(200, copied)
  await userEvent.click(screen.getByRole('button', { name: 'Copy from last run' }))
  expect(await screen.findByText('Copied from the last run')).toBeInTheDocument()
  expect(cell('Hours worked for Otieno Ouma')).toHaveValue('160')
  expect(posted(calls, 'POST', `${api}/inputs/copy-previous`)).toHaveLength(2)
})

it('someone who may only look, or a run that is sent, shows the entries without fields', async () => {
  signedIn({ [`GET ${api}`]: () => json(200, { ...run, canManage: false }) }, ['pay_runs.view'])
  renderApp(path)
  const grid = await openInputs()
  expect(within(grid).queryAllByRole('textbox')).toHaveLength(0)
  expect(within(grid).getByText('Peter Kamau').closest('tr')).toHaveTextContent('20')
  for (const name of ['Save', 'Copy from last run']) expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
  expect(screen.queryByText('Saving sends this pay run back to draft, to be worked out again.')).not.toBeInTheDocument()
})

it('saving days keeps a change to basic pay made in review', async () => {
  const withEdit = inputs()
  withEdit.people[0] = { ...(withEdit.people[0] as object), entries: [entry({ quantity: 20, amount: 15000, reason: 'Two days unpaid leave' })] }
  const calls = signedIn({ [`GET ${api}/inputs`]: () => json(200, withEdit), [`PUT ${api}/inputs`]: () => json(200, withEdit) })
  renderApp(path)
  await openInputs()
  await userEvent.clear(cell('Days worked for Peter Kamau'))
  await userEvent.type(cell('Days worked for Peter Kamau'), '21')
  await userEvent.click(screen.getByRole('button', { name: 'Save' }))
  await vi.waitFor(() => expect(posted(calls, 'PUT', `${api}/inputs`)).toEqual([{ entries: [
    { employeeId: peter, payItemId: null, quantity: 21, amount: 15000, reason: 'Two days unpaid leave' },
  ] }]))
})
