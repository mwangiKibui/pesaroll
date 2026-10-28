import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../../test/a11y'
import { apiError, company, fakeApi, json, me, orgToken, payRunDetail, payRunPerson, payRunSummary } from '../../test/fakeApi'
import { renderApp } from '../../test/renderApp'

beforeEach(() => vi.stubEnv('VITE_FEATURES', 'payRuns'))
afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); localStorage.clear() })

const westlands = company({ id: '0199c0de-0000-7000-8000-0000000000c1', legalName: 'Westlands Ltd' })
const grant = (permission: string) => ({ permission, allCompanies: true, companyIds: [] as string[] })
const run = payRunDetail({ companyId: westlands.id })
const path = `/pay/runs/${run.id}`
const api = `/api/pay-runs/${run.id}`
const wanjiru = payRunPerson()
const otieno = payRunPerson({
  employeeId: '0199c0de-0000-7000-8000-00000000a502', staffNo: 'EMP-02', name: 'Otieno Ouma', notes: ['joined', 'owed', 'adjusted'], basicPay: 30000, grossPay: 41500,
  paye: 4200, nssfEmployee: 1800, shif: 1141.25, housingLevyEmployee: 622.5, netPay: 33736.25,
  lines: [
    { kind: 'earning', code: 'BASIC', name: 'Basic pay', statutoryKey: null, base: 30000, rate: null, amount: 30000, monthToDate: null, earlier: 0, quantity: null, unitRate: null, reason: null, owedFrom: null, owedTo: null },
    { kind: 'earning', code: 'OT', name: 'Overtime', statutoryKey: null, base: null, rate: null, amount: 5000, monthToDate: null, earlier: 0, quantity: 10, unitRate: 500, reason: null, owedFrom: null, owedTo: null },
    { kind: 'earning', code: 'COMM', name: 'Commission', statutoryKey: null, base: null, rate: null, amount: 4000, monthToDate: null, earlier: 0, quantity: null, unitRate: null, reason: 'Agreed with the manager', owedFrom: null, owedTo: null },
    { kind: 'earning', code: 'OT', name: 'Overtime', statutoryKey: null, base: null, rate: null, amount: 2500, monthToDate: null, earlier: 0, quantity: 5, unitRate: 500, reason: null, owedFrom: '2026-09-26', owedTo: '2026-09-30' },
    { kind: 'deduction', code: 'NSSF', name: 'NSSF', statutoryKey: 'nssf_employee', base: 30000, rate: 0.06, amount: 1800, monthToDate: 1800, earlier: 0, quantity: null, unitRate: null, reason: null, owedFrom: null, owedTo: null },
    { kind: 'employer_cost', code: 'NSSF-ER', name: 'NSSF (employer)', statutoryKey: 'nssf_employer', base: 30000, rate: 0.06, amount: 1800, monthToDate: 1800, earlier: 0, quantity: null, unitRate: null, reason: null, owedFrom: null, owedTo: null },
  ],
})
const people = (items: unknown[]) => json(200, { items, total: items.length, page: 1, pageSize: 10 })
const request = (overrides: Record<string, unknown> = {}) => ({
  id: '0199c0de-0000-7000-8000-0000000b0001', kind: 'pay_run', kindName: { en: 'Pay runs', sw: 'Malipo ya mishahara' }, companyId: westlands.id, company: 'Westlands Ltd',
  subjectId: run.id, status: 'submitted', currentStep: 1, steps: 1, sentBy: 'Wanjiru Kamau', sentAt: '2026-10-27T07:00:00Z', declineReason: null,
  canDecide: true, canCancel: false, waitingFor: ['Amina Otieno'], history: [], ...overrides,
})

function signedIn(routes: Parameters<typeof fakeApi>[0] = {}, permissions?: ReturnType<typeof grant>[]) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [westlands]),
    [`GET ${api}`]: () => json(200, run),
    [`GET ${api}/people`]: () => people([otieno, wanjiru]),
    ...(permissions ? { 'GET /api/auth/me': () => json(200, { ...me, permissions }) } : {}),
    ...routes,
  })
}
const posted = (calls: ReturnType<typeof fakeApi>, to: string) => calls.filter(c => c.method === 'POST' && c.path === to).map(c => c.body)
const heading = () => screen.findByRole('heading', { name: 'Monthly pay run · October 2026', level: 1 })

