import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const signedIn = { 'POST /api/auth/refresh': () => orgToken() }
const saved = company()
const companyPath = `/api/companies/${saved.id}`

const drawer = () => screen.getByRole('dialog')

/** Search a dropdown in the drawer and pick the option. */
async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(drawer()).getByRole('combobox', { name }), search)
  await userEvent.click(within(drawer()).getByRole('option', { name: option }))
}

it('with no companies yet, offers the first one, named after the organization and in Kenya', async () => {
  fakeApi(signedIn)
  const { container } = renderApp('/setup/companies')

  expect(await screen.findByRole('heading', { name: 'Your companies' })).toBeInTheDocument()
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Companies').closest('li')).toHaveAttribute('aria-current', 'step')
  expect(within(steps).getByText('Organization').closest('li')).toHaveTextContent('✓')
  expect(await screen.findByRole('heading', { name: 'Add your first company' })).toBeInTheDocument()
  await expectNoA11yViolations(container)

  await userEvent.click(screen.getByRole('button', { name: 'Add company' }))

  expect(within(drawer()).getByRole('heading', { name: 'Add company' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'Legal name' })).toHaveValue('Mama Oliech Group')
  expect(within(drawer()).getByRole('combobox', { name: 'Country' })).toHaveValue('Kenya')
  // The numbers come from the country: payroll needs the first three, the last is optional.
  const pin = within(drawer()).getByRole('textbox', { name: 'KRA PIN' })
  expect(pin).toHaveAttribute('placeholder', 'Your KRA PIN')
  expect(pin).toHaveAccessibleDescription(/Used for PAYE.*Needed before your first payroll/)
  expect(within(drawer()).getByRole('textbox', { name: 'NSSF employer number' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'Business registration number optional' })).toBeInTheDocument()
  const county = within(drawer()).getByRole('combobox', { name: 'County optional' })
  expect(county).toHaveAttribute('placeholder', 'Your county')
  await userEvent.type(county, 'nai')
  expect(within(drawer()).getAllByRole('option').map(o => o.textContent)).toEqual(['Nairobi'])
  await expectNoA11yViolations(container)
})

it('adds a company; numbers it still needs show against it, and the step is ticked', async () => {
  const calls = fakeApi({ ...signedIn, 'POST /api/companies': () => json(201, saved) })
  renderApp('/setup/companies')
  await userEvent.click(await screen.findByRole('button', { name: 'Add company' }))

  const legal = within(drawer()).getByRole('textbox', { name: 'Legal name' })
  await userEvent.clear(legal)
  await userEvent.type(legal, 'Mama Oliech Restaurant Ltd ')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Trading name optional' }), 'Mama Oliech')
  await choose('Industry optional', 'restaurant', 'Restaurants, bars and clubs')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'KRA PIN' }), 'p051234567x')
  await choose('County optional', 'nairobi', 'Nairobi')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add company' }))

  expect(await screen.findByText('Company added')).toBeInTheDocument()
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  const body = calls.find(c => c.method === 'POST' && c.path === '/api/companies')?.body as Record<string, unknown>
  expect(body).toMatchObject({ legalName: 'Mama Oliech Restaurant Ltd', tradingName: 'Mama Oliech', countryCode: 'KE', industryCode: 'restaurants_bars', regionCode: '047' })
  expect(body.identifiers).toMatchObject({ kra_pin: 'P051234567X' })

  const table = screen.getByRole('region', { name: 'Companies' })
  const row = within(table).getByText('Mama Oliech Restaurant Ltd').closest('tr')!
  expect(row).toHaveTextContent('Missing: NSSF employer number, SHA employer number')
  const headers = within(table).getAllByRole('columnheader').map(h => h.textContent)
  expect(headers).toEqual(['Company', 'Country', 'Industry', 'Location', 'Contact', 'Ready for payroll', 'Actions'])
  const cells = within(row).getAllByRole('cell').map(c => c.textContent)
  expect(cells.slice(0, 5)).toEqual(['Mama Oliech Restaurant LtdMama Oliech', 'Kenya', 'Restaurants, bars and clubs', 'Nairobi, Nairobi', '—'])
  expect(within(screen.getByRole('list', { name: 'Setup steps' })).getByText('Companies').closest('li')).toHaveTextContent('✓')
  expect(screen.getByText(/can't run payroll until/)).toBeInTheDocument()
})

it('checks the name and the numbers against the country before sending anything', async () => {
  const calls = fakeApi(signedIn)
  renderApp('/setup/companies')
  await userEvent.click(await screen.findByRole('button', { name: 'Add company' }))

  await userEvent.clear(within(drawer()).getByRole('textbox', { name: 'Legal name' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'KRA PIN' }), 'A12')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Email optional' }), 'not-an-email')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add company' }))

  expect(within(drawer()).getByRole('textbox', { name: 'Legal name' })).toHaveAccessibleDescription(/Enter the company's legal name \(2 to 160 characters\)/)
  expect(within(drawer()).getByRole('textbox', { name: 'KRA PIN' })).toHaveAccessibleDescription(/KRA PIN doesn't look right. For example P051234567X./)
  expect(within(drawer()).getByRole('textbox', { name: 'Email optional' })).toHaveAccessibleDescription(/Enter a valid email address/)
  expect(calls.filter(c => c.path === '/api/companies' && c.method === 'POST')).toHaveLength(0)
})

