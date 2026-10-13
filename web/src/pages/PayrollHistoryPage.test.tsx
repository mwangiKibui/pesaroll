import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { company, fakeApi, json, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())
beforeEach(() => { URL.createObjectURL = vi.fn(() => 'blob:template'); URL.revokeObjectURL = vi.fn() })

const restaurant = company()
const centrail = company({ id: '0199c0de-0000-7000-8000-0000000000c2', legalName: 'Centrail Ltd' })
const year = new Date().getFullYear()
const batch = {
  id: '0199c0de-0000-7000-8000-00000000b001', companyId: restaurant.id, fileName: 'old-payroll.xlsx', rowCount: 2, firstPeriod: `${year}-01-01`, lastPeriod: `${year}-01-01`,
  grossTotal: 135000, payeTotal: 20000, createdAt: `${year}-01-20T09:00:00Z`,
}

function withHistory(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    'POST /api/auth/refresh': () => orgToken(),
    'GET /api/companies': () => json(200, [restaurant, centrail]),
    'GET /api/payroll-history/batches': () => json(200, [batch]),
    ...routes,
  })
}

it('lists the imports, for every company or those shown, with Upload in the table\'s header', async () => {
  const calls = withHistory()
  const { container } = renderApp('/setup/payroll-history')

  expect(await screen.findByRole('heading', { name: 'Bring in your payroll history' })).toBeInTheDocument()
  const imports = await screen.findByRole('region', { name: 'Payroll history imports' })
  expect(within(imports).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Company', 'File', 'Months', 'Rows', 'Gross pay', 'PAYE', 'Imported on', 'Actions'])
  expect(within(imports).getAllByRole('cell').map(c => c.textContent).slice(0, 7))
    .toEqual(['Mama Oliech Restaurant Ltd', 'old-payroll.xlsx', `January ${year}`, '2', 'KES 135,000', 'KES 20,000', `20 Jan ${year}`])
  expect(screen.getByRole('navigation', { name: 'Pages of Payroll history imports' })).toBeInTheDocument()
  expect(screen.getAllByRole('button', { name: /Upload|Add/ }).map(b => b.textContent)).toEqual(['Upload payroll history'])
  expect(calls.some(c => c.path === '/api/payroll-history/batches' && !c.url.includes('companyIds'))).toBe(true)

  // Only one company's.
  await userEvent.click(screen.getByRole('button', { name: /Show/ }))
  await userEvent.click(screen.getByRole('option', { name: /Mama Oliech/ }))
  await vi.waitFor(() => expect(calls.some(c => c.path === '/api/payroll-history/batches' && c.url.includes('companyIds='))).toBe(true))
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Payroll history').closest('a')).toHaveAttribute('aria-current', 'page')
  await expectNoA11yViolations(container)
})

it('with nothing imported yet, says what to upload', async () => {
  withHistory({ 'GET /api/payroll-history/batches': () => json(200, []) })
  renderApp('/setup/payroll-history')
  expect(await screen.findByRole('heading', { name: 'No payroll history yet' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Upload payroll history' })).toBeInTheDocument()
})

it('deleting an import asks first, then removes its months', async () => {
  const calls = withHistory({ [`DELETE /api/payroll-history/batches/${batch.id}`]: () => new Response(null, { status: 204 }) })
  renderApp('/setup/payroll-history')
  await userEvent.click(await screen.findByRole('button', { name: 'Delete old-payroll.xlsx' }))
  const dialog = screen.getByRole('alertdialog', { name: 'Delete the import old-payroll.xlsx?' })
  expect(within(dialog).getByText(/People it added stay, as former employees/)).toBeInTheDocument()
  await userEvent.click(within(dialog).getByRole('button', { name: 'Delete import' }))
  await vi.waitFor(() => expect(calls.some(c => c.method === 'DELETE' && c.path === `/api/payroll-history/batches/${batch.id}`)).toBe(true))
  expect(await screen.findByText('Import deleted')).toBeInTheDocument()
})

it('uploads for a company chosen first, and imports with the file name', async () => {
  const calls = withHistory({
    'GET /api/payroll-history/template': () => new Response(new Blob(['x'])),
    'POST /api/payroll-history/import/read': () => json(200, {
      rows: [{ line: 2, cells: { kraPin: 'A012345678Z', name: 'Wanjiru Kamau', month: `${year}-02`, basic: '85000', gross: '85000', nssf: '4320', shif: '2338', ahl: '1275', paye: '15000', net: '70000' } }],
      problems: [],
      notes: [{ line: 2, column: 'net', message: 'Net pay is KES 70,000, but gross less deductions is KES 62,067.', kind: 'warning' }],
    }),
    'POST /api/payroll-history/import/check': () => json(200, { problems: [], notes: [] }),
    'POST /api/payroll-history/import': () => json(200, { imported: 1 }),
  })
  renderApp('/setup/payroll-history')
  await userEvent.click(await screen.findByRole('button', { name: 'Upload payroll history' }))
  const upload = await screen.findByRole('dialog', { name: 'Upload payroll history' })
  expect(within(upload).getByRole('button', { name: 'Download template' })).toBeDisabled()
  await userEvent.click(within(upload).getByRole('combobox', { name: 'Company' }))
  await userEvent.click(screen.getAllByRole('option', { name: 'Centrail Ltd' })[0])
  await userEvent.upload(within(upload).getByLabelText(/Choose a file/), new File(['x'], 'old-payroll.xlsx'))
  await userEvent.click(screen.getByRole('button', { name: 'Proceed to preview' }))

  const review = await screen.findByRole('dialog', { name: /Review 1 row/ })
  expect(within(review).getByText('1 row to check.')).toBeInTheDocument()   // a warning doesn't block
  await userEvent.click(within(review).getByRole('button', { name: 'Import 1 row' }))
  await vi.waitFor(() => expect(calls.some(c => c.path === '/api/payroll-history/import')).toBe(true))
  expect(calls.find(c => c.path === '/api/payroll-history/import')!.body).toMatchObject({ fileName: 'old-payroll.xlsx', rows: [{ cells: { company: 'Centrail Ltd' } }] })
  expect(await screen.findByText('1 row of payroll history imported')).toBeInTheDocument()
})