it('a worked-out run: its steps, totals, the cut-off and what changed, and each person\'s pay', async () => {
  signedIn({
    [`GET ${api}`]: () => json(200, {
      ...run, cutoffOn: '2026-10-25',
      changes: { previousPayRunId: 'p0', previousPeriodStart: '2026-09-01', added: [{ employeeId: otieno.employeeId, staffNo: 'EMP-02', name: 'Otieno Ouma' }], gone: [], grossPayChange: 7500, netPayChange: -1200 },
    }),
  })
  const { container } = renderApp(path)

  expect(await heading()).toBeInTheDocument()
  expect(screen.getByText('Pays 30 Oct 2026 · 14 people')).toBeInTheDocument()
  const steps = screen.getByRole('list', { name: 'Pay run steps' })
  expect(within(steps).getAllByRole('listitem').map(s => s.textContent)).toEqual(['1Draft', '2Awaiting approval', '3Approved', '4Paid'])
  expect(within(steps).getByText('Draft').closest('li')).toHaveAttribute('aria-current', 'step')
  const totals = screen.getByRole('group', { name: 'Totals' })
  expect(totals).toHaveTextContent('Gross payKES 798,000')
  expect(totals).toHaveTextContent('PAYEKES 121,300')
  expect(totals).toHaveTextContent('Net payKES 589,200')
  expect(totals).toHaveTextContent('Employer costsKES 42,670')
  expect(screen.getByText('Changes entered after 25 Oct 2026 are paid in the next run.')).toBeInTheDocument()
  expect(screen.getByText(/Since September 2026: Otieno Ouma joined\. Gross pay up KES 7,500, net pay down KES 1,200\./)).toBeInTheDocument()

  const table = await screen.findByRole('region', { name: 'People in this pay run' })
  expect(within(table).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['Employee', 'Gross pay (KES)', 'PAYE (KES)', 'SHIF (KES)', 'NSSF (KES)', 'Housing Levy (KES)', 'Net pay (KES)', 'Actions'])
  const row = within(table).getByText('Wanjiru Kamau').closest('tr')!
  expect(within(row).getAllByRole('cell').map(c => c.textContent).slice(0, 7))
    .toEqual(['Wanjiru KamauEMP-01', '85,000.00', '16,216.88', '2,337.50', '4,320.00', '1,275.00', '60,850.62'])
  expect(within(table).getByText('Otieno Ouma').closest('tr')).toHaveTextContent('Joined')
  await expectNoA11yViolations(container)
})

it('a person\'s lines say how each was worked out: days × rate, owed from the last period, changed for this run and why', async () => {
  signedIn()
  renderApp(path)
  await userEvent.click(await screen.findByRole('button', { name: 'View Otieno Ouma' }))
  const sheet = await screen.findByRole('dialog', { name: 'Otieno Ouma' })
  const earnings = within(sheet).getByRole('list', { name: 'Earnings' })
  expect(within(earnings).getAllByRole('listitem').map(li => li.textContent)).toEqual([
    'Basic payKES 30,000',
    'Overtime10 × KES 500KES 5,000',
    'CommissionChanged for this run: Agreed with the managerKES 4,000',
    'OvertimeOwed from 26 Sep to 30 Sep 2026 (after that period\'s cut-off)5 × KES 500KES 2,500',
  ])
  expect(within(within(sheet).getByRole('list', { name: 'Deductions' })).getByRole('listitem')).toHaveTextContent('NSSF6% of KES 30,000KES 1,800')
  expect(within(sheet).getByRole('list', { name: 'Employer costs' })).toHaveTextContent('NSSF (employer)')
  expect(sheet).toHaveTextContent('Net payKES 33,736.25')
  expect(within(sheet).getByRole('list', { name: 'To check' })).toHaveTextContent('Joined in this period')
})

it('a draft is worked out in the background: the page waits, then shows the figures', async () => {
  let status = 'draft'
  const calls = signedIn({
    [`GET ${api}`]: () => {
      const now = status
      if (status === 'calculating') status = 'calculated'
      return json(200, { ...run, status: now, calculatedAt: now === 'draft' ? null : run.calculatedAt, totals: now === 'draft' ? { ...run.totals, people: 0 } : run.totals })
    },
    [`POST ${api}/calculate`]: () => { status = 'calculating'; return json(202, { ...run, status: 'calculating' }) },
  })
  renderApp(path)
  expect(await heading()).toBeInTheDocument()
  expect(screen.queryByRole('region', { name: 'People in this pay run' })).not.toBeInTheDocument()
  const next = screen.getByRole('region', { name: "Pay isn't worked out yet" })
  expect(within(next).getAllByRole('term').map(d => `${d.textContent}: ${d.nextElementSibling?.textContent}`))
    .toEqual(['Period: 1 Oct to 31 Oct 2026', 'Pay date: 30 Oct 2026', 'Payroll schedule: Monthly'])
  expect(next).toHaveTextContent('Work it out to see each person\'s pay, their deductions and the totals.')
  await userEvent.click(within(next).getByRole('button', { name: 'Work out pay' }))
  expect(await screen.findByText('Working out pay. This takes a moment.')).toBeInTheDocument()
  expect(await screen.findByRole('region', { name: 'People in this pay run' }, { timeout: 5000 })).toBeInTheDocument()
  expect(posted(calls, `${api}/calculate`)).toHaveLength(1)
})

