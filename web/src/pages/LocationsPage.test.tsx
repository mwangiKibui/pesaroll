import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, location, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())
// The template downloads through a temporary link to a blob.
beforeEach(() => { URL.createObjectURL = vi.fn(() => 'blob:template'); URL.revokeObjectURL = vi.fn() })

const signedIn = { 'POST /api/auth/refresh': () => orgToken() }
const restaurant = company()
const catering = company({ id: '0199c0de-0000-7000-8000-00000000c002', legalName: 'Oliech Catering Ltd', tradingName: null })
const hq = location()
const cateringHq = location({ id: '0199c0de-0000-7000-8000-00000000d002', code: 'HQ-02', companyIds: [catering.id], mainFor: [catering.id] })

const drawer = () => screen.getByRole('dialog')
const table = () => screen.getByRole('region', { name: 'Locations' })

async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(drawer()).getByRole('combobox', { name }), search)
  await userEvent.click(within(drawer()).getByRole('option', { name: option }))
}

function oneCompany(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({ ...signedIn, 'GET /api/companies': () => json(200, [restaurant]), 'GET /api/locations': () => json(200, [hq]), ...routes })
}

it('lists the head office made with the company; with one company there is no company selector', async () => {
  oneCompany()
  const { container } = renderApp('/setup/locations')

  expect(await screen.findByRole('heading', { name: 'Where do your people work?' })).toBeInTheDocument()
  expect(await within(table()).findByText('HQ-01')).toBeInTheDocument()
  expect(within(table()).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Location', 'Type', 'Town and county', 'Applies to', 'Main location for', 'Actions'])
  const row = within(table()).getByText('HQ-01').closest('tr')!
  expect(within(row).getAllByRole('cell').map(c => c.textContent).slice(0, 5))
    .toEqual(['Head officeHQ-01', 'Head office', 'Nairobi, Nairobi', 'Mama Oliech Restaurant Ltd', 'Mama Oliech Restaurant Ltd'])
  expect(screen.queryByRole('button', { name: /Show companies/ })).not.toBeInTheDocument()
  const steps = screen.getByRole('list', { name: 'Setup steps' })
  expect(within(steps).getByText('Locations').closest('a')).toHaveAttribute('aria-current', 'page')
  expect(within(steps).getByText('Locations').closest('li')).toHaveTextContent('✓')
  await expectNoA11yViolations(container)
})

it('with several companies, the selector filters the table to the chosen company', async () => {
  fakeApi({ ...signedIn, 'GET /api/companies': () => json(200, [restaurant, catering]), 'GET /api/locations': () => json(200, [hq, cateringHq]) })
  renderApp('/setup/locations')
  expect(await within(await screen.findByRole('region', { name: 'Locations' })).findByText('HQ-02')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: /Show companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /All companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /Oliech Catering Ltd/ }))
  await userEvent.keyboard('{Escape}')

  expect(within(table()).getByText('HQ-02')).toBeInTheDocument()
  expect(within(table()).queryByText('HQ-01')).not.toBeInTheDocument()
})

