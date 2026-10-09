import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { expectNoA11yViolations } from '../test/a11y'
import { apiError, company, fakeApi, json, location, orgToken } from '../test/fakeApi'
import { renderApp } from '../test/renderApp'

afterEach(() => vi.unstubAllGlobals())

const signedIn = { 'POST /api/auth/refresh': () => orgToken() }
const restaurant = company({ industryCode: 'restaurants_bars' })
const catering = company({ id: '0199c0de-0000-7000-8000-00000000c002', legalName: 'Oliech Catering Ltd', tradingName: null })
const all = { allCompanies: true, companyIds: [] }
const division = { id: 'dv1', code: 'RES-01', name: 'Restaurants', ...all }
const ops = { id: 'dp1', code: 'OPE-01', name: 'Operations', divisionId: 'dv1', parentId: null, costCentre: null, ...all }
const kitchen = { id: 'dp2', code: 'KIT-01', name: 'Kitchen', divisionId: null, parentId: 'dp1', costCentre: 'CC-100', ...all }
const cook = { id: 'ps1', code: 'COO-01', title: 'Cook', departmentId: 'dp2', grade: 'G3', ...all }
const shift = { id: 'un1', code: 'MOR-01', name: 'Morning shift', departmentId: 'dp2', ...all }

const drawer = () => screen.getByRole('dialog')
const panel = (name: string) => screen.getByRole('tabpanel', { name })
const tab = (name: string) => userEvent.click(screen.getByRole('tab', { name }))

async function choose(name: string, search: string, option: string) {
  await userEvent.type(within(drawer()).getByRole('combobox', { name }), search)
  await userEvent.click(within(drawer()).getByRole('option', { name: option }))
}

function withStructure(routes: Parameters<typeof fakeApi>[0] = {}) {
  return fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [restaurant]),
    'GET /api/locations': () => json(200, [location()]),
    'GET /api/structure/divisions': () => json(200, [division]),
    'GET /api/structure/departments': () => json(200, [ops, kitchen]),
    'GET /api/structure/positions': () => json(200, [cook]),
    'GET /api/structure/units': () => json(200, [shift]),
    ...routes,
  })
}