it('Refresh works a worked-out run out again', async () => {
  const calls = signedIn({ [`POST ${api}/calculate`]: () => json(202, { ...run, status: 'calculating' }) })
  renderApp(path)
  await heading()
  await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))
  expect(await screen.findByText('Working out pay. This takes a moment.')).toBeInTheDocument()
  expect(posted(calls, `${api}/calculate`)).toHaveLength(1)
})

it('sends a worked-out run for approval; with nobody set up to approve, says so with the code', async () => {
  let answer = apiError(409, 'WF-5005', 'Choose who approves pay runs for Westlands Ltd first.')
  const calls = signedIn({ [`POST ${api}/submit`]: () => answer })
  renderApp(path)
  await heading()
  await userEvent.click(screen.getByRole('button', { name: 'Send for approval' }))
  const problem = await screen.findByRole('alert')
  expect(problem).toHaveTextContent('Choose who approves pay runs for Westlands Ltd first.')
  expect(problem).toHaveTextContent('Code WF-5005')
  expect(problem).toHaveTextContent('An administrator sets this up under Organization → Workflows.')

  answer = json(200, { ...run, status: 'awaiting_approval', approvalRequestId: request().id })
  await userEvent.click(screen.getByRole('button', { name: 'Send for approval' }))
  expect(await screen.findByText('Sent for approval')).toBeInTheDocument()
  expect(posted(calls, `${api}/submit`)).toHaveLength(2)
})

it('cancels a run after asking', async () => {
  const calls = signedIn({ [`POST ${api}/cancel`]: () => json(200, { ...run, status: 'cancelled' }) })
  renderApp(path)
  await heading()
  await userEvent.click(screen.getByRole('button', { name: 'Cancel pay run' }))
  await userEvent.click(within(screen.getByRole('alertdialog', { name: 'Cancel this pay run?' })).getByRole('button', { name: 'Cancel pay run' }))
  expect(await screen.findByText('Pay run cancelled')).toBeInTheDocument()
  expect(posted(calls, `${api}/cancel`)).toHaveLength(1)
  expect(screen.queryByRole('button', { name: 'Send for approval' })).not.toBeInTheDocument()
})

it('the approver it waits for can send it back, with a reason', async () => {
  const waiting = { ...run, status: 'awaiting_approval', approvalRequestId: request().id, canManage: false }
  let status = 'awaiting_approval'
  const calls = signedIn({
    [`GET ${api}`]: () => json(200, { ...waiting, status }),
    [`GET /api/approvals/${request().id}`]: () => json(200, request()),
    [`POST /api/approvals/${request().id}/decline`]: () => { status = 'calculated'; return json(200, request({ status: 'declined' })) },
  }, ['pay_run_approvals.manage'].map(grant))
  renderApp(path)
  await heading()
  expect(within(screen.getByRole('list', { name: 'Pay run steps' })).getByText('Awaiting approval').closest('li')).toHaveAttribute('aria-current', 'step')

  await userEvent.click(await screen.findByRole('button', { name: 'Send back' }))
  const back = screen.getByRole('dialog', { name: 'Send this pay run back?' })
  await userEvent.click(within(back).getByRole('button', { name: 'Send back' }))
  expect(within(back).getByText('Say why, so Payroll knows what to fix.')).toBeInTheDocument()
  await userEvent.type(within(back).getByRole('textbox', { name: 'Why' }), 'Overtime for Otieno is missing')
  await userEvent.click(within(back).getByRole('button', { name: 'Send back' }))
  expect(await screen.findByText('Sent back to Payroll')).toBeInTheDocument()
  expect(posted(calls, `/api/approvals/${request().id}/decline`)).toEqual([{ reason: 'Overtime for Otieno is missing' }])

})

it('the approver it waits for approves it', async () => {
  let status = 'awaiting_approval'
  const calls = signedIn({
    [`GET ${api}`]: () => json(200, { ...run, status, approvalRequestId: request().id, canManage: false }),
    [`GET /api/approvals/${request().id}`]: () => json(200, request()),
    [`POST /api/approvals/${request().id}/approve`]: () => { status = 'approved'; return json(200, request({ status: 'approved' })) },
  }, ['pay_run_approvals.manage'].map(grant))
  renderApp(path)
  await heading()
  await userEvent.click(await screen.findByRole('button', { name: 'Approve pay run' }))
  expect(await screen.findByText('Pay run approved')).toBeInTheDocument()
  expect(posted(calls, `/api/approvals/${request().id}/approve`)).toHaveLength(1)
  await vi.waitFor(() => expect(within(screen.getByRole('list', { name: 'Pay run steps' })).getByText('Approved').closest('li')).toHaveAttribute('aria-current', 'step'))
})