it('adds a location; there is no minimum-wage area to fill in', async () => {
  let locations = [hq]
  const shop = location({ id: '0199c0de-0000-7000-8000-00000000d003', code: 'MOM-01', name: 'Mombasa shop', regionCode: '001', town: 'Mombasa', mainFor: [] })
  const calls = oneCompany({
    'GET /api/locations': () => json(200, locations),
    'POST /api/locations': () => { locations = [hq, shop]; return json(201, shop) },
  })
  renderApp('/setup/locations')
  await userEvent.click(await screen.findByRole('button', { name: 'Add location' }))

  expect(within(drawer()).getByRole('heading', { name: 'Add location' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('textbox', { name: 'Code optional' })).toHaveAccessibleDescription(/Leave it empty/)
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Location name' }), 'Mombasa shop')
  await choose('County', 'mom', 'Mombasa')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Town optional' }), 'Mombasa')
  expect(within(drawer()).queryByRole('combobox', { name: 'Minimum-wage area' })).not.toBeInTheDocument()
  expect(within(drawer()).getByRole('combobox', { name: 'Type' })).toHaveValue('Branch')
  await userEvent.clear(within(drawer()).getByRole('combobox', { name: 'Type' }))
  await choose('Type', 'ware', 'Warehouse')
  expect(within(drawer()).getByRole('checkbox', { name: 'Main location for Mama Oliech Restaurant Ltd' })).not.toBeChecked()
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add location' }))

  expect(await screen.findByText('Location added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/locations')?.body).toMatchObject({
    name: 'Mombasa shop', kind: 'warehouse', regionCode: '001', town: 'Mombasa', allCompanies: false, companyIds: [restaurant.id], mainFor: [],
  })
  expect(await within(table()).findByText('MOM-01')).toBeInTheDocument()
})

it('checks the details before sending; problems the server finds show under their fields', async () => {
  let reply = () => apiError(409, 'ONB-3513', 'Another location already has this code. Choose a different one or leave it empty.')
  const calls = oneCompany({ 'POST /api/locations': () => reply() })
  renderApp('/setup/locations')
  await userEvent.click(await screen.findByRole('button', { name: 'Add location' }))

  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add location' }))
  expect(within(drawer()).getByRole('textbox', { name: 'Location name' })).toHaveAccessibleDescription("Enter the location's name (2 to 120 characters).")
  expect(within(drawer()).getByRole('combobox', { name: 'County' })).toHaveAccessibleDescription('Choose a county from the list.')
  expect(calls.filter(c => c.method === 'POST' && c.path === '/api/locations')).toHaveLength(0)

  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Location name' }), 'Mombasa shop')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Code optional' }), 'hq-01')
  await choose('County', 'mom', 'Mombasa')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add location' }))
  expect(await within(drawer()).findByText('Another location already has this code. Choose a different one or leave it empty.')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/locations')?.body).toMatchObject({ code: 'HQ-01' })

  reply = () => json(400, { code: 'ONB-3512', message: 'Some location details don\'t look right.', reference: '7F3K92QD', fields: { mainFor: 'A location can only be the main location of a company it serves.' } })
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add location' }))
  expect(await within(drawer()).findByText('A location can only be the main location of a company it serves.')).toBeInTheDocument()
})

it('edits a location', async () => {
  const calls = oneCompany({ [`PATCH /api/locations/${hq.id}`]: () => json(200, { ...hq, name: 'Nairobi head office' }) })
  renderApp('/setup/locations')

  await userEvent.click(await screen.findByRole('button', { name: 'Edit Head office' }))
  expect(within(drawer()).getByRole('heading', { name: 'Edit location' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('combobox', { name: 'County' })).toHaveValue('Nairobi')
  expect(within(drawer()).getByRole('checkbox', { name: 'Main location for Mama Oliech Restaurant Ltd' })).toBeChecked()
  const name = within(drawer()).getByRole('textbox', { name: 'Location name' })
  await userEvent.clear(name)
  await userEvent.type(name, 'Nairobi head office')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))

  expect(await screen.findByText('Location saved')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'PATCH')?.body).toMatchObject({ code: 'HQ-01', name: 'Nairobi head office', mainFor: [restaurant.id] })
})

it('deleting asks first; a main location is refused with the reason', async () => {
  oneCompany({
    [`DELETE /api/locations/${hq.id}`]: () => apiError(409, 'ONB-3515', 'This is the main location of Mama Oliech Restaurant Ltd. Make another location their main location first.'),
  })
  renderApp('/setup/locations')

  await userEvent.click(await screen.findByRole('button', { name: 'Delete Head office' }))
  await userEvent.click(within(screen.getByRole('alertdialog', { name: 'Delete Head office?' })).getByRole('button', { name: 'Delete location' }))

  expect(await screen.findByText(/This is the main location of Mama Oliech Restaurant Ltd/)).toBeInTheDocument()
  expect(within(table()).getByText('HQ-01')).toBeInTheDocument()
})

it('Proceed goes on to the org structure', async () => {
  oneCompany()
  renderApp('/setup/locations')

  await userEvent.click(await screen.findByRole('button', { name: 'Proceed to Org structure' }))

  expect(await screen.findByRole('heading', { name: 'How is your organization organised?' })).toBeInTheDocument()
})