it('an empty structure offers the starting structure for the industry, added with one click', async () => {
  let departments: unknown[] = []
  const calls = fakeApi({
    ...signedIn,
    'GET /api/companies': () => json(200, [restaurant]),
    'GET /api/structure/departments': () => json(200, departments),
    'POST /api/structure/suggestions': () => { departments = [ops]; return json(200, { departments: 4, positions: 8 }) },
  })
  const { container } = renderApp('/setup/structure')

  expect(await screen.findByRole('heading', { name: 'How is your organization organised?' })).toBeInTheDocument()
  const suggested = await screen.findByRole('region', { name: 'Suggested for Restaurants, bars and clubs' })
  expect(suggested).toHaveTextContent('Kitchen, Service, Bar, Admin')
  expect(suggested).toHaveTextContent('8 positions, such as Chef, Cook and Kitchen assistant')
  expect(screen.getAllByRole('tab').map(t => t.textContent)).toEqual(['Divisions', 'Departments', 'Positions', 'Units'])
  expect(screen.getByRole('tab', { name: 'Departments' })).toHaveAttribute('aria-selected', 'true')
  await expectNoA11yViolations(container)

  await userEvent.click(within(suggested).getByRole('button', { name: 'Add these' }))

  expect(await screen.findByText('Added 4 departments and 8 positions')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/structure/suggestions')?.body).toEqual({ allCompanies: true, companyIds: [] })
  expect(await within(panel('Departments')).findByText('Operations')).toBeInTheDocument()
  expect(screen.queryByRole('region', { name: /Suggested for/ })).not.toBeInTheDocument()
})

it('each tab lists its level with what it belongs to', async () => {
  withStructure()
  renderApp('/setup/structure')

  const departments = await screen.findByRole('region', { name: 'Departments' })
  expect(await within(departments).findByText('Kitchen')).toBeInTheDocument()
  expect(within(departments).getAllByRole('columnheader').map(h => h.textContent))
    .toEqual(['Department', 'Division', 'Part of', 'Cost centre', 'Applies to', 'Actions'])
  const row = within(departments).getByText('KIT-01').closest('tr')!
  expect(within(row).getAllByRole('cell').map(c => c.textContent).slice(0, 5)).toEqual(['KitchenKIT-01', '—', 'Operations', 'CC-100', 'All companies'])

  await tab('Positions')
  const positions = screen.getByRole('region', { name: 'Positions' })
  expect(within(positions).getAllByRole('columnheader').map(h => h.textContent)).toEqual(['Position', 'Department', 'Grade', 'Applies to', 'Actions'])
  expect(within(positions).getByText('COO-01').closest('tr')).toHaveTextContent('Kitchen')
  await tab('Units')
  expect(within(screen.getByRole('region', { name: 'Units' })).getByText('Morning shift').closest('tr')).toHaveTextContent('Kitchen')
  await tab('Divisions')
  expect(within(screen.getByRole('region', { name: 'Divisions' })).getByText('RES-01')).toBeInTheDocument()
})

it('adds a department in a division and under another department', async () => {
  const calls = withStructure({ 'POST /api/structure/departments': () => json(201, { ...kitchen, id: 'dp3', code: 'GRI-01', name: 'Grill' }) })
  renderApp('/setup/structure')
  await within(await screen.findByRole('region', { name: 'Departments' })).findByText('Kitchen')

  await userEvent.click(within(panel('Departments')).getByRole('button', { name: 'Add department' }))
  expect(within(drawer()).getByRole('heading', { name: 'Add department' })).toBeInTheDocument()
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Department name' }), 'Grill')
  await choose('Division optional', 'res', 'Restaurants')
  await choose('Part of optional', 'kit', 'Kitchen')
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Cost centre optional' }), 'cc-110')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add department' }))

  expect(await screen.findByText('Department added')).toBeInTheDocument()
  expect(calls.find(c => c.method === 'POST' && c.path === '/api/structure/departments')?.body).toMatchObject({
    name: 'Grill', code: null, divisionId: 'dv1', parentId: 'dp2', costCentre: 'cc-110', allCompanies: false, companyIds: [restaurant.id],
  })
})