it('problems the server finds show under their fields; others show with their code and reference', async () => {
  let reply = () => apiError(409, 'ONB-3508', 'You already have a company with this name.')
  fakeApi({ ...signedIn, 'POST /api/companies': () => reply() })
  renderApp('/setup/companies')
  await userEvent.click(await screen.findByRole('button', { name: 'Add company' }))

  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add company' }))
  expect(await within(drawer()).findByText('You already have a company with this name.')).toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'Legal name' })).toHaveAccessibleDescription(/already have a company with this name/)

  reply = () => json(400, { code: 'ONB-3507', message: 'Some details need fixing.', reference: '7F3K92QD', fields: { phone: 'Enter a valid phone number.' } })
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add company' }))
  expect(await within(drawer()).findByText('Enter a valid phone number.')).toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'Phone number optional' })).toHaveAccessibleDescription('Enter a valid phone number.')

  reply = () => apiError(403, 'ACC-8001', "You don't have permission to do this. Ask an administrator in your organization.")
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add company' }))
  const alert = await within(drawer()).findByRole('alert')
  expect(alert).toHaveTextContent('ACC-8001')
  expect(alert).toHaveTextContent('7F3K92QD')
})

it('edits a company; a number cleared is sent empty so it is removed', async () => {
  const calls = fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [saved]),
    [`PATCH ${companyPath}`]: () => json(200, company({ tradingName: 'Oliech Grill', identifiers: {} })),
  })
  renderApp('/setup/companies')

  await userEvent.click(await screen.findByRole('button', { name: 'Edit Mama Oliech Restaurant Ltd' }))
  expect(within(drawer()).getByRole('heading', { name: 'Edit company' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'KRA PIN' })).toHaveValue('P051234567X')
  const trading = within(drawer()).getByRole('textbox', { name: 'Trading name optional' })
  await userEvent.clear(trading)
  await userEvent.type(trading, 'Oliech Grill')
  await userEvent.clear(within(drawer()).getByRole('textbox', { name: 'KRA PIN' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Company saved')).toBeInTheDocument()
  const body = calls.find(c => c.method === 'PATCH')?.body as { tradingName: string; identifiers: Record<string, string> }
  expect(body.tradingName).toBe('Oliech Grill')
  expect(body.identifiers.kra_pin).toBe('')
  expect(screen.getByRole('region', { name: 'Companies' })).toHaveTextContent('Oliech Grill')
})

it('deletes a company after asking', async () => {
  let companies = [saved]
  const calls = fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, companies),
    [`DELETE ${companyPath}`]: () => { companies = []; return new Response(null, { status: 204 }) },
  })
  renderApp('/setup/companies')

  await userEvent.click(await screen.findByRole('button', { name: 'Delete Mama Oliech Restaurant Ltd' }))
  const confirm = screen.getByRole('alertdialog', { name: 'Delete Mama Oliech Restaurant Ltd?' })
  await userEvent.click(within(confirm).getByRole('button', { name: 'Cancel' }))
  expect(calls.filter(c => c.method === 'DELETE')).toHaveLength(0)

  await userEvent.click(screen.getByRole('button', { name: 'Delete Mama Oliech Restaurant Ltd' }))
  await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete company' }))

  expect(await screen.findByText('Company deleted')).toBeInTheDocument()
  expect(calls.filter(c => c.method === 'DELETE' && c.path === companyPath)).toHaveLength(1)
  expect(await screen.findByRole('heading', { name: 'Add your first company' })).toBeInTheDocument()
})

it('a company with every number shows ready for payroll', async () => {
  fakeApi({ ...signedIn, 'GET /api/companies': () => json(200, [company({ readiness: { ready: true, missing: [] } })]) })
  renderApp('/setup/companies')

  const row = (await screen.findByText('Mama Oliech Restaurant Ltd')).closest('tr')!
  expect(within(row).getAllByRole('cell').at(-2)).toHaveTextContent('Ready')
  expect(screen.queryByText(/can't run payroll until/)).not.toBeInTheDocument()
})

it('continuing needs at least one company, then goes to the dashboard with two steps done', async () => {
  let companies: ReturnType<typeof company>[] = []
  fakeApi({ ...signedIn, 'GET /api/companies': () => json(200, companies), 'POST /api/companies': () => { companies = [saved]; return json(201, saved) } })
  renderApp('/setup/companies')

  await userEvent.click(await screen.findByRole('button', { name: 'Save and continue' }))
  expect(await screen.findByText('Add at least one company to continue.')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: 'Add company' }))
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add company' }))
  await screen.findByText('Company added')
  await userEvent.click(screen.getByRole('button', { name: 'Save and continue' }))

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(screen.getByText('Finish setting up · 2 of 10')).toBeInTheDocument()
  expect(screen.getAllByText('Next: Locations').length).toBeGreaterThan(0)
})

it('the organization step leads on to companies', async () => {
  fakeApi(signedIn)
  renderApp('/setup/organization')

  const link = await screen.findByRole('link', { name: 'Continue to companies' })
  // In the name's card, on the right of Save changes.
  expect(link.parentElement).toContainElement(screen.getByRole('button', { name: 'Save changes' }))
  expect(link.closest('form')).not.toBeNull()
  await userEvent.click(link)

  expect(await screen.findByRole('heading', { name: 'Your companies' })).toBeInTheDocument()
})