it('whoever it isn\'t waiting for (the preparer, by default) can\'t approve it, and sees who it waits for', async () => {
  signedIn({
    [`GET ${api}`]: () => json(200, { ...run, status: 'awaiting_approval', approvalRequestId: request().id }),
    [`GET /api/approvals/${request().id}`]: () => json(200, request({ canDecide: false })),
  })
  renderApp(path)
  await heading()
  expect(await screen.findByText('Waiting for approval from Amina Otieno')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Approve pay run' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Send for approval' })).not.toBeInTheDocument()
})

it('a run sent back shows why; one that couldn\'t be worked out shows its code', async () => {
  signedIn({ [`GET ${api}`]: () => json(200, { ...run, status: 'draft', declineReason: 'Overtime is missing', calculationError: 'PAY-2010' }) })
  renderApp(path)
  await heading()
  expect(screen.getByText('Sent back: Overtime is missing')).toBeInTheDocument()
  expect(screen.getByText(/We couldn't work out this pay run\. Code PAY-2010/)).toBeInTheDocument()
})

it('lists who was left out and why', async () => {
  signedIn({
    [`GET ${api}`]: () => json(200, { ...run, leftOut: [{ employeeId: 'e9', staffNo: 'EMP-09', name: 'Achieng Odhiambo', reason: 'no_pay' }] }),
  })
  renderApp(path)
  await heading()
  expect(within(screen.getByRole('list', { name: 'Left out' })).getByRole('listitem')).toHaveTextContent('Achieng OdhiamboEMP-09No basic pay set')
})

it('an approved run can be corrected for chosen people, which opens the correction', async () => {
  const fix = payRunDetail({ id: '0199c0de-0000-7000-8000-0000000a0009', companyId: westlands.id, kind: 'correction', status: 'draft', correctsPayRunId: run.id })
  const calls = signedIn({
    [`GET ${api}`]: () => json(200, { ...run, status: 'approved' }),
    [`POST ${api}/corrections`]: () => json(201, fix),
    [`GET /api/pay-runs/${fix.id}`]: () => json(200, fix),
  })
  renderApp(path)
  await heading()
  await userEvent.click(screen.getByRole('button', { name: 'Correct' }))
  const sheet = screen.getByRole('dialog', { name: 'Correct this pay run' })
  await userEvent.click(within(sheet).getByRole('button', { name: 'Start correction' }))
  expect(within(sheet).getByText('Choose at least one person.')).toBeInTheDocument()
  await userEvent.click(within(sheet).getByRole('button', { name: /People to correct/ }))
  await userEvent.click(await screen.findByRole('option', { name: 'Otieno Ouma' }))
  await userEvent.keyboard('{Escape}')
  await userEvent.click(within(sheet).getByRole('button', { name: 'Start correction' }))
  expect(await screen.findByRole('heading', { name: 'Monthly correction · October 2026', level: 1 })).toBeInTheDocument()
  expect(posted(calls, `${api}/corrections`)).toEqual([{ employeeIds: [otieno.employeeId] }])
})

it('someone who may only look has no actions', async () => {
  signedIn({ [`GET ${api}`]: () => json(200, { ...run, canManage: false }) }, ['pay_runs.view'].map(grant))
  renderApp(path)
  await heading()
  for (const name of ['Work out pay', 'Send for approval', 'Cancel pay run', 'Refresh']) expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
})

it('a run that can\'t be shown (another company, or a draft to an approver) says so', async () => {
  signedIn({ [`GET ${api}`]: () => apiError(404, 'PAY-2016', "We couldn't find that pay run. It may be at a company you can't see.") })
  renderApp(path)
  expect(await screen.findByText("We couldn't find that pay run. It may be at a company you can't see.")).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Back to pay runs' })).toBeInTheDocument()
})

it('the list opens a run', async () => {
  signedIn({ 'GET /api/pay-runs': () => json(200, { items: [payRunSummary({ companyId: westlands.id })], total: 1, page: 1, pageSize: 10 }) })
  renderApp('/pay/runs')
  await userEvent.click(await screen.findByRole('link', { name: 'October 2026' }))
  expect(await heading()).toBeInTheDocument()
})