it('positions can be shared; a unit must be in a department', async () => {
  const calls = withStructure({ 'POST /api/structure/positions': () => json(201, { ...cook, id: 'ps2', title: 'Clerk', departmentId: null, grade: null }) })
  renderApp('/setup/structure')
  await userEvent.click(await screen.findByRole('tab', { name: 'Positions' }))

  await userEvent.click(await within(panel('Positions')).findByRole('button', { name: 'Add position' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Position title' }), 'Clerk')
  expect(within(drawer()).getByRole('combobox', { name: 'Department optional' })).toHaveAccessibleDescription(/Leave it empty for a position used across departments/)
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add position' }))
  expect(await screen.findByText('Position added')).toBeInTheDocument()
  expect(calls.find(c => c.path === '/api/structure/positions' && c.method === 'POST')?.body).toMatchObject({ title: 'Clerk', departmentId: null })

  await tab('Units')
  await userEvent.click(within(panel('Units')).getByRole('button', { name: 'Add unit' }))
  await userEvent.type(within(drawer()).getByRole('textbox', { name: 'Unit name' }), 'Night shift')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Add unit' }))
  expect(within(drawer()).getByRole('combobox', { name: 'Department' })).toHaveAccessibleDescription('Choose the department this unit is in.')
  expect(calls.filter(c => c.path === '/api/structure/units' && c.method === 'POST')).toHaveLength(0)
})

it('edits an item, and a server problem shows under its field', async () => {
  let reply = () => json(400, { code: 'ONB-3519', message: 'Some details don\'t look right.', reference: '7F3K92QD', fields: { parentId: "A department can't sit under itself or one of its own departments." } })
  const calls = withStructure({ 'PATCH /api/structure/departments/dp1': () => reply() })
  renderApp('/setup/structure')

  await userEvent.click(await within(await screen.findByRole('region', { name: 'Departments' })).findByRole('button', { name: 'Edit Operations' }))
  expect(within(drawer()).getByRole('heading', { name: 'Edit department' })).toBeInTheDocument()
  expect(within(drawer()).getByRole('combobox', { name: 'Division optional' })).toHaveValue('Restaurants')
  await choose('Part of optional', 'kit', 'Kitchen')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))
  expect(await within(drawer()).findByText("A department can't sit under itself or one of its own departments.")).toBeInTheDocument()

  reply = () => json(200, { ...ops, name: 'Operations and service' })
  const name = within(drawer()).getByRole('textbox', { name: 'Department name' })
  await userEvent.clear(name)
  await userEvent.type(name, 'Operations and service')
  await userEvent.click(within(drawer()).getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Department saved')).toBeInTheDocument()
  expect(calls.filter(c => c.method === 'PATCH').at(-1)?.body).toMatchObject({ code: 'OPE-01', name: 'Operations and service' })
})

it('deleting asks first; something in use is refused with what uses it', async () => {
  withStructure({ 'DELETE /api/structure/departments/dp2': () => apiError(409, 'ONB-3522', 'Kitchen still has 1 position and 1 unit. Move or delete them first.') })
  renderApp('/setup/structure')

  await userEvent.click(await within(await screen.findByRole('region', { name: 'Departments' })).findByRole('button', { name: 'Delete Kitchen' }))
  await userEvent.click(within(screen.getByRole('alertdialog', { name: 'Delete Kitchen?' })).getByRole('button', { name: 'Delete' }))

  expect(await screen.findByText('Kitchen still has 1 position and 1 unit. Move or delete them first.')).toBeInTheDocument()
})

it('with several companies, the selector filters every tab', async () => {
  const bar = { ...ops, id: 'dp9', code: 'BAR-01', name: 'Bar', divisionId: null, allCompanies: false, companyIds: [catering.id] }
  withStructure({ 'GET /api/companies': () => json(200, [restaurant, catering]), 'GET /api/structure/departments': () => json(200, [ops, bar]) })
  renderApp('/setup/structure')
  expect(await within(await screen.findByRole('region', { name: 'Departments' })).findByText('BAR-01')).toBeInTheDocument()

  await userEvent.click(screen.getByRole('button', { name: /Show companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /All companies/ }))
  await userEvent.click(screen.getByRole('option', { name: /Mama Oliech Restaurant Ltd/ }))
  await userEvent.keyboard('{Escape}')

  expect(within(panel('Departments')).queryByText('BAR-01')).not.toBeInTheDocument()
  expect(within(panel('Departments')).getByText('OPE-01')).toBeInTheDocument()   // for all companies
})

it('Save and continue goes to the dashboard with four steps done', async () => {
  withStructure()
  renderApp('/setup/structure')

  await userEvent.click(await screen.findByRole('button', { name: 'Save and continue' }))

  expect(await screen.findByRole('heading', { name: /Karibu, Wanjiru/ })).toBeInTheDocument()
  expect(await screen.findByText('Finish setting up · 4 of 10')).toBeInTheDocument()
  expect(screen.getAllByText('Next: Payroll schedules').length).toBeGreaterThan(0)
})

it('the setup steps in the sidebar are links to their pages, done or not', async () => {
  withStructure()
  renderApp('/setup/structure')
  const steps = await screen.findByRole('list', { name: 'Setup steps' })

  expect(within(steps).getByRole('link', { name: /Organization/ })).toHaveAttribute('href', '/setup/organization')
  expect(within(steps).getByRole('link', { name: /Companies/ })).toHaveAttribute('href', '/setup/companies')
  expect(within(steps).getByRole('link', { name: /Locations/ })).toHaveAttribute('href', '/setup/locations')
  expect(within(steps).getByRole('link', { name: /Org structure/ })).toHaveAttribute('aria-current', 'page')
  expect(within(steps).queryByRole('link', { name: /Payroll schedules/ })).not.toBeInTheDocument()   // not built yet

  await userEvent.click(within(steps).getByRole('link', { name: /Companies/ }))
  expect(await screen.findByRole('heading', { name: 'Your companies' })).toBeInTheDocument()
})