it('uploads locations: template, then a review of every row before importing for the chosen companies', async () => {
  let locations = [hq, cateringHq]
  const cells = (name: string, kind: string, county: string, main: string) => ({ name, kind, code: '', county, town: '', address: '', main })
  const rows = [{ line: 2, cells: cells('Westlands branch', 'Branch', 'Nairobi', 'No') }, { line: 3, cells: cells('Mombasa shop', 'Warehouse', 'Mombasa', 'Yes') }]
  const calls = fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [restaurant, catering]),
    'GET /api/locations': () => json(200, locations),
    'GET /api/locations/template': () => new Response('xlsx', { status: 200, headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' } }),
    'POST /api/locations/import/read': () => json(200, { rows, problems: [] }),
    'POST /api/locations/import/check': () => json(200, { problems: [] }),
    'POST /api/locations/import': () => { locations = [hq, cateringHq, location({ id: 'x1', code: 'WES-01', name: 'Westlands branch', kind: 'branch', mainFor: [] })]; return json(200, { imported: 2 }) },
  })
  renderApp('/setup/locations')

  await userEvent.click(await screen.findByRole('button', { name: 'Upload locations' }))
  const upload = screen.getByRole('dialog', { name: 'Upload locations' })
  await userEvent.click(within(upload).getByRole('button', { name: 'Download template' }))
  expect(calls.some(c => c.method === 'GET' && c.path === '/api/locations/template')).toBe(true)
  await vi.waitFor(() => expect(URL.createObjectURL).toHaveBeenCalled())

  await userEvent.upload(within(upload).getByLabelText('Choose a file'), new File(['x'], 'branches.xlsx'))
  const review = await screen.findByRole('dialog', { name: 'Review 2 locations' })
  const sent = calls.find(c => c.path === '/api/locations/import/read')!
  expect(((sent.body as FormData).get('file') as Blob).size).toBe(1)   // the file went as form data
  expect(within(review).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Row', 'Location name', 'Type', 'Code', 'County', 'Town', 'Address', 'Main location', 'Actions'])
  expect(within(review).getByRole('combobox', { name: 'Type, row 3' })).toHaveValue('Warehouse')

  // All companies by default; choose only the restaurant.
  await userEvent.click(within(review).getByRole('button', { name: /Apply to/ }))
  await userEvent.click(screen.getByRole('option', { name: /All companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /Mama Oliech Restaurant Ltd/ }))
  await userEvent.keyboard('{Escape}')
  expect(within(review).getByText(/The row marked Main location becomes the main location of the companies you choose/)).toBeInTheDocument()
  await userEvent.click(within(review).getByRole('button', { name: 'Import 2 locations' }))

  expect(await screen.findByText('2 locations imported')).toBeInTheDocument()
  expect(calls.find(c => c.path === '/api/locations/import/check')?.body).toEqual({ rows })
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/locations/import')?.body).toEqual({ rows, allCompanies: false, companyIds: [restaurant.id] })
  expect(await within(table()).findByText('WES-01')).toBeInTheDocument()
})

it('with one company, the review has no company choice', async () => {
  const rows = [{ line: 2, cells: { name: 'Karen branch', kind: '', code: '', county: 'Nairobi', town: '', address: '', main: '' } }]
  const calls = oneCompany({
    'POST /api/locations/import/read': () => json(200, { rows, problems: [] }),
    'POST /api/locations/import/check': () => json(200, { problems: [] }),
    'POST /api/locations/import': () => json(200, { imported: 1 }),
  })
  renderApp('/setup/locations')

  await userEvent.click(await screen.findByRole('button', { name: 'Upload locations' }))
  await userEvent.upload(within(screen.getByRole('dialog', { name: 'Upload locations' })).getByLabelText('Choose a file'), new File(['x'], 'branches.csv'))
  const review = await screen.findByRole('dialog', { name: 'Review 1 location' })
  expect(within(review).queryByRole('button', { name: /Apply to/ })).not.toBeInTheDocument()
  await userEvent.click(within(review).getByRole('button', { name: 'Import 1 location' }))

  expect(await screen.findByText('1 location imported')).toBeInTheDocument()
  expect(calls.find(c => c.path === '/api/locations/import')?.body).toMatchObject({ allCompanies: false, companyIds: [restaurant.id] })
})
